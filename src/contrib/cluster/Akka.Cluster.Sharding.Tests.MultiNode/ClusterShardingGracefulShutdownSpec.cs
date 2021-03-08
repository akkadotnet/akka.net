//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGracefulShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGracefulShutdownSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingGracefulShutdownSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.roles = [""backend""]
            akka.cluster.sharding {
                coordinator-failure-backoff = 3s
                shard-failure-backoff = 3s
            }
            # don't leak ddata state across runs
            akka.cluster.sharding.distributed-data.durable.keys = []
            akka.persistence.journal.leveldb-shared.store.native = off
            ")
        {
            First = Role("first");
            Second = Role("second");
        }
    }

    public class PersistentClusterShardingGracefulShutdownSpecConfig : ClusterShardingGracefulShutdownSpecConfig
    {
        public PersistentClusterShardingGracefulShutdownSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingGracefulShutdownSpecConfig : ClusterShardingGracefulShutdownSpecConfig
    {
        public DDataClusterShardingGracefulShutdownSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingGracefulShutdownSpec : ClusterShardingGracefulShutdownSpec
    {
        public PersistentClusterShardingGracefulShutdownSpec()
            : base(new PersistentClusterShardingGracefulShutdownSpecConfig(), typeof(PersistentClusterShardingGracefulShutdownSpec))
        {
        }
    }

    public class DDataClusterShardingGracefulShutdownSpec : ClusterShardingGracefulShutdownSpec
    {
        public DDataClusterShardingGracefulShutdownSpec()
            : base(new DDataClusterShardingGracefulShutdownSpecConfig(), typeof(DDataClusterShardingGracefulShutdownSpec))
        {
        }
    }

    public abstract class ClusterShardingGracefulShutdownSpec : MultiNodeClusterShardingSpec<ClusterShardingGracefulShutdownSpecConfig>
    {
        #region setup

        private const string TypeName = "Entity";
        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingGracefulShutdownSpec(ClusterShardingGracefulShutdownSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(TypeName));
        }

        private void Join(RoleName from, RoleName to, string typeName)
        {
            base.Join(from, to);
            RunOn(() =>
            {
                StartSharding(typeName);
            }, from);
            EnterBarrier($"{from}-started");
        }

        private IActorRef StartSharding(string typeName)
        {
            return StartSharding(
                Sys,
                typeName,
                entityProps: Props.Create(() => new ShardedEntity()),
                extractEntityId: IntExtractEntityId,
                extractShardId: IntExtractShardId,
                allocationStrategy: ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 2, relativeLimit: 1.0),
                handOffStopMessage: ShardedEntity.Stop.Instance);
        }

        #endregion

        [MultiNodeFact]
        public void ClusterShardingGracefulShutdownSpecs()
        {
            Cluster_sharding_must_start_some_shards_in_both_regions();
            Cluster_sharding_must_gracefully_shutdown_a_region();
            Cluster_sharding_must_gracefully_shutdown_empty_region();
        }

        private void Cluster_sharding_must_start_some_shards_in_both_regions()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.First, config.Second);

                Join(config.First, config.First, TypeName);
                Join(config.Second, config.First, TypeName);

                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    var regionAddresses = Enumerable.Range(1, 100).Select(n =>
                    {
                        _region.Value.Tell(n, probe.Ref);
                        probe.ExpectMsg(n, TimeSpan.FromSeconds(1));
                        return probe.LastSender.Path.Address;
                    }).ToImmutableHashSet();

                    regionAddresses.Count.Should().Be(2);
                });
                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_must_gracefully_shutdown_a_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    _region.Value.Tell(GracefulShutdown.Instance);
                }, config.Second);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        for (int i = 1; i <= 200; i++)
                        {
                            _region.Value.Tell(i, probe.Ref);
                            probe.ExpectMsg(i, TimeSpan.FromSeconds(1));
                            probe.LastSender.Path.Should().Be(_region.Value.Path / i.ToString() / i.ToString());
                        }
                    });
                }, config.First);
                EnterBarrier("handoff-completed");

                RunOn(() =>
                {
                    Watch(_region.Value);
                    ExpectTerminated(_region.Value);
                }, config.Second);

                EnterBarrier("after-3");
            });
        }

        private void Cluster_sharding_must_gracefully_shutdown_empty_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    var regionEmpty = StartSharding(typeName: "EntityEmpty");

                    Watch(regionEmpty);
                    regionEmpty.Tell(GracefulShutdown.Instance);
                    ExpectTerminated(regionEmpty, TimeSpan.FromSeconds(5));
                }, config.First);
            });
        }
    }
}
