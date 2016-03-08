using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Cluster.Sharding
{
    public interface IShardRegionCommand { }
    public interface IShardRegionQuery { }

    /// <summary>
    /// If the state of the entries are persistent you may stop entries that are not used to
    /// reduce memory consumption. This is done by the application specific implementation of
    /// the entity actors for example by defining receive timeout (<see cref="IActorContext.SetReceiveTimeout"/>).
    /// If a message is already enqueued to the entity when it stops itself the enqueued message
    /// in the mailbox will be dropped. To support graceful passivation without loosing such
    /// messages the entity actor can send this <see cref="Passivate"/> message to its parent <see cref="ShardRegion"/>.
    /// The specified wrapped <see cref="StopMessage"/> will be sent back to the entity, which is
    /// then supposed to stop itself. Incoming messages will be buffered by the `ShardRegion`
    /// between reception of <see cref="Passivate"/> and termination of the entity. Such buffered messages
    /// are thereafter delivered to a new incarnation of the entity.
    /// 
    /// <see cref="PoisonPill"/> is a perfectly fine <see cref="StopMessage"/>.
    /// </summary>
    [Serializable]
    public sealed class Passivate : IShardRegionCommand
    {
        public Passivate(object stopMessage)
        {
            StopMessage = stopMessage;
        }

        public object StopMessage { get; private set; }
    }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to handoff all shards that are hosted by
    /// the <see cref="ShardRegion"/> and then the <see cref="ShardRegion"/> actor will be stopped. You can <see cref="ICanWatch.Watch"/>
    /// it to know when it is completed.
    /// </summary>
    [Serializable]
    public sealed class GracefulShutdown : IShardRegionCommand
    {
        public static readonly GracefulShutdown Instance = new GracefulShutdown();

        private GracefulShutdown()
        {
        }
    }

    /// <summary>
    /// We must be sure that a shard is initialized before to start send messages to it.
    /// Shard could be terminated during initialization.
    /// </summary>
    [Serializable]
    public sealed class ShardInitialized
    {
        public readonly string ShardId;

        public ShardInitialized(string shardId)
        {
            ShardId = shardId;
        }
    }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to request for <see cref="CurrentRegions"/>,
    /// which contains the addresses of all registered regions.
    /// Intended for testing purpose to see when cluster sharding is "ready".
    /// </summary>
    [Serializable]
    public sealed class GetCurrentRegions : IShardRegionQuery
    {
        public static readonly GetCurrentRegions Instance = new GetCurrentRegions();

        private GetCurrentRegions()
        {
        }
    }

    /// <summary>
    /// Reply to <see cref="GetCurrentRegions"/>.
    /// </summary>
    [Serializable]
    public sealed class CurrentRegions
    {
        public readonly Address[] Regions;
        public CurrentRegions(Address[] regions)
        {
            Regions = regions;
        }
    }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to request for <see cref="ClusterShardingStats"/>,
    /// which contains statistics about the currently running sharded entities in the
    /// entire cluster. If the `timeout` is reached without answers from all shard regions
    /// the reply will contain an empty map of regions.
    /// 
    /// Intended for testing purpose to see when cluster sharding is "ready" or to monitor
    /// the state of the shard regions.
    /// </summary>
    [Serializable]
    public sealed class GetClusterShardingStats : IShardRegionQuery
    {
        public readonly TimeSpan Timeout;

        public GetClusterShardingStats(TimeSpan timeout)
        {
            Timeout = timeout;
        }
    }

    /// <summary>
    /// Reply to <see cref="GetClusterShardingStats"/>, contains statistics about all the sharding regions in the cluster.
    /// </summary>
    [Serializable]
    public sealed class ClusterShardingStats
    {
        public readonly IDictionary<Address, ShardRegionStats> Regions;

        public ClusterShardingStats(IDictionary<Address, ShardRegionStats> regions)
        {
            Regions = regions;
        }
    }

    /// <summary>
    /// Send this message to the `ShardRegion` actor to request for <see cref="ShardRegionStats"/>,
    /// which contains statistics about the currently running sharded entities in the
    /// entire region.
    /// Intended for testing purpose to see when cluster sharding is "ready" or to monitor
    /// the state of the shard regions.
    /// 
    /// For the statistics for the entire cluster, see <see cref="GetClusterShardingStats$"/>.
    /// </summary>
    [Serializable]
    public sealed class GetShardRegionStats : IShardRegionQuery
    {
        public static readonly GetShardRegionStats Instance = new GetShardRegionStats();

        private GetShardRegionStats()
        {
        }
    }

    /// <summary>
    /// Send this message to a <see cref="ShardRegion"/> actor instance to request a
    /// <see cref="CurrentShardRegionState"/> which describes the current state of the region.
    /// The state contains information about what shards are running in this region
    /// and what entities are running on each of those shards.
    /// </summary>
    [Serializable]
    public sealed class GetShardRegionState : IShardRegionQuery
    {
        public static readonly GetShardRegionState Instance = new GetShardRegionState();

        private GetShardRegionState()
        {
        }
    }

    /// <summary>
    /// Reply to <see cref="GetShardRegionState"/> If gathering the shard information times out the set of shards will be empty.
    /// </summary>
    [Serializable]
    public sealed class CurrentShardRegionState
    {
        public readonly ISet<ShardState> Shards;

        public CurrentShardRegionState(ISet<ShardState> shards)
        {
            Shards = shards;
        }
    }

    [Serializable]
    public sealed class ShardRegionStats
    {
        public readonly IDictionary<string, int> Stats;

        public ShardRegionStats(IDictionary<string, int> stats)
        {
            Stats = stats;
        }
    }

    [Serializable]
    public sealed class ShardState
    {
        public readonly string ShardId;
        public readonly string[] EntityIds;

        public ShardState(string shardId, string[] entityIds)
        {
            ShardId = shardId;
            EntityIds = entityIds;
        }
    }
}