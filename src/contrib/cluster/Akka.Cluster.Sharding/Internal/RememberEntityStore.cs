using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Akka.Actor;
using Akka.Annotations;

namespace Akka.Cluster.Sharding.Internal
{
    using ShardId = String;
    using EntityId = String;

    internal interface IRememberEntitiesProvider
    {
        /// <summary>
        /// Called once per started shard coordinator to create the remember entities coordinator store.
        ///
        /// Note that this is not used for the deprecated persistent coordinator which has its own impl for keeping track of
        /// remembered shards.
        /// </summary>
        /// <returns>an actor that handles the protocol defined in [[RememberEntitiesCoordinatorStore]]</returns>
        Props CoordinatorStoreProps();

        /// <summary>
        /// Called once per started shard to create the remember entities shard store
        /// </summary>
        /// <param name="shardId"></param>
        /// <returns>an actor that handles the protocol defined in [[RememberEntitiesShardStore]]</returns>
        Props ShardStoreProps(ShardId shardId);
    }

    [InternalApi]
    internal static class RememberEntitiesShardStore
    {
        // SPI protocol for a remember entities shard store
        public interface ICommand {}

        // Note: the store is not expected to receive and handle new update before it has responded to the previous one
        public sealed class Update : ICommand
        {
            public Update(IImmutableSet<string> started, IImmutableSet<string> stopped)
            {
                Started = started;
                Stopped = stopped;
            }

            public IImmutableSet<EntityId> Started { get; }
            public IImmutableSet<EntityId> Stopped { get; }
        }

        // responses for Update
        public sealed class UpdateDone
        {
            public UpdateDone(IImmutableSet<string> started, IImmutableSet<string> stopped)
            {
                Started = started;
                Stopped = stopped;
            }

            public IImmutableSet<EntityId> Started { get; }
            public IImmutableSet<EntityId> Stopped { get; }

            public void Deconstruct(out IImmutableSet<EntityId> started, out IImmutableSet<EntityId> stopped)
            {
                started = Started;
                stopped = Stopped;
            }
        }

        public sealed class GetEntities : ICommand
        {
            public static readonly GetEntities Instance = new GetEntities();
            private GetEntities() { }
        }

        public sealed class RememberedEntities
        {
            public RememberedEntities(IImmutableSet<string> entities)
            {
                Entities = entities;
            }

            public IImmutableSet<EntityId> Entities { get; }
        }
    }

    [InternalApi]
    internal static class RememberEntitiesCoordinatorStore
    {
        // SPI protocol for a remember entities coordinator store
        public interface ICommand {}

        /// <summary>
        /// Sent once for every started shard (but could be retried), should result in a response of either
        /// UpdateDone or UpdateFailed
        /// </summary>
        public sealed class AddShard : ICommand
        {
            public AddShard(string entityId)
            {
                EntityId = entityId;
            }

            public ShardId EntityId { get; }
        }

        public sealed class UpdateDone
        {
            public UpdateDone(string entityId)
            {
                EntityId = entityId;
            }

            public ShardId EntityId { get; }
        }

        public sealed class UpdateFailed
        {
            public UpdateFailed(string entityId)
            {
                EntityId = entityId;
            }

            public ShardId EntityId { get; }
        }

        /// <summary>
        /// Sent once when the coordinator starts (but could be retried), should result in a response of
        /// RememberedShards
        /// </summary>
        public sealed class GetShards : ICommand
        {
            public static readonly GetShards Instance = new GetShards();
            private GetShards() {}
        }

        public sealed class RememberedShards
        {
            public RememberedShards(IImmutableSet<string> entities)
            {
                Entities = entities;
            }

            public IImmutableSet<ShardId> Entities { get; }
        }
        // No message for failed load since we eager load the set of shards, may need to change in the future
    }
}
