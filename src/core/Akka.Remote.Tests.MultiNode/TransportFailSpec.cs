//-----------------------------------------------------------------------
// <copyright file="TransportFailSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Remote.Tests.MultiNode
{
    public class TransportFailSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public TransportFailSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(true).WithFallback(ConfigurationFactory.ParseString(@"
              akka.loglevel = INFO
              akka.remote{
                 transport-failure-detector {
                  implementation-class = """+ typeof(TestFailureDetector).AssemblyQualifiedName + @"""
                  heartbeat-interval = 1 s
                }
                retry-gate-closed-for = 3 s
                # Don't trigger watch Terminated
                watch-failure-detector.acceptable-heartbeat-pause = 60 s
                #use-passive-connections = off
              }
            "));
        }

        internal static AtomicBoolean FdAvailable = new(true);

        /// <summary>
        /// Failure detector implementation that will fail when <see cref="FdAvailable"/> is false.
        /// </summary>
        public class TestFailureDetector : FailureDetector
        {
            public TestFailureDetector(Config config, EventStream eventStream)
            {

            }

            private volatile bool _active = false;

            public override bool IsAvailable => _active ? FdAvailable.Value : true;

            public override bool IsMonitoring => _active;

            public override void HeartBeat()
            {
                _active = true;
            }
        }

        public class Subject : ReceiveActor
        {
            public Subject()
            {
                ReceiveAny(_ => Sender.Tell(_));
            }
        }
    }

    public class TransportFailSpec : MultiNodeSpec
    {
        private readonly TransportFailSpecConfig _config;

        public TransportFailSpec() : this(new TransportFailSpecConfig()) { }

        private TransportFailSpec(TransportFailSpecConfig config) : base(config, typeof(TransportFailSpecConfig))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory => 2;

        private async Task<IActorRef> IdentifyAsync(RoleName role, string actorName, TimeSpan timeout)
        {
            var p = CreateTestProbe(); // fresh probe per attempt: a late reply can't satisfy a later attempt
            Sys.ActorSelection(Node(role) / "user" / actorName).Tell(new Identify(actorName), p.Ref);
            return (await p.ExpectMsgAsync<ActorIdentity>(timeout)).Subject;
        }

        [MultiNodeFact]
        public async Task TransportFail_should_reconnect()
        {
            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("actors-started");
                var subject = await IdentifyAsync(_config.Second, "subject", TimeSpan.FromSeconds(3));
                await WatchAsync(subject);
                subject.Tell("hello");
                await ExpectMsgAsync("hello");
            }, _config.First);

            await RunOnAsync(async () =>
            {
                Sys.ActorOf(Props.Create(() => new TransportFailSpecConfig.Subject()), "subject");
                await EnterBarrierAsync("actors-started");
            }, _config.Second);

            await EnterBarrierAsync("watch-established");

            // trigger transport failure detector
            TransportFailSpecConfig.FdAvailable.GetAndSet(false);

            // wait for ungated (also later awaitAssert retry)
            await Task.Delay(RARP.For(Sys).Provider.RemoteSettings.RetryGateClosedFor);
            TransportFailSpecConfig.FdAvailable.GetAndSet(true);

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("actors-started2");
                var quarantineProbe = CreateTestProbe();
                Sys.EventStream.Subscribe(quarantineProbe.Ref, typeof(QuarantinedEvent));

                IActorRef subject2 = null;
                // covers the rest of the 3 s gate, one possible re-gate if the peer still sees the failure detector as down, and a slow handshake
                await AwaitAssertAsync(async () =>
                {
                    subject2 = await IdentifyAsync(_config.Second, "subject2", TimeSpan.FromSeconds(2));
                    Assert.NotNull(subject2);
                }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(500));
                await WatchAsync(subject2);
                await quarantineProbe.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
                subject2.Tell("hello2");
                await ExpectMsgAsync("hello2");
                await EnterBarrierAsync("watch-established2");
                await ExpectTerminatedAsync(subject2);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var subject2 = Sys.ActorOf(Props.Create(() => new TransportFailSpecConfig.Subject()), "subject2");
                await EnterBarrierAsync("actors-started2");
                await EnterBarrierAsync("watch-established2");
                subject2.Tell(PoisonPill.Instance);
            }, _config.Second);

            await EnterBarrierAsync("done");
        }
    }
}
