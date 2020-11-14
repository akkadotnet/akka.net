//-----------------------------------------------------------------------
// <copyright file="EventSourcedRememberEntitiesShardStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence;

namespace Akka.Cluster.Sharding.Internal
{
    using EntityId = String;
    using ShardId = String;

    internal class EventSourcedRememberEntitiesShardStore : PersistentActor
    {
        /// <summary>
        /// An interface which represents a state change for the Shard
        /// </summary>
        public interface IStateChange : IClusterShardingSerializable
        {
        }

        /// <summary>
        /// Persistent state of the Shard.
        /// </summary>
        public sealed class State : IClusterShardingSerializable, IEquatable<State>
        {
            public State(IImmutableSet<EntityId> entities = null)
            {
                Entities = entities ?? ImmutableHashSet<EntityId>.Empty;
            }

            public IImmutableSet<EntityId> Entities { get; }

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
            public override string ToString() => $"State({string.Join(", ", Entities)})";

            #endregion
        }

        /// <summary>
        /// `State` change for starting a set of entities in this `Shard`
        /// </summary>
        public sealed class EntitiesStarted : IStateChange, IEquatable<EntitiesStarted>
        {
            public EntitiesStarted(IImmutableSet<EntityId> entities = null)
            {
                Entities = entities ?? ImmutableHashSet<EntityId>.Empty;
            }

            public IImmutableSet<EntityId> Entities { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as EntitiesStarted);
            }

            public bool Equals(EntitiesStarted other)
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
            public override string ToString() => $"EntitiesStarted({string.Join(", ", Entities)})";

            #endregion
        }

        public sealed class StartedAck
        {
            public static readonly StartedAck Instance = new StartedAck();

            private StartedAck()
            {
            }
        }

        /// <summary>
        /// `State` change for an entity which has terminated.
        /// </summary>
        public sealed class EntitiesStopped : IStateChange, IEquatable<EntitiesStopped>
        {
            public EntitiesStopped(IImmutableSet<EntityId> entities)
            {
                Entities = entities;
            }

            public IImmutableSet<EntityId> Entities { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as EntitiesStopped);
            }

            public bool Equals(EntitiesStopped other)
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
            public override string ToString() => $"EntitiesStopped({string.Join(", ", Entities)})";

            #endregion
        }

        public static Props Props(string typeName, ShardId shardId, ClusterShardingSettings settings)
        {
            return Actor.Props.Create(() => new EventSourcedRememberEntitiesShardStore(typeName, shardId, settings));
        }

        private readonly int maxUpdatesPerWrite;
        private State state = new State();

        /// <summary>
        /// Persistent actor keeping the state for Akka Persistence backed remember entities (enabled through `state-store-mode=persistence`).
        ///
        /// @see [[ClusterSharding$ ClusterSharding extension]]
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="shardId"></param>
        /// <param name="settings"></param>
        public EventSourcedRememberEntitiesShardStore(
            string typeName,
            ShardId shardId,
            ClusterShardingSettings settings)
        {
            TypeName = typeName;
            ShardId = shardId;
            Settings = settings;
            maxUpdatesPerWrite = Context.System.Settings.Config
                .GetInt("akka.cluster.sharding.event-sourced-remember-entities-store.max-updates-per-write");

            Log.Debug("Starting up EventSourcedRememberEntitiesStore");

            PersistenceId = $"/sharding/{TypeName}Shard/{ShardId}";

            JournalPluginId = settings.JournalPluginId;
            SnapshotPluginId = settings.SnapshotPluginId;
        }

        public string TypeName { get; }

        public ShardId ShardId { get; }

        public ClusterShardingSettings Settings { get; }

        public override string PersistenceId { get; }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case EntitiesStarted started:
                    state = new State(state.Entities.Union(started.Entities));
                    return true;
                case EntitiesStopped stopped:
                    state = new State(state.Entities.Except(stopped.Entities));
                    return true;
                case SnapshotOffer offer when (offer.Snapshot is State snapshot):
                    state = snapshot;
                    return true;
                case RecoveryCompleted _:
                    Log.Debug("Recovery completed for shard [{0}] with [{1}] entities", ShardId, state.Entities.Count);
                    return true;
            }
            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case RememberEntitiesShardStore.Update update:
                    var events = new List<IStateChange>();
                    if (update.Started.Count > 0)
                        events.Add(new EntitiesStarted(update.Started));
                    if (update.Stopped.Count > 0)
                        events.Add(new EntitiesStopped(update.Stopped));

                    var left = events.Count;
                    void PersistEventsAndHandleComplete(IEnumerable<IStateChange> evts)
                    {
                        PersistAll(evts, _ =>
                        {
                            left -= 1;
                            if (left == 0)
                            {
                                Sender.Tell(new RememberEntitiesShardStore.UpdateDone(update.Started, update.Stopped));
                                state = new State(state.Entities.Union(update.Started).Except(update.Stopped));
                                SaveSnapshotWhenNeeded();
                            }
                        });
                    }
                    if (left <= maxUpdatesPerWrite)
                    {
                        // optimized when batches are small
                        PersistEventsAndHandleComplete(events);
                    }
                    else
                    {
                        // split up in several writes so we don't hit journal limit
                        foreach (var g in events.Grouped(maxUpdatesPerWrite))
                            PersistEventsAndHandleComplete(g);
                    }
                    return true;

                case RememberEntitiesShardStore.GetEntities _:
                    Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(state.Entities));
                    return true;

                case SaveSnapshotSuccess e:
                    Log.Debug("Snapshot saved successfully");
                    InternalDeleteMessagesBeforeSnapshot(e, Settings.TuningParameters.KeepNrOfBatches, Settings.TuningParameters.SnapshotAfter);
                    return true;

                case SaveSnapshotFailure e:
                    Log.Warning(e.Cause, "Snapshot failure: [{0}]", e.Cause.Message);
                    return true;

                case DeleteMessagesSuccess e:
                    var deleteTo = e.ToSequenceNr - 1;
                    var deleteFrom = Math.Max(0, deleteTo - (Settings.TuningParameters.KeepNrOfBatches * Settings.TuningParameters.SnapshotAfter));
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

        protected override void PostStop()
        {
            base.PostStop();
            Log.Debug("Store stopping");
        }

        private void SaveSnapshotWhenNeeded()
        {
            if (LastSequenceNr % Settings.TuningParameters.SnapshotAfter == 0 && LastSequenceNr != 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(state);
            }
        }
    }
}
