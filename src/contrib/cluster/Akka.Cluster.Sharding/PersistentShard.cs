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

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntryId = String;
    using Msg = Object;

    /// <summary>
    /// This actor creates children entity actors on demand that it is told to be 
    /// responsible for. It is used when `rememberEntities` is enabled.
    /// </summary>
    public class PersistentShard : Shard
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected int PersistCount = 0;

        private readonly string _persistenceId;
        /// <summary>
        /// TBD
        /// </summary>
        public override string PersistenceId { get { return _persistenceId; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="shardId">TBD</param>
        /// <param name="entityProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        public PersistentShard(
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            IdExtractor extractEntityId,
            ShardResolver extractShardId,
            object handOffStopMessage)
            : base(typeName, shardId, entityProps, settings, extractEntityId, extractShardId, handOffStopMessage)
        {
            _persistenceId = "/sharding/" + TypeName + "Shard/" + ShardId;
            JournalPluginId = Settings.JournalPluginId;
            SnapshotPluginId = Settings.SnapshotPluginId;

        }

        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy => Settings.TunningParameters.EntityRecoveryStrategy == "constant"
            ? EntityRecoveryStrategy.ConstantStrategy(
                Context.System,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyFrequency,
                Settings.TunningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities)
            : EntityRecoveryStrategy.AllStrategy;

        protected override bool ReceiveCommand(object message)
        {
            return HandleCommand(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool ReceiveRecover(object message)
        {
            SnapshotOffer offer;
            if (message is EntityStarted)
            {
                var started = (EntityStarted)message;
                State = new ShardState(State.Entries.Add(started.EntityId));
            }
            else if (message is EntityStopped)
            {
                var stopped = (EntityStopped)message;
                State = new ShardState(State.Entries.Remove(stopped.EntityId));
            }
            else if ((offer = message as SnapshotOffer) != null && offer.Snapshot is ShardState)
            {
                State = (ShardState)offer.Snapshot;
            }
            else if (message is RecoveryCompleted)
            {
                RestartRememberedEntities();
                base.Initialized();
                Log.Debug("PersistentShard recovery completed shard [{0}] with [{1}] entities", ShardId, State.Entries.Count);
            }
            else return false;
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="evt">TBD</param>
        /// <param name="handler">TBD</param>
        protected override void ProcessChange<T>(T evt, Action<T> handler)
        {
            SaveSnapshotIfNeeded();
            Persist(evt, handler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void SaveSnapshotIfNeeded()
        {
            PersistCount++;
            if ((PersistCount % Settings.TunningParameters.SnapshotAfter) == 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(State);
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
                if (MessageBuffers.TryGetValue(id, out var buffer) && buffer.Count != 0)
                {
                    // Note; because we're not persisting the EntityStopped, we don't need
                    // to persist the EntityStarted either.
                    Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                    SendMessageBuffer(new EntityStarted(id));
                }
                else
                {
                    if (!Passivating.Contains(tref))
                    {
                        Log.Debug("Entity [{0}] stopped without passivating, will restart after backoff", id);
                        Context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.EntityRestartBackoff, Sender, new RestartEntity(id), Self);
                    }
                    else
                        ProcessChange(new EntityStopped(id), PassivateCompleted);
                }
            }

            Passivating = Passivating.Remove(tref);
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
            var child = Context.Child(name);
            if (Equals(child, ActorRefs.Nobody))
            {
                // Note; we only do this if remembering, otherwise the buffer is an overhead
                MessageBuffers = MessageBuffers.SetItem(id, ImmutableList<Tuple<object, IActorRef>>.Empty.Add(Tuple.Create(message, sender)));
                SaveSnapshotIfNeeded();
                Persist(new EntityStarted(id), SendMessageBuffer);
            }
            else
                child.Tell(payload, sender);
        }
        
        private void RestartRememberedEntities()
        {
            RememberedEntitiesRecoveryStrategy.RecoverEntities(State.Entries).ForEach(scheduledRecovery =>
                scheduledRecovery.ContinueWith(t => new RestartEntities(t.Result)).PipeTo(Self));
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="shardId">TBD</param>
        /// <param name="entryProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="idExtractor">TBD</param>
        /// <param name="shardResolver">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        /// <returns>TBD</returns>
        public static Actor.Props Props(string typeName, ShardId shardId, Props entryProps, ClusterShardingSettings settings, IdExtractor idExtractor, ShardResolver shardResolver, object handOffStopMessage)
        {
            return Actor.Props.Create(() => new PersistentShard(typeName, shardId, entryProps, settings, idExtractor, shardResolver, handOffStopMessage)).WithDeploy(Deploy.Local);
        }
    }
}