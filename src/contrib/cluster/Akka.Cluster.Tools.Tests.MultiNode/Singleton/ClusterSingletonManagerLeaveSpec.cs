//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeaveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.MultiNode;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerLeaveSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public ClusterSingletonManagerLeaveSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterSingletonManagerLeaveNode1 : ClusterSingletonManagerLeaveSpec { }
    public class ClusterSingletonManagerLeaveNode2 : ClusterSingletonManagerLeaveSpec { }
    public class ClusterSingletonManagerLeaveNode3 : ClusterSingletonManagerLeaveSpec { }

    public abstract class ClusterSingletonManagerLeaveSpec : MultiNodeClusterSpec
    {
        private readonly ClusterSingletonManagerLeaveSpecConfig _config;

        private class Echo : ReceiveActor
        {
            private readonly IActorRef _testActorRef;

            public Echo(IActorRef testActorRef)
            {
                _testActorRef = testActorRef;
                ReceiveAny(x => Sender.Tell(Self));
            }

            protected override void PostStop()
            {
                _testActorRef.Tell("stopped");
            }
        }

        private readonly Lazy<IActorRef> _echoProxy;

        protected ClusterSingletonManagerLeaveSpec() : this(new ClusterSingletonManagerLeaveSpecConfig())
        {
        }

        protected ClusterSingletonManagerLeaveSpec(ClusterSingletonManagerLeaveSpecConfig config) : base(config)
        {
            _config = config;

            _echoProxy = new Lazy<IActorRef>(() => Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/echo",
                settings: ClusterSingletonProxySettings.Create(Sys)),
                name: "echoProxy"));
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
                terminationMessage: PoisonPill.Instance,
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
            var first = _config.First;
            var second = _config.Second;
            var third = _config.Third;

            Join(first, first);
            RunOn(() =>
            {
                _echoProxy.Value.Tell("hello1");
                ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
            }, first);
            EnterBarrier("first-active");

            Join(second, first);
            Join(third, first);
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() => Cluster.ReadView.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
            });
            EnterBarrier("all-up");

            RunOn(() =>
            {
                Cluster.Leave(Node(first).Address);
            }, second);

            RunOn(() =>
            {
                ExpectMsg("stopped", TimeSpan.FromSeconds(10));
            }, first);
            EnterBarrier("first-stopped");

            RunOn(() =>
            {
                var p = CreateTestProbe();
                var firstAddress = Node(first).Address;
                p.Within(TimeSpan.FromSeconds(10), () =>
                {
                    p.AwaitAssert(() =>
                    {
                        _echoProxy.Value.Tell("hello2", p.Ref);
                        var actualAddress = p.ExpectMsg<IActorRef>(TestKitSettings.DefaultTimeout);
                        actualAddress.Path.Address.Should().NotBe(firstAddress);
                    });
                });
            }, second, third);
            EnterBarrier("handover-done");
        }
    }
}