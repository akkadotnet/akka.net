//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
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
            akka.loglevel = DEBUG
            akka.actor.provider = ""Akka.Aluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.log-remote-lifecycle-events = off
            akka.cluster.auto-down-unreachable-after = 0s")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterSingletonManagerStartupNode1 : ClusterSingletonManagerStartupConfig { }
    public class ClusterSingletonManagerStartupNode2 : ClusterSingletonManagerStartupConfig { }
    public class ClusterSingletonManagerStartupNode3 : ClusterSingletonManagerStartupConfig { }

    public abstract class ClusterSingletonManagerStartupSpec : MultiNodeClusterSpec
    {
        protected ClusterSingletonManagerStartupSpec() : base(new ClusterSingletonManagerStartupConfig())
        {
            EchoProxy = new Lazy<IActorRef>(() => Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/echo",
                settings: ClusterSingletonProxySettings.Create(Sys)),
                name: "echoProxy"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        protected Lazy<IActorRef> EchoProxy;

        [MultiNodeFact]
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
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new Echo(TestActor)),
                settings: ClusterSingletonManagerSettings.Create(Sys),
                terminationMessage: PoisonPill.Instance),
                name: "echo");
        }
    }
}