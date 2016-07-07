//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerStartupConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public ClusterSingletonManagerStartupConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterSingletonManagerStartupNode1 : ClusterSingletonManagerStartupSpec { }
    public class ClusterSingletonManagerStartupNode2 : ClusterSingletonManagerStartupSpec { }
    public class ClusterSingletonManagerStartupNode3 : ClusterSingletonManagerStartupSpec { }

    public abstract class ClusterSingletonManagerStartupSpec : MultiNodeClusterSpec
    {
        private class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(_ => Sender.Tell(Self));
            }
        }

        private readonly ClusterSingletonManagerStartupConfig _config;

        protected ClusterSingletonManagerStartupSpec() : this(new ClusterSingletonManagerStartupConfig())
        {

        }

        protected ClusterSingletonManagerStartupSpec(ClusterSingletonManagerStartupConfig config) : base(config)
        {
            _config = config;
            EchoProxy = new Lazy<IActorRef>(() => Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/echo",
                settings: ClusterSingletonProxySettings.Create(Sys)),
                name: "echoProxy"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        protected Lazy<IActorRef> EchoProxy;

        [MultiNodeFact]
        public void ClusterSingletonManagerStartupSpecs()
        {
            Startup_of_ClusterSingleton_should_be_quick();
        }

        public void Startup_of_ClusterSingleton_should_be_quick()
        {
            Join(_config.First, _config.First);
            Join(_config.Second, _config.First);
            Join(_config.Third, _config.First);

            Within(TimeSpan.FromSeconds(7), () =>
            {
                AwaitAssert(() =>
                {
                    var members = Cluster.ReadView.State.Members;
                    members.Count.Should().Be(3);
                    members.All(c => c.Status == MemberStatus.Up).Should().BeTrue();
                });
            });
            EnterBarrier("all-up");

            // the singleton instance is expected to start "instantly"
            EchoProxy.Value.Tell("hello");
            ExpectMsg<IActorRef>(TimeSpan.FromSeconds(3));

            EnterBarrier("done");
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
                CreateSingleton();
            }, from);
        }

        private IActorRef CreateSingleton()
        {
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create<Echo>(),
                settings: ClusterSingletonManagerSettings.Create(Sys),
                terminationMessage: PoisonPill.Instance),
                name: "echo");
        }
    }
}