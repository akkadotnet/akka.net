//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Akka.TestKit.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonApiSpec : AkkaSpec
    {
        #region Internal

        public sealed class Pong
        {
            public static Pong Instance => new Pong();
            private Pong() { }
        }

        public sealed class Ping
        {
            public IActorRef RespondTo { get; }
            public Ping(IActorRef respondTo) => RespondTo = respondTo;
        }

        public sealed class Perish
        {
            public static Perish Instance => new Perish();
            private Perish() { }
        }

        public class PingPong : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Ping ping:
                        ping.RespondTo.Tell(Pong.Instance);
                        break;
                    case Perish _:
                        Context.Stop(Self);
                        break;
                }
            }
        }

        #endregion

        private readonly Cluster _clusterNode1;
        private readonly Cluster _clusterNode2;
        private readonly ActorSystem _system2;

        public static Config GetConfig() => ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.provider = ""cluster""
            akka.cluster.roles = [""singleton""]
            akka.remote {
                dot-netty.tcp {
                    hostname = ""127.0.0.1""
                    port = 0
                }
            }").WithFallback(TestConfigs.DefaultConfig);

        public ClusterSingletonApiSpec(ITestOutputHelper testOutput)
            : base(GetConfig(), testOutput)
        {
            _clusterNode1 = Cluster.Get(Sys);

            _system2 = ActorSystem.Create(
                Sys.Name,
                ConfigurationFactory.ParseString("akka.cluster.roles = [\"singleton\"]").WithFallback(Sys.Settings.Config));

            _clusterNode2 = Cluster.Get(_system2);
        }

        [Fact]
        public void A_cluster_singleton_must_be_accessible_from_two_nodes_in_a_cluster()
        {
            var node1UpProbe = CreateTestProbe(Sys);
            var node2UpProbe = CreateTestProbe(Sys);

            _clusterNode1.Join(_clusterNode1.SelfAddress);
            node1UpProbe.AwaitAssert(() => _clusterNode1.SelfMember.Status.ShouldBe(MemberStatus.Up), TimeSpan.FromSeconds(3));

            _clusterNode2.Join(_clusterNode2.SelfAddress);
            node2UpProbe.AwaitAssert(() => _clusterNode2.SelfMember.Status.ShouldBe(MemberStatus.Up), TimeSpan.FromSeconds(3));

            var cs1 = ClusterSingleton.Get(Sys);
            var cs2 = ClusterSingleton.Get(_system2);

            var settings = ClusterSingletonSettings.Create(Sys).WithRole("singleton");
            var node1ref = cs1.Init(SingletonActor.Create(Props.Create<PingPong>(), "ping-pong").WithStopMessage(Perish.Instance).WithSettings(settings));
            var node2ref = cs2.Init(SingletonActor.Create(Props.Create<PingPong>(), "ping-pong").WithStopMessage(Perish.Instance).WithSettings(settings));

            // subsequent spawning returns the same refs
            cs1.Init(SingletonActor.Create(Props.Create<PingPong>(), "ping-pong").WithStopMessage(Perish.Instance).WithSettings(settings)).ShouldBe(node1ref);
            cs2.Init(SingletonActor.Create(Props.Create<PingPong>(), "ping-pong").WithStopMessage(Perish.Instance).WithSettings(settings)).ShouldBe(node2ref);

            var node1PongProbe = CreateTestProbe(Sys);
            var node2PongProbe = CreateTestProbe(_system2);

            node1PongProbe.AwaitAssert(() =>
            {
                node1ref.Tell(new Ping(node1PongProbe.Ref));
                node1PongProbe.ExpectMsg<Pong>();
            }, TimeSpan.FromSeconds(3));

            node2PongProbe.AwaitAssert(() =>
            {
                node2ref.Tell(new Ping(node2PongProbe.Ref));
                node2PongProbe.ExpectMsg<Pong>();
            }, TimeSpan.FromSeconds(3));
        }

        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            await _system2.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(3));
        }
    }
}
