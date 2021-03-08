//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRegistrationCoordinatedShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRegistrationCoordinatedShutdownSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingRegistrationCoordinatedShutdownSpecConfig()
            : base(loglevel: "DEBUG")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
        }
    }

    public class ClusterShardingRegistrationCoordinatedShutdownSpec : MultiNodeClusterShardingSpec<ClusterShardingRegistrationCoordinatedShutdownSpecConfig>
    {
        #region setup

        private readonly Lazy<IActorRef> _region;

        public ClusterShardingRegistrationCoordinatedShutdownSpec()
            : this(new ClusterShardingRegistrationCoordinatedShutdownSpecConfig(), typeof(ClusterShardingRegistrationCoordinatedShutdownSpec))
        {
        }

        protected ClusterShardingRegistrationCoordinatedShutdownSpec(ClusterShardingRegistrationCoordinatedShutdownSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        #endregion

        [MultiNodeFact]
        public void ClusterShardingRegistrationCoordinatedShutdownSpecs()
        {
            Region_registration_during_CoordinatedShutdown_must_try_next_oldest();
        }

        private void Region_registration_during_CoordinatedShutdown_must_try_next_oldest()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // second should be oldest
                Join(config.Second, config.Second);
                Join(config.First, config.Second);
                Join(config.Third, config.Second);

                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                });

                var probe = CreateTestProbe();
                var csTaskDone = CreateTestProbe();
                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).AddTask(CoordinatedShutdown.PhaseBeforeClusterShutdown, "test", () =>
                    {
                        Thread.Sleep(200);
                        _region.Value.Tell(1, probe.Ref);
                        probe.ExpectMsg(1);
                        csTaskDone.Ref.Tell(Done.Instance);
                        return Task.FromResult(Done.Instance);
                    });
                }, config.Third);

                StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: Props.Create(() => new ShardedEntity()),
                    extractEntityId: IntExtractEntityId,
                    extractShardId: IntExtractShardId);

                EnterBarrier("before-shutdown");

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                }, config.Second);

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                    csTaskDone.ExpectMsg<Done>();
                }, config.Third);

                EnterBarrier("after-shutdown");

                RunOn(() =>
                {
                    _region.Value.Tell(2);
                    ExpectMsg(2);
                    LastSender.Path.Address.HasLocalScope.Should().BeTrue();
                }, config.First);

                EnterBarrier("after-1");
            });
        }
    }
}
