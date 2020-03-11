//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeaveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }

        // The singleton actor
        public class Echo : ReceiveActor
        {
            private readonly IActorRef _testActorRef;

            public Echo(IActorRef testActorRef)
            {
                _testActorRef = testActorRef;

                Receive<string>(x => x.Equals("stop"), _ =>
                {
                    _testActorRef.Tell("stop");
                    Context.Stop(Self);
                });

                ReceiveAny(x => Sender.Tell(Self));
            }

            protected override void PreStart()
            {
                _testActorRef.Tell("preStart");
            }

            protected override void PostStop()
            {
                _testActorRef.Tell("postStop");
            }

            public static Props Props(IActorRef testActorRef)
                => Actor.Props.Create(() => new Echo(testActorRef));
        }
    }

    public class ClusterSingletonManagerLeaveSpec : MultiNodeClusterSpec
    {
        private readonly ClusterSingletonManagerLeaveSpecConfig _config;
        private readonly Lazy<IActorRef> _echoProxy;

        private TestProbe EchoProxyTerminatedProbe { get; }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        public ClusterSingletonManagerLeaveSpec() : this(new ClusterSingletonManagerLeaveSpecConfig())
        {
        }

        protected ClusterSingletonManagerLeaveSpec(ClusterSingletonManagerLeaveSpecConfig config) : base(config, typeof(ClusterSingletonManagerLeaveSpec))
        {
            _config = config;
            EchoProxyTerminatedProbe = CreateTestProbe();
            _echoProxy = new Lazy<IActorRef>(() => EchoProxyTerminatedProbe.Watch(Sys.ActorOf(ClusterSingletonProxy.Props(
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

        private IActorRef CreateSingleton()
        {
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: ClusterSingletonManagerLeaveSpecConfig.Echo.Props(TestActor),
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
            Within(10.Seconds(), () =>
            {
                AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
            });
            EnterBarrier("all-up");

            RunOn(() =>
            {
                Cluster.Leave(Node(_config.First).Address);
            }, _config.Second);

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                ExpectMsg("stop", 10.Seconds());
                ExpectMsg("postStop");
                // CoordinatedShutdown makes sure that singleton actors are
                // stopped before Cluster shutdown
                ExpectMsg("MemberRemoved");
            }, _config.First);
            EnterBarrier("first-stopped");

            RunOn(() =>
            {
                ExpectMsg("preStart");
            }, _config.Second);
            EnterBarrier("second-started");

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
            }, _config.Second, _config.Third);
            EnterBarrier("second-working");

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                Cluster.Leave(Node(_config.Second).Address);
                ExpectMsg("stop", 15.Seconds());
                ExpectMsg("postStop");
                ExpectMsg("MemberRemoved");
                EchoProxyTerminatedProbe.ExpectTerminated(_echoProxy.Value, TimeSpan.FromSeconds(10));
            }, _config.Second);
            EnterBarrier("second-stopped");

            RunOn(() =>
            {
                ExpectMsg("preStart");
            }, _config.Third);
            EnterBarrier("third-started");

            RunOn(() =>
            {
                var t = TestActor;
                Cluster.RegisterOnMemberRemoved(() => t.Tell("MemberRemoved"));
                Cluster.Leave(Node(_config.Third).Address);
                ExpectMsg("stop", 10.Seconds());
                ExpectMsg("postStop");
                ExpectMsg("MemberRemoved");
                EchoProxyTerminatedProbe.ExpectTerminated(_echoProxy.Value, TimeSpan.FromSeconds(10));
            }, _config.Third);
            EnterBarrier("third-stopped");
        }
    }
}
