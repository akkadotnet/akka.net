//-----------------------------------------------------------------------
// <copyright file="PersistentShard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence;
using Akka.Util.Internal;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Coordination;
using Akka.Actor.Scheduler;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;
    using Msg = Object;

    /// <summary>
    /// This actor creates children entity actors on demand that it is told to be
    /// responsible for. It is used when `rememberEntities` is enabled.
    /// </summary>
    internal sealed class PersistentShard : PersistentActor, IShard, IWithTimers
    {
        IActorContext IShard.Context => Context;
        IActorRef IShard.Self => Self;
        IActorRef IShard.Sender => Sender;
        ILoggingAdapter IShard.Log => base.Log;
        void IShard.Unhandled(object message) => base.Unhandled(message);

        public string TypeName { get; }
        public string ShardId { get; }
        public Func<string, Props> EntityProps { get; }
        public ClusterShardingSettings Settings { get; }
        public ExtractEntityId ExtractEntityId { get; }
        public ExtractShardId ExtractShardId { get; }
        public object HandOffStopMessage { get; }
        public IActorRef HandOffStopper { get; set; }
        public Shard.ShardState State { get; set; } = Shard.ShardState.Empty;
        public ImmutableDictionary<string, IActorRef> RefById { get; set; } = ImmutableDictionary<string, IActorRef>.Empty;
        public ImmutableDictionary<IActorRef, string> IdByRef { get; set; } = ImmutableDictionary<IActorRef, string>.Empty;
        public ImmutableDictionary<string, long> LastMessageTimestamp { get; set; } = ImmutableDictionary<string, long>.Empty;
        public ImmutableHashSet<IActorRef> Passivating { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableDictionary<string, ImmutableList<(object, IActorRef)>> MessageBuffers { get; set; } = ImmutableDictionary<string, ImmutableList<(object, IActorRef)>>.Empty;
        public ICancelable PassivateIdleTask { get; }

        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy { get; }

        public ITimerScheduler Timers { get; set; }
        public Lease Lease { get; }
        public TimeSpan LeaseRetryInterval { get; } = TimeSpan.FromSeconds(5); // won't be used

        public PersistentShard(
            string typeName,
            string shardId,
            Func<string, Props> entityProps,
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

            var idleInterval = TimeSpan.FromTicks(Settings.PassivateIdleEntityAfter.Ticks / 2);
            PassivateIdleTask = Settings.ShouldPassivateIdleEntities
                ? Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(idleInterval, idleInterval, Self, Shard.PassivateIdleTick.Instance, Self)
                : null;

            if (settings.LeaseSettings != null)
            {
                Lease = LeaseProvider.Get(Context.System).GetLease(
                    $"{Context.System.Name}-shard-{typeName}-{shardId}",
                    settings.LeaseSettings.LeaseImplementation,
                    Cluster.Get(Context.System).SelfAddress.HostPort());

                LeaseRetryInterval = settings.LeaseSettings.LeaseRetryInterval;
            }
        }

        public override string PersistenceId { get; }

        protected override void PostStop()
        {
            this.ReleaseLeaseIfNeeded();
            PassivateIdleTask?.Cancel();
            base.PostStop();
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case SaveSnapshotSuccess m:
                    Log.Debug("PersistentShard snapshot saved successfully");
                    InternalDeleteMessagesBeforeSnapshot(m, Settings.TunningParameters.KeepNrOfBatches, Settings.TunningParameters.SnapshotAfter);
                    break;
                case SaveSnapshotFailure m:
                    Log.Warning("PersistentShard snapshot failure: [{0}]", m.Cause.Message);
                    break;
                case DeleteMessagesSuccess m:
                    var deleteTo = m.ToSequenceNr - 1;
                    var deleteFrom = Math.Max(0, deleteTo - Settings.TunningParameters.KeepNrOfBatches * Settings.TunningParameters.SnapshotAfter);
                    Log.Debug("PersistentShard messages to [{0}] deleted successfully. Deleting snapshots from [{1}] to [{2}]", m.ToSequenceNr, deleteFrom, deleteTo);
                    DeleteSnapshots(new SnapshotSelectionCriteria(deleteTo, DateTime.MaxValue, deleteFrom));
                    break;
                case DeleteMessagesFailure m:
                    Log.Warning("PersistentShard messages to [{0}] deletion failure: [{1}]", m.ToSequenceNr, m.Cause.Message);
                    break;
                case DeleteSnapshotsSuccess m:
                    Log.Debug("PersistentShard snapshots matching [{0}] deleted successfully", m.Criteria);
                    break;
                case DeleteSnapshotsFailure m:
                    Log.Warning("PersistentShard snapshots matching [{0}] deletion failure: [{1}]", m.Criteria, m.Cause.Message);
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
                case SnapshotOffer offer when offer.Snapshot is Shard.ShardState state:
                    State = state;
                    return true;
                case RecoveryCompleted _:
                    this.AcquireLeaseIfNeeded(); // onLeaseAcquired called when completed
                    Log.Debug("PersistentShard recovery completed shard [{0}] with [{1}] entities", ShardId, State.Entries.Count);
                    return true;
            }
            return false;
        }

        public void OnLeaseAcquired()
        {
            Log.Debug("Shard initialized");
            Context.Parent.Tell(new ShardInitialized(ShardId));
            Context.Become(ReceiveCommand);
            RestartRememberedEntities();
            this.Stash.UnstashAll();
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
            if (!IdByRef.TryGetValue(tref, out var id)) return;
            IdByRef = IdByRef.Remove(tref);
            RefById = RefById.Remove(id);

            if (PassivateIdleTask != null)
            {
                LastMessageTimestamp = LastMessageTimestamp.Remove(id);
            }

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

        public void DeliverTo(string id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (child.IsNobody())
            {
                if (State.Entries.Contains(id))
                {
                    if (MessageBuffers.ContainsKey(id)) // this may happen when entity is stopped without passivation
                    {
                        throw new InvalidOperationException($"Message buffers contains id [{id}].");
                    }
                    this.GetOrCreateEntity(id).Tell(payload, sender);
                }
                else
                {
                    // Note; we only do this if remembering, otherwise the buffer is an overhead
                    MessageBuffers = MessageBuffers.SetItem(id, ImmutableList<(object, IActorRef)>.Empty.Add((message, sender)));
                    ProcessChange(new Shard.EntityStarted(id), this.SendMessageBuffer);
                }
            }
            else
            {
                this.TouchLastMessageTimestamp(id);
                child.Tell(payload, sender);
            }
        }
    }
}
