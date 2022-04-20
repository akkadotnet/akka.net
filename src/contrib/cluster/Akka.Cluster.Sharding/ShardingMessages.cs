﻿//-----------------------------------------------------------------------
// <copyright file="ShardingMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// TBD
    /// </summary>
    public interface IShardRegionCommand { }

    /// <summary>
    /// If the state of the entities are persistent you may stop entities that are not used to
    /// reduce memory consumption. This is done by the application specific implementation of
    /// the entity actors for example by defining receive timeout (<see cref="IActorContext.SetReceiveTimeout"/>).
    /// If a message is already enqueued to the entity when it stops itself the enqueued message
    /// in the mailbox will be dropped. To support graceful passivation without losing such
    /// messages the entity actor can send this <see cref="Passivate"/> message to its parent <see cref="ShardRegion"/>.
    /// The specified wrapped <see cref="StopMessage"/> will be sent back to the entity, which is
    /// then supposed to stop itself. Incoming messages will be buffered by the <see cref="ShardRegion"/>
    /// between reception of <see cref="Passivate"/> and termination of the entity. Such buffered messages
    /// are thereafter delivered to a new incarnation of the entity.
    ///
    /// <see cref="PoisonPill.Instance"/> is a perfectly fine <see cref="StopMessage"/>.
    /// </summary>
    [Serializable]
    public sealed class Passivate : IShardRegionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="stopMessage">TBD</param>
        public Passivate(object stopMessage)
        {
            StopMessage = stopMessage;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public object StopMessage { get; }
    }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to handoff all shards that are hosted by
    /// the <see cref="ShardRegion"/> and then the <see cref="ShardRegion"/> actor will be stopped. You can <see cref="ICanWatch.Watch"/>
    /// it to know when it is completed.
    /// </summary>
    [Serializable]
    public sealed class GracefulShutdown : IShardRegionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly GracefulShutdown Instance = new GracefulShutdown();

        private GracefulShutdown()
        {
        }
    }

    [Serializable]
    internal sealed class GracefulShutdownTimeout : IShardRegionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly GracefulShutdownTimeout Instance = new GracefulShutdownTimeout();

        private GracefulShutdownTimeout()
        {
        }
    }


    /// <summary>
    /// We must be sure that a shard is initialized before to start send messages to it.
    /// Shard could be terminated during initialization.
    /// </summary>
    [Serializable]
    internal sealed class ShardInitialized : IEquatable<ShardInitialized>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string ShardId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shardId">TBD</param>
        public ShardInitialized(string shardId)
        {
            ShardId = shardId;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ShardInitialized);
        }

        public bool Equals(ShardInitialized other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Equals(ShardId, other.ShardId);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return ShardId.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString() => $"ShardInitialized({ShardId})";

        #endregion
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IShardRegionQuery { }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to request for <see cref="CurrentRegions"/>,
    /// which contains the addresses of all registered regions.
    /// Intended for testing purpose to see when cluster sharding is "ready" or to monitor
    /// the state of the shard regions.
    /// </summary>
    [Serializable]
    public sealed class GetCurrentRegions : IShardRegionQuery, IClusterShardingSerializable
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly GetCurrentRegions Instance = new GetCurrentRegions();

        private GetCurrentRegions()
        {
        }

        /// <inheritdoc/>
        public override string ToString() => $"GetCurrentRegions";
    }

    /// <summary>
    /// Reply to <see cref="GetCurrentRegions"/>.
    /// </summary>
    [Serializable]
    public sealed class CurrentRegions : IClusterShardingSerializable, IEquatable<CurrentRegions>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IImmutableSet<Address> Regions;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="regions">TBD</param>
        public CurrentRegions(IImmutableSet<Address> regions)
        {
            Regions = regions;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as CurrentRegions);
        }

        public bool Equals(CurrentRegions other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Regions.SetEquals(other.Regions);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 0;
                foreach (var s in Regions)
                    hashCode = (hashCode * 397) ^ s.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"CurrentRegions({string.Join(", ", Regions.Select(r => $"[{r}]"))})";

        #endregion
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
    public sealed class GetClusterShardingStats : IShardRegionQuery, IClusterShardingSerializable, IEquatable<GetClusterShardingStats>
    {
        /// <summary>
        /// The timeout for this operation.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates a new GetClusterShardingStats message instance.
        /// </summary>
        /// <param name="timeout">The amount of time to allow this operation to run.</param>
        public GetClusterShardingStats(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as GetClusterShardingStats);
        }

        public bool Equals(GetClusterShardingStats other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Timeout.Equals(other.Timeout);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Timeout.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString() => $"GetClusterShardingStats({Timeout})";

        #endregion
    }

    /// <summary>
    /// Reply to <see cref="GetClusterShardingStats"/>, contains statistics about all the sharding regions in the cluster.
    /// </summary>
    [Serializable]
    public sealed class ClusterShardingStats : IClusterShardingSerializable, IEquatable<ClusterShardingStats>
    {
        /// <summary>
        /// All of the statistics for a specific shard region organized per-node.
        /// </summary>
        public readonly IImmutableDictionary<Address, ShardRegionStats> Regions;

        /// <summary>
        /// Creates a new ClusterShardingStats message.
        /// </summary>
        /// <param name="regions">The set of sharding statistics per-node.</param>
        public ClusterShardingStats(IImmutableDictionary<Address, ShardRegionStats> regions)
        {
            Regions = regions;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ClusterShardingStats);
        }

        public bool Equals(ClusterShardingStats other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Regions.Keys.SequenceEqual(other.Regions.Keys)
                && Regions.Values.SequenceEqual(other.Regions.Values);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 0;
                foreach (var r in Regions)
                    hashCode = (hashCode * 397) ^ r.Key.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"ClusterShardingStats({string.Join(", ", Regions.Select(r => $"[{r.Key}]:[{r.Value}]"))})";

        #endregion
    }

    /// <summary>
    /// Send this message to the <see cref="ShardRegion"/> actor to request for <see cref="ShardRegionStats"/>,
    /// which contains statistics about the currently running sharded entities in the
    /// entire region.
    /// Intended for testing purpose to see when cluster sharding is "ready" or to monitor
    /// the state of the shard regions.
    ///
    /// For the statistics for the entire cluster, see <see cref="GetClusterShardingStats"/>.
    /// </summary>
    [Serializable]
    public sealed class GetShardRegionStats : IShardRegionQuery, IClusterShardingSerializable
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly GetShardRegionStats Instance = new GetShardRegionStats();

        private GetShardRegionStats()
        {
        }

        /// <inheritdoc/>
        public override string ToString() => $"GetShardRegionStats";
    }

    /// <summary>
    /// Entity allocation statistics for a specific shard region.
    /// </summary>
    [Serializable]
    public sealed class ShardRegionStats : IClusterShardingSerializable, IEquatable<ShardRegionStats>
    {
        /// <summary>
        /// the region stats mapping of `ShardId` to number of entities
        /// </summary>
        public readonly IImmutableDictionary<ShardId, int> Stats;
        /// <summary>
        /// set of shards if any failed to respond within the timeout
        /// </summary>
        public readonly IImmutableSet<string> Failed;

        /// <summary>
        /// Creates a new ShardRegionStats instance.
        /// </summary>
        /// <param name="stats">the region stats mapping of `ShardId` to number of entities</param>
        [Obsolete]
        public ShardRegionStats(IImmutableDictionary<ShardId, int> stats)
            : this(stats, ImmutableHashSet<ShardId>.Empty)
        {
        }

        /// <summary>
        /// Creates a new ShardRegionStats instance.
        /// </summary>
        /// <param name="stats">the region stats mapping of `ShardId` to number of entities</param>
        /// <param name="failed">set of shards if any failed to respond within the timeout</param>
        public ShardRegionStats(IImmutableDictionary<ShardId, int> stats, IImmutableSet<ShardId> failed)
        {
            Stats = stats;
            Failed = failed;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ShardRegionStats);
        }

        public bool Equals(ShardRegionStats other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Stats.Keys.SequenceEqual(other.Stats.Keys)
                && Stats.Values.SequenceEqual(other.Stats.Values)
                && Failed.SetEquals(other.Failed);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 0;
                foreach (var s in Stats)
                    hashCode = (hashCode * 397) ^ s.Key.GetHashCode();
                hashCode = (hashCode * 397) ^ Failed.Count.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ShardRegionStats[stats={string.Join(", ", Stats.Select(i => $"({i.Key}:{i.Value})"))}, failed={string.Join(", ", Failed)}]";
        }

        #endregion
    }

    /// <summary>
    /// Send this message to a <see cref="ShardRegion"/> actor instance to request a
    /// <see cref="CurrentShardRegionState"/> which describes the current state of the region.
    /// The state contains information about what shards are running in this region
    /// and what entities are running on each of those shards.
    /// </summary>
    [Serializable]
    public sealed class GetShardRegionState : IShardRegionQuery, IClusterShardingSerializable
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly GetShardRegionState Instance = new GetShardRegionState();

        private GetShardRegionState()
        {
        }

        /// <inheritdoc/>
        public override string ToString() => $"GetShardRegionState";
    }

    /// <summary>
    /// Reply to <see cref="GetShardRegionState"/>
    ///
    /// If gathering the shard information times out the set of shards will be empty.
    /// </summary>
    [Serializable]
    public sealed class CurrentShardRegionState : IClusterShardingSerializable, IEquatable<CurrentShardRegionState>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IImmutableSet<ShardState> Shards;
        public readonly IImmutableSet<string> Failed;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shards">TBD</param>
        [Obsolete]
        public CurrentShardRegionState(IImmutableSet<ShardState> shards)
            : this(shards, ImmutableHashSet<ShardId>.Empty)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shards">TBD</param>
        /// <param name="failed"></param>
        public CurrentShardRegionState(IImmutableSet<ShardState> shards, IImmutableSet<ShardId> failed)
        {
            Shards = shards;
            Failed = failed;
        }


        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as CurrentShardRegionState);
        }

        public bool Equals(CurrentShardRegionState other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Shards.SetEquals(other.Shards)
                && Failed.SetEquals(other.Failed);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 0;
                foreach (var s in Shards)
                    hashCode = (hashCode * 397) ^ s.GetHashCode();
                hashCode = (hashCode * 397) ^ Failed.Count.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"CurrentShardRegionState[shards={string.Join(", ", Shards)}, failed={string.Join(", ", Failed)}]";
        }

        #endregion
    }


    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class ShardState : IClusterShardingSerializable, IEquatable<ShardState>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string ShardId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IImmutableSet<string> EntityIds;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shardId">TBD</param>
        /// <param name="entityIds">TBD</param>
        public ShardState(string shardId, IImmutableSet<string> entityIds)
        {
            ShardId = shardId;
            EntityIds = entityIds;
        }

        #region Equals

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ShardState);
        }

        public bool Equals(ShardState other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return ShardId == other.ShardId
                && EntityIds.SetEquals(other.EntityIds);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 0;
                hashCode = (hashCode * 397) ^ ShardId.GetHashCode();
                foreach (var e in EntityIds)
                    hashCode = (hashCode * 397) ^ e.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ShardState[shardId={ShardId}, entityIds={string.Join(", ", EntityIds)}]";
        }

        #endregion
    }


    /// <summary>
    /// Discover if the shard region is registered with the coordinator.
    /// Not serializable as only to be sent to the local shard region
    /// Response is [[ShardRegionState]]
    /// </summary>
    internal sealed class GetShardRegionStatus : IShardRegionQuery, INoSerializationVerificationNeeded
    {
        public static readonly GetShardRegionStatus Instance = new GetShardRegionStatus();

        private GetShardRegionStatus()
        {
        }
    }

    /// <summary>
    /// Status of a ShardRegion. Only for local requests so not serializable.
    /// </summary>
    internal sealed class ShardRegionStatus : INoSerializationVerificationNeeded
    {
        public readonly string TypeName;
        public readonly bool RegisteredWithCoordinator;

        public ShardRegionStatus(string typeName, bool registeredWithCoordinator)
        {
            TypeName = typeName;
            RegisteredWithCoordinator = registeredWithCoordinator;
        }
    }
}
