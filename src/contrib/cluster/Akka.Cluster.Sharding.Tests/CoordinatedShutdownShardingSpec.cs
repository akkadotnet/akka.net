//-----------------------------------------------------------------------
// <copyright file="CoordinatedShutdownShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class CoordinatedShutdownShardingSpec : AkkaSpec
    {
        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private readonly ActorSystem _sys3;

        private readonly IActorRef _region1;
        private readonly IActorRef _region2;
        private readonly IActorRef _region3;

        private readonly TestProbe _probe1;
        private readonly TestProbe _probe2;
        private readonly TestProbe _probe3;

        private static readonly Config SpecConfig;

        private class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(_ => Sender.Tell(_));
            }
        }

        private readonly ExtractEntityId _extractEntityId = message => (message.ToString(), message);

        private readonly ExtractShardId _extractShard = message => (message.GetHashCode() % 10).ToString();

        static CoordinatedShutdownShardingSpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));
        }

        public CoordinatedShutdownShardingSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            _sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys3 = Sys;

            var props = Props.Create(() => new EchoActor());
            _region1 = ClusterSharding.Get(_sys1).Start("type1", props, ClusterShardingSettings.Create(_sys1),
                _extractEntityId, _extractShard);
            _region2 = ClusterSharding.Get(_sys2).Start("type1", props, ClusterShardingSettings.Create(_sys2),
                _extractEntityId, _extractShard);
            _region3 = ClusterSharding.Get(_sys3).Start("type1", props, ClusterShardingSettings.Create(_sys3),
                _extractEntityId, _extractShard);


            _probe1 = CreateTestProbe(_sys1);
            _probe2 = CreateTestProbe(_sys2);
            _probe3 = CreateTestProbe(_sys3);

            CoordinatedShutdown.Get(_sys1).AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "unbind", () =>
            {
                _probe1.Ref.Tell("CS-unbind-1");
                return Task.FromResult(Done.Instance);
            });

            CoordinatedShutdown.Get(_sys2).AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "unbind", () =>
            {
                _probe2.Ref.Tell("CS-unbind-2");
                return Task.FromResult(Done.Instance);
            });

            CoordinatedShutdown.Get(_sys3).AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "unbind", () =>
            {
                _probe3.Ref.Tell("CS-unbind-3");
                return Task.FromResult(Done.Instance);
            });
        }

        protected override void BeforeTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys2);
        }

        /// <summary>
        /// Using region 2 as it is not shutdown in either test.
        /// </summary>
        private void PingEntities()
        {
            AwaitAssert(() =>
            {
                _region2.Tell(1, _probe2.Ref);
                _probe2.ExpectMsg<int>(1.Seconds()).Should().Be(1);
                _region2.Tell(2, _probe2.Ref);
                _probe2.ExpectMsg<int>(1.Seconds()).Should().Be(2);
                _region2.Tell(3, _probe2.Ref);
                _probe2.ExpectMsg<int>(1.Seconds()).Should().Be(3);
            }, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void Sharding_and_CoordinatedShutdown_must_run_successfully()
        {
            InitCluster();
            RunCoordinatedShutdownWhenLeaving();
            RunCoordinatedShutdownWhenDowning();
        }

        private void InitCluster()
        {
            Cluster.Get(_sys1).Join(Cluster.Get(_sys1).SelfAddress); // coordinator will initially run on sys1
            AwaitAssert(() => Cluster.Get(_sys1).SelfMember.Status.Should().Be(MemberStatus.Up));

            Cluster.Get(_sys2).Join(Cluster.Get(_sys1).SelfAddress);
            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys1).State.Members.Count.Should().Be(2);
                    Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Get(_sys2).State.Members.Count.Should().Be(2);
                    Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });

            Cluster.Get(_sys3).Join(Cluster.Get(_sys1).SelfAddress);
            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys1).State.Members.Count.Should().Be(3);
                    Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Get(_sys2).State.Members.Count.Should().Be(3);
                    Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Get(_sys3).State.Members.Count.Should().Be(3);
                    Cluster.Get(_sys3).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });

            PingEntities();
        }

        private void RunCoordinatedShutdownWhenLeaving()
        {
            Cluster.Get(_sys3).Leave(Cluster.Get(_sys1).SelfAddress);
            _probe1.ExpectMsg("CS-unbind-1");

            Within(20.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys2).State.Members.Count.Should().Be(2);
                    Cluster.Get(_sys3).State.Members.Count.Should().Be(2);
                });
            });

            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys1).IsTerminated.Should().BeTrue();
                    _sys1.WhenTerminated.IsCompleted.Should().BeTrue();
                });
            });

            PingEntities();
        }

        private void RunCoordinatedShutdownWhenDowning()
        {
            // coordinator is on Sys2
            Cluster.Get(_sys2).Down(Cluster.Get(_sys3).SelfAddress);
            _probe3.ExpectMsg("CS-unbind-3");

            Within(20.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys2).State.Members.Count.Should().Be(1);
                });
            });

            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys3).IsTerminated.Should().BeTrue();
                    _sys3.WhenTerminated.IsCompleted.Should().BeTrue();
                });
            });

            PingEntities();
        }
    }
}
