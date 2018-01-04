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
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;
    using Msg = Object;

    /// <summary>
    /// This actor creates children entity actors on demand that it is told to be
    /// responsible for. It is used when `rememberEntities` is enabled.
    /// </summary>
    internal sealed class PersistentShard : PersistentActor, IShard
    {
        IActorContext IShard.Context => Context;
        IActorRef IShard.Self => Self;
        IActorRef IShard.Sender => Sender;
        ILoggingAdapter IShard.Log => base.Log;
        void IShard.Unhandled(object message) => base.Unhandled(message);

        public string TypeName { get; }
        public string ShardId { get; }
        public Props EntityProps { get; }
        public ClusterShardingSettings Settings { get; }
        public ExtractEntityId ExtractEntityId { get; }
        public ExtractShardId ExtractShardId { get; }
        public object HandOffStopMessage { get; }
        public IActorRef HandOffStopper { get; set; }
        public Shard.ShardState State { get; set; } = Shard.ShardState.Empty;
        public ImmutableDictionary<string, IActorRef> RefById { get; set; } = ImmutableDictionary<string, IActorRef>.Empty;
        public ImmutableDictionary<IActorRef, string> IdByRef { get; set; } = ImmutableDictionary<IActorRef, string>.Empty;
        public ImmutableHashSet<IActorRef> Passivating { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableDictionary<string, ImmutableList<Tuple<object, IActorRef>>> MessageBuffers { get; set; } = ImmutableDictionary<string, ImmutableList<Tuple<object, IActorRef>>>.Empty;

        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy { get; } 

        public PersistentShard(
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
        {
            TypeName = typeName;
            ShardId = shardId;
            EntityProps = entityProps;
            Settings = settings;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;

            PersistenceId = "/sharding/" + TypeName + "Shard/" + ShardId;
            JournalPluginId = settings.JournalPluginId;
            SnapshotPluginId = settings.SnapshotPluginId;
            RememberedEntitiesRecoveryStrategy = Settings.TunningParameters.EntityRecoveryStrategy == "constant"
                ? EntityRecoveryStrategy.ConstantStrategy(
                    Context.System,
                    Settings.TunningParameters.EntityRecoveryConstantRateStrategyFrequency,
                    Settings.TunningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities)
                : EntityRecoveryStrategy.AllStrategy;
        }

        public override string PersistenceId { get; }

        protected override bool ReceiveCommand(object message)
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
                        DeleteMessages(deleteToSequenceNr);
                    }
                    break;
                case SaveSnapshotFailure m:
                    Log.Warning("PersistentShard snapshot failure: {0}", m.Cause.Message);
                    break;
                case DeleteMessagesSuccess m:
                    Log.Debug("PersistentShard messages to {0} deleted successfully", m.ToSequenceNr);
                    DeleteSnapshots(new SnapshotSelectionCriteria(m.ToSequenceNr - 1));
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
                    return this.HandleCommand(message);
            }
            return true;
        }

        protected override bool ReceiveRecover(object message)
        {
            switch (message)
            {
                case Shard.EntityStarted started:
                    State = new Shard.ShardState(State.Entries.Add(started.EntityId));
                    return true;
                case Shard.EntityStopped stopped:
                    State = new Shard.ShardState(State.Entries.Remove(stopped.EntityId));
                    return true;
                case SnapshotOffer offer when offer.Snapshot is Shard.ShardState:
                    State = (Shard.ShardState)offer.Snapshot;
                    return true;
                case RecoveryCompleted _:
                    RestartRememberedEntities();
                    this.Initialized();
                    Log.Debug("PersistentShard recovery completed shard [{0}] with [{1}] entities", ShardId, State.Entries.Count);
                    return true;
            }
            return false;
        }

        private void RestartRememberedEntities()
        {
            RememberedEntitiesRecoveryStrategy.RecoverEntities(State.Entries).ForEach(scheduledRecovery =>
                scheduledRecovery.ContinueWith(t => new Shard.RestartEntities(t.Result), TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self, Self));
        }

        public void SaveSnapshotWhenNeeded()
        {
            if (LastSequenceNr % Settings.TunningParameters.SnapshotAfter == 0 && LastSequenceNr != 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(State);
            }
        }
        public void ProcessChange<T>(T evt, Action<T> handler) where T : Shard.StateChange
        {
            SaveSnapshotWhenNeeded();
            Persist(evt, handler);
        }

        public void EntityTerminated(IActorRef tref)
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
                    this.SendMessageBuffer(new Shard.EntityStarted(id));
                }
                else
                {
                    if (!Passivating.Contains(tref))
                    {
                        Log.Debug("Entity [{0}] stopped without passivating, will restart after backoff", id);
                        Context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.EntityRestartBackoff, Self, new Shard.RestartEntity(id), ActorRefs.NoSender);
                    }
                    else
                        ProcessChange(new Shard.EntityStopped(id), this.PassivateCompleted);
                }

                Passivating = Passivating.Remove(tref);
            }
        }

        public void DeliverTo(string id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (Equals(child, ActorRefs.Nobody))
            {
                // Note; we only do this if remembering, otherwise the buffer is an overhead
                MessageBuffers = MessageBuffers.SetItem(id, ImmutableList<Tuple<object, IActorRef>>.Empty.Add(Tuple.Create(message, sender)));
                ProcessChange(new Shard.EntityStarted(id), this.SendMessageBuffer);
            }
            else
                child.Tell(payload, sender);
        }
    }
}