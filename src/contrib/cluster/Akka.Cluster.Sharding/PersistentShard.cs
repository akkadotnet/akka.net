using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence;

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
        protected int PersistCount = 0;

        private readonly string _persistenceId;
        public override string PersistenceId { get { return _persistenceId; } }

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

        protected override bool ReceiveCommand(object message)
        {
            return HandleCommand(message);
        }

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
                foreach (var entry in State.Entries)
                    GetEntity(entry);

                base.Initialized();
                Log.Debug("Shard recovery completed [{0}]", ShardId);
            }
            else return false;
            return true;
        }

        protected override void ProcessChange<T>(T evt, Action<T> handler)
        {
            SaveSnapshotIfNeeded();
            Persist(evt, handler);
        }

        protected void SaveSnapshotIfNeeded()
        {
            PersistCount++;
            if ((PersistCount & Settings.TunningParameters.SnapshotAfter) == 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(State);
            }
        }

        protected override void EntityTerminated(IActorRef tref)
        {
            ShardId id;
            IImmutableList<Tuple<Msg, IActorRef>> buffer;

            if (IdByRef.TryGetValue(tref, out id))
            {
                if (MessageBuffers.TryGetValue(id, out buffer) && buffer.Count != 0)
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
                    {
                        ProcessChange(new EntityStopped(id), PassivateCompleted);
                    }
                }
            }

            Passivating = Passivating.Remove(tref);
        }

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

        public static Actor.Props Props(string typeName, ShardId shardId, Props entryProps, ClusterShardingSettings settings, IdExtractor idExtractor, ShardResolver shardResolver, object handOffStopMessage)
        {
            return Actor.Props.Create(() => new PersistentShard(typeName, shardId, entryProps, settings, idExtractor, shardResolver, handOffStopMessage)).WithDeploy(Deploy.Local);
        }
    }
}