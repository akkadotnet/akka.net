﻿using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonManagerStartupConfig : MultiNodeConfig
    {
        public ClusterSingletonManagerStartupConfig()
        {
            var first = Role("first");
            var second = Role("second");
            var third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.actor.provider = ""Akka.Aluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.log-remote-lifecycle-events = off
            akka.cluster.auto-down-unreachable-after = 0s");
        }
    }

    public class ClusterSingletonManagerStartupNode1 : ClusterSingletonManagerStartupConfig { }
    public class ClusterSingletonManagerStartupNode2 : ClusterSingletonManagerStartupConfig { }
    public class ClusterSingletonManagerStartupNode3 : ClusterSingletonManagerStartupConfig { }

    public class ClusterSingletonManagerStartupSpec : MultiNodeSpec
    {
        public ClusterSingletonManagerStartupSpec() : base(new ClusterSingletonManagerStartupConfig())
        {
            EchoProxy = new Lazy<IActorRef>(() => Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/echo",
                settings: ClusterSingletonProxySettings.Create(Sys)),
                name: "echoProxy"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        protected Lazy<IActorRef> EchoProxy;

        [Fact]
        public void Startup_of_ClusterSingleton_should_be_quick()
        {
            var first = GetRole("first");
            var second = GetRole("second");
            var third = GetRole("third");

            Join(first, first);
            Join(second, first);
            Join(third, first);

            Within(TimeSpan.FromSeconds(7), () =>
            {
                AwaitAssert(() =>
                {
                    var members = Cluster.Get(Sys).ReadView.State.Members;
                    Assert.Equal(3, members.Count);
                    foreach (var member in members)
                    {
                        Assert.Equal(MemberStatus.Up, member.Status);
                    }
                });
            });
            EnterBarrier("all-up");

            // the singleton instance is expected to start "instantly"
            EchoProxy.Value.Tell("hello");
            ExpectMsg<IActorRef>(TimeSpan.FromSeconds(3));

            EnterBarrier("done");
        }

        private RoleName GetRole(string name)
        {
            return Roles.First(roleName => roleName.Name == name);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Join(Node(to).Address);
                CreateSingleton();
            }, from);
        }

        private IActorRef CreateSingleton()
        {
            //TODO
            /**
            system.actorOf(ClusterSingletonManager.props(
              singletonProps = Props(classOf[Echo], testActor),
              terminationMessage = PoisonPill,
              settings = ClusterSingletonManagerSettings(system)),
              name = "echo")
            */
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new Echo(TestActor)),
                settings: ClusterSingletonManagerSettings.Create(Sys),
                terminationMessage: PoisonPill.Instance),
                name: "echo");
        }
    }
}