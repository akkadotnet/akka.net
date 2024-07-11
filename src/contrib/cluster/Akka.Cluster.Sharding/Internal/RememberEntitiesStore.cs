//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    /// <summary>
    /// Created once for the shard guardian
    /// </summary>
    public interface IRememberEntitiesProvider
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

    /// <summary>
    /// Could potentially become an open SPI in the future.
    ///
    /// Implementations are responsible for each of the methods failing the returned future after a timeout.
    /// </summary>
    internal static class RememberEntitiesShardStore
    {
        /// <summary>
        /// SPI protocol for a remember entities shard store
        /// </summary>
        public interface ICommand
        {
        }

        /// <summary>
        /// Note: the store is not expected to receive and handle new update before it has responded to the previous one
        /// </summary>
        public sealed class Update : ICommand, IEquatable<Update>
        {
            public Update(IImmutableSet<EntityId> started, IImmutableSet<EntityId> stopped)
            {
                Started = started;
                Stopped = stopped;
            }

            public IImmutableSet<EntityId> Started { get; }
            public IImmutableSet<EntityId> Stopped { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as Update);
            }

            public bool Equals(Update other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Started.SetEquals(other.Started)
                    && Stopped.SetEquals(other.Stopped);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Started)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    foreach (var s in Stopped)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"Update(started:{string.Join(", ", Started)}, stopped:{string.Join(", ", Stopped)})";

            #endregion
        }

        /// <summary>
        /// responses for Update
        /// </summary>
        public sealed class UpdateDone : ICommand, IEquatable<UpdateDone>
        {
            public UpdateDone(IImmutableSet<EntityId> started, IImmutableSet<EntityId> stopped)
            {
                Started = started;
                Stopped = stopped;
            }

            public IImmutableSet<EntityId> Started { get; }
            public IImmutableSet<EntityId> Stopped { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as UpdateDone);
            }

            public bool Equals(UpdateDone other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Started.SetEquals(other.Started)
                    && Stopped.SetEquals(other.Stopped);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Started)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    foreach (var s in Stopped)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"UpdateDone(started:{string.Join(", ", Started)}, stopped:{string.Join(", ", Stopped)})";

            #endregion
        }

        public sealed class GetEntities : ICommand
        {
            public static readonly GetEntities Instance = new();

            private GetEntities()
            {

            }

            /// <inheritdoc/>
            public override string ToString() => "GetEntities";
        }

        public sealed class RememberedEntities : ICommand, IEquatable<RememberedEntities>
        {
            public RememberedEntities(IImmutableSet<EntityId> entities)
            {
                Entities = entities;
            }

            public IImmutableSet<EntityId> Entities { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as RememberedEntities);
            }

            public bool Equals(RememberedEntities other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Entities.SetEquals(other.Entities);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Entities)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"RememberedEntities({string.Join(", ", Entities)})";

            #endregion
        }
    }

    /// <summary>
    /// Could potentially become an open SPI in the future.
    /// </summary>
    internal static class RememberEntitiesCoordinatorStore
    {
        /// <summary>
        /// SPI protocol for a remember entities coordinator store
        /// </summary>
        public interface ICommand
        {
        }

        /// <summary>
        /// Sent once for every started shard (but could be retried), should result in a response of either
        /// UpdateDone or UpdateFailed
        /// </summary>
        public sealed class AddShard : ICommand, IEquatable<AddShard>
        {
            public AddShard(ShardId shardId)
            {
                ShardId = shardId;
            }

            public ShardId ShardId { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as AddShard);
            }

            public bool Equals(AddShard other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardId.Equals(other.ShardId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardId.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"AddShard({ShardId})";

            #endregion
        }

        public sealed class UpdateDone : ICommand, IEquatable<UpdateDone>
        {
            public UpdateDone(ShardId shardId)
            {
                ShardId = shardId;
            }

            public ShardId ShardId { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as UpdateDone);
            }

            public bool Equals(UpdateDone other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardId.Equals(other.ShardId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardId.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"UpdateDone({ShardId})";

            #endregion
        }

        public sealed class UpdateFailed : ICommand, IEquatable<UpdateFailed>
        {
            public UpdateFailed(ShardId shardId)
            {
                ShardId = shardId;
            }

            public ShardId ShardId { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as UpdateFailed);
            }

            public bool Equals(UpdateFailed other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardId.Equals(other.ShardId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardId.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"UpdateFailed({ShardId})";

            #endregion
        }

        /// <summary>
        /// Sent once when the coordinator starts (but could be retried), should result in a response of
        /// RememberedShards
        /// </summary>
        public sealed class GetShards : ICommand
        {
            public static readonly GetShards Instance = new();

            private GetShards()
            {
            }

            /// <inheritdoc/>
            public override string ToString() => "GetShards";
        }

        public sealed class RememberedShards : ICommand, IEquatable<RememberedShards>
        {
            public RememberedShards(IImmutableSet<ShardId> entities)
            {
                Entities = entities;
            }

            public IImmutableSet<ShardId> Entities { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as RememberedShards);
            }

            public bool Equals(RememberedShards other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Entities.SetEquals(other.Entities);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Entities)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"RememberedShards({string.Join(", ", Entities)})";

            #endregion
        }

        // No message for failed load since we eager lod the set of shards, may need to change in the future
    }
}
