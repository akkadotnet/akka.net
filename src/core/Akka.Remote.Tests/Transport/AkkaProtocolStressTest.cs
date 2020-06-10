//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolStressTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Used to test the throughput of the Akka Protocol
    /// </summary>
    public class AkkaProtocolStressTest : AkkaSpec
    {
        #region Setup / Config

        public static Config AkkaProtocolStressTestConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
                akka {
                  actor.serialize-messages = off
                  actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                  remote.dot-netty.tcp.hostname = ""localhost""
                  remote.log-remote-lifecycle-events = on

                ## Keep gate duration in this test for a low value otherwise too much messages are dropped
                  remote.retry-gate-closed-for = 100 ms
                  remote.transport-failure-detector{
                        threshold = 1.0
                        max-sample-size = 2
                        min-std-deviation = 1 ms
                        ## We want lots of lost connections in this test, keep it sensitive
                        heartbeat-interval = 1 s
                        acceptable-heartbeat-pause = 1 s
                  }
                  remote.dot-netty.tcp.applied-adapters = [""gremlin""]
                  remote.dot-netty.tcp.port = 0
                }");
            }
        }

        sealed class ResendFinal
        {
            private ResendFinal() { }
            private static readonly ResendFinal _instance = new ResendFinal(); 

            public static ResendFinal Instance
            {
                get { return _instance; }
            }
        }

        class SequenceVerifier : UntypedActor
        {
            private int Limit = 100000;
            private int NextSeq = 0;
            private int MaxSeq = -1;
            private int Losses = 0;

            private IActorRef _remote;
            private IActorRef _controller;

            public SequenceVerifier(IActorRef remote, IActorRef controller)
            {
                _remote = remote;
                _controller = controller;
            }

            protected override void OnReceive(object message)
            {
                if (message.Equals("start"))
                {
                    Self.Tell("sendNext");
                }
                else if (message.Equals("sendNext") && NextSeq < Limit)
                {
                    _remote.Tell(NextSeq);
                    NextSeq++;
                    if (NextSeq%2000 == 0)
                        Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(500), Self, "sendNext", Self);
                    else
                        Self.Tell("sendNext");
                }
                else if (message is int || message is long)
                {
                    var seq = Convert.ToInt32(message);
                    if (seq > MaxSeq)
                    {
                        Losses += seq - MaxSeq - 1;
                        MaxSeq = seq;

                        // Due to the (bursty) lossyness of gate, we are happy with receiving at least one message from the upper
                        // half (> 50000). Since messages are sent in bursts of 2000 0.5 seconds apart, this is reasonable.
                        // The purpose of this test is not reliable delivery (there is a gremlin with 30% loss anyway) but respecting
                        // the proper ordering.

                        if (seq > Limit*0.5)
                        {
                            _controller.Tell((MaxSeq, Losses));
                            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Self,
                                ResendFinal.Instance, Self);
                            Context.Become(Done);
                        }
                    }
                    else
                    {
                        _controller.Tell(string.Format("Received out of order message. Previous {0} Received: {1}", MaxSeq, seq));
                    }
                }
            }

            // Make sure the other side eventually "gets the message"
            private void Done(object message)
            {
                if (message is ResendFinal)
                {
                    _controller.Tell((MaxSeq, Losses));
                }
            }
        }

        class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                //BUG: looks like the serializer will by default convert plain numerics sent over the wire into long integers
                if (message is int || message is long)
                {
                    Sender.Tell(message);
                }
            }
        }

        private ActorSystem systemB;
        private IActorRef remote;

        private Address AddressB
        {
            get { return systemB.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress; }
        }

        private RootActorPath RootB
        {
            get { return new RootActorPath(AddressB); }
        }

        private IActorRef Here
        {
            get
            {
                Sys.ActorSelection(RootB / "user" / "echo").Tell(new Identify(null), TestActor);
                return ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(300)).Subject;
            }
        }


        #endregion

        public AkkaProtocolStressTest() : base(AkkaProtocolStressTestConfig)
        {
            systemB = ActorSystem.Create("systemB", Sys.Settings.Config);
            remote = systemB.ActorOf(Props.Create<Echo>(), "echo");
        }

        #region Tests

        [Fact(Skip = "Extremely racy")]
        public void AkkaProtocolTransport_must_guarantee_at_most_once_delivery_and_message_ordering_despite_packet_loss()
        {
            //todo mute both systems for deadletters for any type of message
            EventFilter.DeadLetter().Mute();
            CreateEventFilter(systemB).DeadLetter().Mute();
            var mc =
                RARP.For(Sys)
                    .Provider.Transport.ManagementCommand(new FailureInjectorTransportAdapter.One(AddressB,
                        new FailureInjectorTransportAdapter.Drop(0.1, 0.1)));
            AwaitCondition(() => mc.IsCompleted && mc.Result, TimeSpan.FromSeconds(3));

            var here = Here;

            var tester = Sys.ActorOf(Props.Create(() => new SequenceVerifier(here, TestActor)));
            tester.Tell("start");

            ExpectMsg<(int,int)>(TimeSpan.FromSeconds(60));
        }

        #endregion

        #region Cleanup

        protected override void BeforeTermination()
        {
            EventFilter.Warning(start: "received dead letter").Mute();
            EventFilter.Warning(new Regex("received dead letter.*(InboundPayload|Disassociate)")).Mute();
            systemB.EventStream.Publish(new Mute(new WarningFilter(new RegexMatcher(new Regex("received dead letter.*(InboundPayload|Disassociate)"))),
                new ErrorFilter(typeof(EndpointException)),
                new ErrorFilter(new StartsWithString("AssociationError"))));
            base.BeforeTermination();
        }

        protected override void AfterTermination()
        {
            Shutdown(systemB);
            base.AfterTermination();
        }

        #endregion
    }
}

