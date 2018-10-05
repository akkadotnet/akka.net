//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeaveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerLeaveSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterSingletonManagerLeaveSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }

        // The singleton actor
    }

    public class ClusterSingletonManagerLeaveSpec : MultiNodeClusterSpec
    {
        public class Echo : UntypedActor
        {
            private readonly IActorRef _testActorRef;

            public Echo(IActorRef testActorRef)
            {
                _testActorRef = testActorRef;
            }

            protected override void PreStart() => _testActorRef.Tell("preStart");

            protected override void PostStop() => _testActorRef.Tell("postStop");

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "stop":
                        _testActorRef.Tell("stop");
                        Context.Stop(Self);
                        break;
                    default:
                        Sender.Tell(Self);
                        break;
                }
            }
        }

        private readonly Lazy<IActorRef> _echoProxy;
        private readonly TestProbe _echoProxyTerminatedProbe;

        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;

        protected override int InitialParticipantsValueFactory => Roles.Count;

        public ClusterSingletonManagerLeaveSpec() : this(new ClusterSingletonManagerLeaveSpecConfig())
        {
        }

        protected ClusterSingletonManagerLeaveSpec(ClusterSingletonManagerLeaveSpecConfig config) : base(config, typeof(ClusterSingletonManagerLeaveSpec))
        {
            _echoProxyTerminatedProbe = CreateTestProbe();
            _echoProxy = new Lazy<IActorRef>(() => _echoProxyTerminatedProbe.Watch(Sys.ActorOf(
                ClusterSingletonProxy.Props(
                    singletonManagerPath: "/user/echo",
                    settings: ClusterSingletonProxySettings.Create(Sys)),
                    name: "echoProxy")));

            _first = config.First;
            _second = config.Second;
            _third = config.Third;
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                CreateSingleton();
            }, from);
        }

        private IActorRef CreateSingleton()
        {
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new Echo(TestActor)), 
                terminationMessage: "stop",
                settings: ClusterSingletonManagerSettings.Create(Sys)),
                name: "echo");
        }

        [MultiNodeFact]
        public void ClusterSingletonManagerLeaveSpecs()
        {
            Leaving_ClusterSingletonManager_must_handover_to_new_instance();
        }

        public void Leaving_ClusterSingletonManager_must_handover_to_new_instance()
        {
            Join(_first, _first);

            RunOn(() => Within(5.Seconds(), () =>
            {
                ExpectMsg("preStart");
                _echoProxy.Value.Tell("hello");
                ExpectMsg<IActorRef>();
            }), _first);

            EnterBarrier("first-active");

            Join(_second, _first);

            RunOn(() => Within(10.Seconds(), () =>
            {
                AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(2));
            }), _first, _second);

            EnterBarrier("second-up");

            Join(_third, _first);
            Within(10.Seconds(), () =>
            {
                AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
            });

            EnterBarrier("all-up");

            RunOn(() =>
            {
                Cluster.Leave(Node(_first).Address);
            }, _second);

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                ExpectMsg("stop", 10.Seconds());
                ExpectMsg("postStop");
                // CoordinatedShutdown makes sure that singleton actors are
                // stopped before Cluster shutdown
                ExpectMsg("MemberRemoved");
                _echoProxyTerminatedProbe.ExpectTerminated(_echoProxy.Value, 10.Seconds());
            }, _first);
            EnterBarrier("first-stopped");

            RunOn(() =>
            {
                ExpectMsg("preStart");
            }, _second);
            EnterBarrier("second-started");

            RunOn(() =>
            {
                var p = CreateTestProbe();
                var firstAddress = Node(_first).Address;
                p.Within(15.Seconds(), () =>
                {
                    p.AwaitAssert(() =>
                    {
                        _echoProxy.Value.Tell("hello2", p.Ref);
                        p.ExpectMsg<IActorRef>(1.Seconds()).Path.Address.Should().NotBe(firstAddress);
                    });
                });
            }, _second, _third);
            EnterBarrier("second-working");

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                Cluster.Leave(Node(_second).Address);
                ExpectMsg("stop", 15.Seconds());
                ExpectMsg("postStop");
                ExpectMsg("MemberRemoved");
                _echoProxyTerminatedProbe.ExpectTerminated(_echoProxy.Value, TimeSpan.FromSeconds(10));
            }, _second);
            EnterBarrier("second-stopped");

            RunOn(() =>
            {
                ExpectMsg("preStart");
            }, _third);
            EnterBarrier("third-started");

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                Cluster.Leave(Node(_third).Address);
                ExpectMsg("stop", 10.Seconds());
                ExpectMsg("postStop");
                ExpectMsg("MemberRemoved");
                _echoProxyTerminatedProbe.ExpectTerminated(_echoProxy.Value, TimeSpan.FromSeconds(10));
            }, _third);
            EnterBarrier("third-stopped");
        }
    }
}
