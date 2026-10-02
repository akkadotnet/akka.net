//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeave2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerLeave2SpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public ClusterSingletonManagerLeave2SpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""cluster""
                akka.remote.log-remote-lifecycle-events = off
            ")
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(ClusterSingletonProxy.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }

        public class EchoStared
        {
            public static EchoStared Instance { get; } = new();
            private EchoStared() { }
        }

        public class Echo : UntypedActor
        {
            private readonly IActorRef _testActorRef;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            public static Props Props(IActorRef testActorRef)
                => Actor.Props.Create(() => new Echo(testActorRef));

            public Echo(IActorRef testActorRef)
            {
                _testActorRef = testActorRef;
            }

            protected override void PreStart()
            {
                //base.PreStart();
                _log.Debug($"Started singleton at [{Cluster.Get(Context.System).SelfAddress}]");
                _testActorRef.Tell("preStart");
            }

            protected override void PostStop()
            {
                _log.Debug($"Stopped singleton at [{Cluster.Get(Context.System).SelfAddress}]");
                _testActorRef.Tell("postStop");
                //base.PostStop();
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "stop":
                        {
                            _testActorRef.Tell("stop");
                            // this is the stop message from singleton manager, but don't stop immediately
                            // will be stopped via PoisonPill from the test to simulate delay
                            break;
                        }
                    default:
                        Sender.Tell(Self);
                        break;
                }
            }
        }
    }

    public class ClusterSingletonManagerLeave2Spec : MultiNodeClusterSpec
    {
        private readonly ClusterSingletonManagerLeave2SpecConfig _config;

        /// <summary>
        /// Watches <c>echoProxy</c> from its own queue, not the test actor's.
        ///
        /// The proxy stops itself when it observes <c>MemberRemoved</c> for its own node
        /// (<see cref="ClusterSingletonProxy"/>'s handler for <see cref="ClusterEvent.MemberRemoved"/>) -
        /// the very same cluster event that drives the <c>RegisterOnMemberRemoved</c> callback below,
        /// which tells the test actor "MemberRemoved". Both reactions are fanned out from one
        /// EventStream publication to two independent subscribers on two dispatchers, so nothing
        /// orders them relative to each other. If the test actor were doing the watching, the
        /// proxy's death-watch <c>Terminated</c> would land in the same single FIFO queue as
        /// "MemberRemoved" and could be dequeued first, stealing the message the
        /// <c>ExpectMsgAsync("MemberRemoved", ...)</c> below is waiting for. A dedicated probe gives
        /// <c>Terminated</c> its own queue, exactly like upstream Akka JVM/Pekko and the sibling
        /// <see cref="ClusterSingletonManagerLeaveSpec"/>.
        /// </summary>
        private TestProbe EchoProxyTerminatedProbe { get; }

        private readonly Lazy<Task<IActorRef>> _echoProxy;

        protected override int InitialParticipantsValueFactory => Roles.Count;

        public ClusterSingletonManagerLeave2Spec()
            : this(new ClusterSingletonManagerLeave2SpecConfig())
        { }

        protected ClusterSingletonManagerLeave2Spec(ClusterSingletonManagerLeave2SpecConfig config)
            : base(config, typeof(ClusterSingletonManagerLeave2Spec))
        {
            _config = config;
            EchoProxyTerminatedProbe = CreateTestProbe();
            _echoProxy = new Lazy<Task<IActorRef>>(async () =>
                await EchoProxyTerminatedProbe.WatchAsync(Sys.ActorOf(ClusterSingletonProxy.Props(
                    singletonManagerPath: "/user/echo",
                    settings: ClusterSingletonProxySettings.Create(Sys)),
                    name: "echoProxy")));
        }

        private async Task JoinAsync(RoleName from, RoleName to)
        {
            await RunOnAsync(async () =>
            {
                Cluster.Join((await NodeAsync(to)).Address);
                CreateSingleton();
            }, from);
        }

        private void CreateSingleton()
        {
            Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: ClusterSingletonManagerLeave2SpecConfig.Echo.Props(TestActor),
                terminationMessage: "stop",
                settings: ClusterSingletonManagerSettings.Create(Sys)),
                name: "echo");
        }

        [MultiNodeFact]
        public async Task ClusterSingletonManagerLeave2Specs()
        {
            await Leaving_ClusterSingletonManager_with_two_nodes_must_handover_to_new_instance();
        }

        private async Task Leaving_ClusterSingletonManager_with_two_nodes_must_handover_to_new_instance()
        {
            await JoinAsync(_config.First, _config.First);
            await RunOnAsync(async () =>
            {
                await WithinAsync(5.Seconds(), async () =>
                {
                    await ExpectMsgAsync("preStart");
                    (await _echoProxy.Value).Tell("hello");
                    await ExpectMsgAsync<IActorRef>();
                });
            }, _config.First);
            await EnterBarrierAsync("first-active");

            await JoinAsync(_config.Second, _config.First);
            await RunOnAsync(async () =>
            {
                await WithinAsync(10.Seconds(), async () =>
                {
                    await AwaitAssertAsync(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(2));
                });
            }, _config.First, _config.Second);
            await EnterBarrierAsync("second-up");

            await JoinAsync(_config.Third, _config.First);
            await RunOnAsync(async () =>
            {
                await WithinAsync(10.Seconds(), async () =>
                {
                    await AwaitAssertAsync(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
                });
            }, _config.First, _config.Second, _config.Third);
            await EnterBarrierAsync("third-up");

            await JoinAsync(_config.Fourth, _config.First);
            await RunOnAsync(async () =>
            {
                await WithinAsync(10.Seconds(), async () =>
                {
                    await AwaitAssertAsync(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(4));
                });
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            await EnterBarrierAsync("fourth-up");

            await JoinAsync(_config.Fifth, _config.First);
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(5));
            });
            await EnterBarrierAsync("all-up");

            await RunOnAsync(async () =>
            {
                Cluster.RegisterOnMemberRemoved(() => TestActor.Tell("MemberRemoved"));
                Cluster.Leave(Cluster.SelfAddress);
                await ExpectMsgAsync("stop", 10.Seconds()); // from singleton manager, but will not stop immediately
            }, _config.First);

            await RunOnAsync(async () =>
            {
                Cluster.RegisterOnMemberRemoved(() => TestActor.Tell("MemberRemoved"));
                Cluster.Leave(Cluster.SelfAddress);
                await ExpectMsgAsync("MemberRemoved", 10.Seconds());
            }, _config.Second, _config.Fourth);

            await RunOnAsync(async () =>
            {
                for (var i = 1; i <= 3; i++)
                {
                    // Deliberate 1s soak, not a stand-in for a real event: this is a negative
                    // assertion (the singleton must not have restarted on second/third yet
                    // while first still holds it), and there is nothing to await instead.
                    await Task.Delay(1000);
                    // singleton should not be started before old has been stopped
                    Sys.ActorSelection("/user/echo/singleton").Tell(new Identify(i));
                    await ExpectMsgAsync<ActorIdentity>(msg =>
                    {
                        // not started
                        msg.MessageId.Should().Be(i);
                        msg.Subject.ShouldBe(null);
                    });
                }
            }, _config.Second, _config.Third);

            await EnterBarrierAsync("still-running-at-first");

            await RunOnAsync(async () =>
            {
                Sys.ActorSelection("/user/echo/singleton").Tell(PoisonPill.Instance);
                await ExpectMsgAsync("postStop");
                // CoordinatedShutdown makes sure that singleton actors are stopped before Cluster shutdown
                await ExpectMsgAsync("MemberRemoved", 10.Seconds());
                await EchoProxyTerminatedProbe.ExpectTerminatedAsync(await _echoProxy.Value, 10.Seconds());
            }, _config.First);
            await EnterBarrierAsync("stopped");

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("preStart");
            }, _config.Third);
            await EnterBarrierAsync("third-started");

            await RunOnAsync(async () =>
            {
                var p = CreateTestProbe();
                var firstAddress = (await NodeAsync(_config.First)).Address;
                var echoProxy = await _echoProxy.Value;
                await p.WithinAsync(15.Seconds(), async () =>
                {
                    await p.AwaitAssertAsync(async () =>
                    {
                        echoProxy.Tell("hello2", p.Ref);
                        (await p.ExpectMsgAsync<IActorRef>(1.Seconds())).Path.Address.Should().NotBe(firstAddress);
                    });
                });

            }, _config.Third, _config.Fifth);
            await EnterBarrierAsync("third-working");
        }
    }
}
