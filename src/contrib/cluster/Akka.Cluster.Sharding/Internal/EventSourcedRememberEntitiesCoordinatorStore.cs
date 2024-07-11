//-----------------------------------------------------------------------
// <copyright file="EventSourcedRememberEntitiesCoordinatorStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    internal class EventSourcedRememberEntitiesCoordinatorStore : PersistentActor
    {
        public static Props Props(string typeName, ClusterShardingSettings settings)
        {
            return Actor.Props.Create(() => new EventSourcedRememberEntitiesCoordinatorStore(typeName, settings));
        }

        public sealed class State : IClusterShardingSerializable, IEquatable<State>
        {
            public State(IImmutableSet<ShardId> shards, bool writtenMigrationMarker = false)
            {
                Shards = shards;
                WrittenMigrationMarker = writtenMigrationMarker;
            }

            public IImmutableSet<string> Shards { get; }
            public bool WrittenMigrationMarker { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as State);
            }

            public bool Equals(State other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shards.SetEquals(other.Shards)
                    && WrittenMigrationMarker.Equals(other.WrittenMigrationMarker);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Shards)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    hashCode = (hashCode * 397) ^ WrittenMigrationMarker.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"State(shards:{string.Join(", ", Shards)}, writtenMigrationMarker:{WrittenMigrationMarker})";

            #endregion
        }

        public sealed class MigrationMarker : IClusterShardingSerializable
        {
            public static readonly MigrationMarker Instance = new();

            private MigrationMarker()
            {
            }

            /// <inheritdoc/>
            public override string ToString() => "MigrationMarker";
        }

        public EventSourcedRememberEntitiesCoordinatorStore(
            string typeName,
            ClusterShardingSettings settings)
        {
            TypeName = typeName;
            Settings = settings;

            //should have been this: $"/sharding/{typeName}Coordinator";
            PersistenceId = $"/system/sharding/{typeName}Coordinator/singleton/coordinator"; //used for backward compatibility instead of Self.Path.ToStringWithoutAddress()

            JournalPluginId = settings.JournalPluginId;
            SnapshotPluginId = settings.SnapshotPluginId;
        }

        public string TypeName { get; }
        public ClusterShardingSettings Settings { get; }

        /// <summary>
        /// Uses the same persistence id as the old persistent coordinator so that the old data can be migrated
        /// without any user action
        /// </summary>
        public override string PersistenceId { get; }

        private readonly HashSet<ShardId> _shards = new();
        private bool _writtenMarker = false;

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case ShardId shardId:
                    _shards.Add(shardId);
                    return true;
                case SnapshotOffer offer when (offer.Snapshot is ShardCoordinator.CoordinatorState state):
                    _shards.UnionWith(state.Shards.Keys.Union(state.UnallocatedShards));
                    return true;
                case SnapshotOffer offer when (offer.Snapshot is State state):
                    _shards.UnionWith(state.Shards);
                    _writtenMarker = state.WrittenMigrationMarker;
                    return true;
                case RecoveryCompleted _:
                    Log.Debug("Recovery complete. Current shards [{0}]. Written Marker {1}", string.Join(", ", _shards), _writtenMarker);
                    if (!_writtenMarker)
                    {
                        Persist(MigrationMarker.Instance, _ =>
                        {
                            Log.Debug("Written migration marker");
                            _writtenMarker = true;
                        });
                    }
                    return true;
                case MigrationMarker _:
                    _writtenMarker = true;
                    return true;
            }
            Log.Error(
                "Unexpected message type [{0}]. Are you migrating from persistent coordinator state store? If so you must add the migration event adapter. Shards will not be restarted.",
                message.GetType());
            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case RememberEntitiesCoordinatorStore.GetShards _:
                    Sender.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(_shards.ToImmutableHashSet()));
                    return true;

                case RememberEntitiesCoordinatorStore.AddShard add:
                    PersistAsync(add.ShardId, shardId =>
                    {
                        _shards.Add(shardId);
                        Sender.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(shardId));
                        SaveSnapshotWhenNeeded();
                    });
                    return true;

                case SaveSnapshotSuccess e:
                    Log.Debug("Snapshot saved successfully");
                    InternalDeleteMessagesBeforeSnapshot(
                        e,
                        Settings.TuningParameters.KeepNrOfBatches,
                        Settings.TuningParameters.SnapshotAfter);
                    return true;

                case SaveSnapshotFailure e:
                    Log.Warning(e.Cause, "Snapshot failure: [{0}]", e.Cause.Message);
                    return true;

                case DeleteMessagesSuccess e:
                    var deleteTo = e.ToSequenceNr - 1;
                    var deleteFrom =
                        Math.Max(0, deleteTo - (Settings.TuningParameters.KeepNrOfBatches * Settings.TuningParameters.SnapshotAfter));
                    Log.Debug(
                        "Messages to [{0}] deleted successfully. Deleting snapshots from [{1}] to [{2}]",
                        e.ToSequenceNr,
                        deleteFrom,
                        deleteTo);
                    DeleteSnapshots(new SnapshotSelectionCriteria(minSequenceNr: deleteFrom, maxTimeStamp: DateTime.MaxValue, maxSequenceNr: deleteTo));
                    return true;

                case DeleteMessagesFailure e:
                    Log.Warning(e.Cause, "Messages to [{0}] deletion failure: [{1}]", e.ToSequenceNr, e.Cause.Message);
                    return true;

                case DeleteSnapshotsSuccess e:
                    Log.Debug("Snapshots matching [{0}] deleted successfully", e.Criteria);
                    return true;

                case DeleteSnapshotsFailure e:
                    Log.Warning(e.Cause, "Snapshots matching [{0}] deletion failure: [{1}]", e.Criteria, e.Cause.Message);
                    return true;
            }
            return false;
        }

        private void SaveSnapshotWhenNeeded()
        {
            if (LastSequenceNr % Settings.TuningParameters.SnapshotAfter == 0 && LastSequenceNr != 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(new State(_shards.ToImmutableHashSet(), _writtenMarker));
            }
        }
    }
}
