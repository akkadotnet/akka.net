//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeave2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;

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
                akka.cluster.auto-down-unreachable-after = off
            ")
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(ClusterSingletonProxy.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }

        public class EchoStared
        {
            public static EchoStared Instance { get; } = new EchoStared();
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
        private readonly Lazy<IActorRef> _echoProxy;

        protected override int InitialParticipantsValueFactory => Roles.Count;

        public ClusterSingletonManagerLeave2Spec()
            : this(new ClusterSingletonManagerLeave2SpecConfig())
        { }

        protected ClusterSingletonManagerLeave2Spec(ClusterSingletonManagerLeave2SpecConfig config)
            : base(config, typeof(ClusterSingletonManagerLeave2Spec))
        {
            _config = config;
            _echoProxy = new Lazy<IActorRef>(() => Watch(Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/echo",
                settings: ClusterSingletonProxySettings.Create(Sys)),
                name: "echoProxy")));
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
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
        public void ClusterSingletonManagerLeave2Specs()
        {
            Leaving_ClusterSingletonManager_with_two_nodes_must_handover_to_new_instance();
        }

        public void Leaving_ClusterSingletonManager_with_two_nodes_must_handover_to_new_instance()
        {
            Join(_config.First, _config.First);
            RunOn(() =>
            {
                Within(5.Seconds(), () =>
                {
                    ExpectMsg("preStart");
                    _echoProxy.Value.Tell("hello");
                    ExpectMsg<IActorRef>();
                });
            }, _config.First);
            EnterBarrier("first-active");

            Join(_config.Second, _config.First);
            RunOn(() =>
            {
                Within(10.Seconds(), () =>
                {
                    AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(2));
                });
            }, _config.First, _config.Second);
            EnterBarrier("second-up");

            Join(_config.Third, _config.First);
            RunOn(() =>
            {
                Within(10.Seconds(), () =>
                {
                    AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
                });
            }, _config.First, _config.Second, _config.Third);
            EnterBarrier("third-up");
            
            Join(_config.Fourth, _config.First);
            RunOn(() =>
            {
                Within(10.Seconds(), () =>
                {
                    AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(4));
                });
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("fourth-up");

            Join(_config.Fifth, _config.First);
            Within(10.Seconds(), () =>
            {
                AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(5));
            });
            EnterBarrier("all-up");

            RunOn(() =>
            {
                Cluster.RegisterOnMemberRemoved(() => TestActor.Tell("MemberRemoved"));
                Cluster.Leave(Cluster.SelfAddress);
                ExpectMsg("stop", 10.Seconds()); // from singleton manager, but will not stop immediately
            }, _config.First);

            RunOn(() =>
            {
                Cluster.RegisterOnMemberRemoved(() => TestActor.Tell("MemberRemoved"));
                Cluster.Leave(Cluster.SelfAddress);
                ExpectMsg("MemberRemoved", 10.Seconds()); 
            }, _config.Second, _config.Fourth);

            RunOn(() =>
            {
                Enumerable.Range(1, 3).ForEach(i =>
                {
                    Thread.Sleep(1000);
                    // singleton should not be started before old has been stopped
                    Sys.ActorSelection("/user/echo/singleton").Tell(new Identify(i));
                    ExpectMsg<ActorIdentity>(msg =>
                    {
                        // not started
                        msg.MessageId.Should().Be(i);
                        msg.Subject.ShouldBe(null);
                    });
                });
            }, _config.Second, _config.Third);

            EnterBarrier("still-running-at-first");

            RunOn(() =>
            {
                Sys.ActorSelection("/user/echo/singleton").Tell(PoisonPill.Instance);
                ExpectMsg("postStop"); 
                // CoordinatedShutdown makes sure that singleton actors are stopped before Cluster shutdown
                ExpectMsg("MemberRemoved", 10.Seconds()); 
                ExpectTerminated(_echoProxy.Value, 10.Seconds());
            }, _config.First);
            EnterBarrier("stopped");

            RunOn(() =>
            {
                ExpectMsg("preStart"); 
            }, _config.Third);
            EnterBarrier("third-started");

            RunOn(() =>
            {
                var p = CreateTestProbe();
                var firstAddress = Node(_config.First).Address;
                p.Within(15.Seconds(), () =>
                {
                    p.AwaitAssert(() =>
                    {
                        _echoProxy.Value.Tell("hello2", p.Ref);
                        p.ExpectMsg<IActorRef>(1.Seconds()).Path.Address.Should().NotBe(firstAddress);
                    });
                });

            }, _config.Third, _config.Fifth);
            EnterBarrier("third-working");
        }
    }
}
