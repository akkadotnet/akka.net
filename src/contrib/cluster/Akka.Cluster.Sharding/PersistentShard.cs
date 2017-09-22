//-----------------------------------------------------------------------
// <copyright file="PersistentShard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence;
using Akka.Util.Internal;
using System.Threading.Tasks;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;
    using Msg = Object;

    public class PersistentShardActor : PersistentActor, IPersistentActorContext
    {
        private readonly PersistentShard _shardSemantic;

        public PersistentShardActor(
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
        {
            _shardSemantic = new PersistentShard(
                context: Context,
                persistentContext: this,
                unhandled: Unhandled,
                typeName: typeName,
                shardId: shardId,
                entityProps: entityProps,
                settings: settings,
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                handOffStopMessage: handOffStopMessage);

            JournalPluginId = _shardSemantic.JournalPluginId;
            SnapshotPluginId = _shardSemantic.SnapshotPluginId;
        }

        public override string PersistenceId => _shardSemantic.PersistenceId;

        protected override bool ReceiveCommand(object message)
        {
            return _shardSemantic.ReceiveCommand(message);
        }

        protected override bool ReceiveRecover(object message)
        {
            return _shardSemantic.ReceiveRecover(message);
        }
    }

    public interface IPersistentActorContext
    {
        long LastSequenceNr { get; }
        long SnapshotSequenceNr { get; }

        void Persist<TEvent>(TEvent @event, Action<TEvent> handler);
        void DeleteMessages(long toSequenceNr);
        void SaveSnapshot(object snapshot);
        void DeleteSnapshot(long sequenceNr);
        void DeleteSnapshots(SnapshotSelectionCriteria criteria);
    }

    /// <summary>
    /// This actor creates children entity actors on demand that it is told to be
    /// responsible for. It is used when `rememberEntities` is enabled.
    /// </summary>
    public class PersistentShard : Shard
    {
        private readonly IPersistentActorContext _persistent;

        private readonly string _persistenceId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="persistentContext">TBD</param>
        /// <param name="unhandled">TBD</param>
        /// <param name="typeName">TBD</param>
        /// <param name="shardId">TBD</param>
        /// <param name="entityProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        public PersistentShard(
            IActorContext context,
            IPersistentActorContext persistentContext,
            Action<object> unhandled,
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
            : base(context, unhandled, typeName, shardId, entityProps, settings, extractEntityId, extractShardId, handOffStopMessage)
        {
            _persistent = persistentContext;
            _persistenceId = "/sharding/" + TypeName + "Shard/" + ShardId;
        }

        public string PersistenceId => _persistenceId;
        public string JournalPluginId => Settings.JournalPluginId;
        public string SnapshotPluginId => Settings.SnapshotPluginId;

        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy => Settings.TunningParameters.EntityRecoveryStrategy == "constant"
            ? EntityRecoveryStrategy.ConstantStrategy(
                _context.System,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyFrequency,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities)
            : EntityRecoveryStrategy.AllStrategy;

        public override void Initialized()
        {
            // would be initialized after recovery completed
        }

        public bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case SaveSnapshotSuccess m:
                    Log.Debug("PersistentShard snapshot saved successfully");
                    /*
                    * delete old events but keep the latest around because
                    *
                    * it's not safe to delete all events immediate because snapshots are typically stored with a weaker consistency
                    * level which means that a replay might "see" the deleted events before it sees the stored snapshot,
                    * i.e. it will use an older snapshot and then not replay the full sequence of events
                    *
                    * for debugging if something goes wrong in production it's very useful to be able to inspect the events
                    */
                    var deleteToSequenceNr = m.Metadata.SequenceNr - Settings.TunningParameters.KeepNrOfBatches * Settings.TunningParameters.SnapshotAfter;
                    if (deleteToSequenceNr > 0)
                    {
                        _persistent.DeleteMessages(deleteToSequenceNr);
                    }
                    break;
                case SaveSnapshotFailure m:
                    Log.Warning("PersistentShard snapshot failure: {0}", m.Cause.Message);
                    break;
                case DeleteMessagesSuccess m:
                    Log.Debug("PersistentShard messages to {0} deleted successfully", m.ToSequenceNr);
                    _persistent.DeleteSnapshots(new SnapshotSelectionCriteria(m.ToSequenceNr - 1));
                    break;

                case DeleteMessagesFailure m:
                    Log.Warning("PersistentShard messages to {0} deletion failure: {1}", m.ToSequenceNr, m.Cause.Message);
                    break;
                case DeleteSnapshotsSuccess m:
                    Log.Debug("PersistentShard snapshots matching {0} deleted successfully", m.Criteria);
                    break;
                case DeleteSnapshotsFailure m:
                    Log.Warning("PersistentShard snapshots matching {0} deletion failure: {1}", m.Criteria, m.Cause.Message);
                    break;
                default:
                    return HandleCommand(message);
            }
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case EntityStarted started:
                    State = new ShardState(State.Entries.Add(started.EntityId));
                    return true;
                case EntityStopped stopped:
                    State = new ShardState(State.Entries.Remove(stopped.EntityId));
                    return true;
                case SnapshotOffer offer when offer.Snapshot is ShardState:
                    State = (ShardState)offer.Snapshot;
                    return true;
                case RecoveryCompleted _:
                    RestartRememberedEntities();
                    base.Initialized();
                    Log.Debug("PersistentShard recovery completed shard [{0}] with [{1}] entities", ShardId, State.Entries.Count);
                    return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="evt">TBD</param>
        /// <param name="handler">TBD</param>
        protected override void ProcessChange<T>(T evt, Action<T> handler)
        {
            SaveSnapshotWhenNeeded();
            _persistent.Persist(evt, handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void SaveSnapshotWhenNeeded()
        {
            if (_persistent.LastSequenceNr % Settings.TunningParameters.SnapshotAfter == 0 && _persistent.LastSequenceNr != 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", _persistent.SnapshotSequenceNr);
                _persistent.SaveSnapshot(State);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tref">TBD</param>
        protected override void EntityTerminated(IActorRef tref)
        {
            if (IdByRef.TryGetValue(tref, out var id))
            {
                IdByRef = IdByRef.Remove(tref);
                RefById = RefById.Remove(id);

                if (MessageBuffers.TryGetValue(id, out var buffer) && buffer.Count != 0)
                {
                    //Note; because we're not persisting the EntityStopped, we don't need
                    // to persist the EntityStarted either.
                    Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                    SendMessageBuffer(new EntityStarted(id));
                }
                else
                {
                    if (!Passivating.Contains(tref))
                    {
                        Log.Debug("Entity [{0}] stopped without passivating, will restart after backoff", id);
                        _context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.EntityRestartBackoff, _context.Self, new RestartEntity(id), ActorRefs.NoSender);
                    }
                    else
                        ProcessChange(new EntityStopped(id), PassivateCompleted);
                }

                Passivating = Passivating.Remove(tref);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="payload">TBD</param>
        /// <param name="sender">TBD</param>
        /// <returns>TBD</returns>
        protected override void DeliverTo(string id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = _context.Child(name);
            if (Equals(child, ActorRefs.Nobody))
            {
                // Note; we only do this if remembering, otherwise the buffer is an overhead
                MessageBuffers = MessageBuffers.SetItem(id, ImmutableList<Tuple<object, IActorRef>>.Empty.Add(Tuple.Create(message, sender)));
                ProcessChange(new EntityStarted(id), SendMessageBuffer);
            }
            else
                child.Tell(payload, sender);
        }

        private void RestartRememberedEntities()
        {
            RememberedEntitiesRecoveryStrategy.RecoverEntities(State.Entries).ForEach(scheduledRecovery =>
                scheduledRecovery.ContinueWith(t => new RestartEntities(t.Result), TaskContinuationOptions.ExecuteSynchronously).PipeTo(_context.Self, _context.Self));
        }
    }
}