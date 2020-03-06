//-----------------------------------------------------------------------
// <copyright file="RemoteMetricsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    public class RemoteMetricsSpec : AkkaSpec
    {
        private readonly Address _address;
        private readonly ActorSystem _client;
        private readonly IActorRef _subject;


        public RemoteMetricsSpec(ITestOutputHelper output)
            : base(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.log-frame-size-exceeding = 200 b
            akka.remote.dot-netty.tcp = {
                port = 0
                hostname = localhost
            }
            akka.loglevel = DEBUG
            ", output)
        {
            _client = ActorSystem.Create("RemoteMetricsSpec-client", ConfigurationFactory.ParseString(@"
                akka.actor.provider =  ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                 akka.remote.dot-netty.tcp = {
                    port = 0
                    hostname = localhost
                }                
            ").WithFallback(Sys.Settings.Config));

            _address = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _subject = Sys.ActorOf(Props.Create(() => new Subject()).WithDeploy(Deploy.Local), "subject");
            var listener = Sys.ActorOf(Props.Create(() => new InfoEventListener(TestActor)).WithDeploy(Deploy.Local),
                "listener");
            Sys.EventStream.Subscribe(listener, typeof (Info));
        }


        protected override void AfterTermination()
        {
            Shutdown(_client);
        }


        [Fact]
        public void RemoteMetricsMustNotLogMessagesLargerThanFrameSizeExceeding()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_subject.Path.Elements);
            sel.Tell(new byte[200]);
            ExpectMsg<PayloadSize>();
        }

        [Fact]
        public void RemoteMetricsMustLogNewMessageSizeForTheSameMessageTypeLargerThanThePreviousOneOnTheThreshold()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_subject.Path.Elements);
            sel.Tell(new byte[200]);
            ExpectMsg<PayloadSize>();
            sel.Tell(new byte[300]);
            ExpectMsg<NewMaximum>();
        }


        [Fact]
        public void RemoteMetricsMustNotLogMessagesLessThanFrameSizeExceeding()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_subject.Path.Elements);
            sel.Tell(new byte[1]);
            ExpectNoMsg();
        }

        [Fact]
        public void RemoteMetricsMustNotLogTheSameMessageSizeTwice()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_subject.Path.Elements);
            sel.Tell(new byte[200]);
            ExpectMsg<PayloadSize>();
            sel.Tell(new byte[200]);
            ExpectNoMsg();
        }

        private class Subject : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        private class InfoEventListener : ActorBase
        {
            private readonly IActorRef _testActor;

            public InfoEventListener(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override bool Receive(object message)
            {
                if (message is Info)
                {
                    var info = ((Info) message);
                    if (info.Message.ToString().Contains("New maximum payload size for"))
                    {
                        _testActor.Tell(new NewMaximum());
                    }
                    if (info.Message.ToString().Contains("Payload size for"))
                    {
                        _testActor.Tell(new PayloadSize());
                    }
                }

                return true;
            }
        }

        private class PayloadSize
        {
        }

        private class NewMaximum
        {
        }
    }
}
