//-----------------------------------------------------------------------
// <copyright file="Shard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntityId = String;
    using Msg = Object;

    //TODO: figure out how not to derive from persistent actor for the sake of alternative ddata based impl
    public abstract class Shard : PersistentActor
    {
        #region messages

        protected interface IShardCommand { }
        public interface IShardQuery { }

        /// <summary>
        /// When a <see cref="StateChange"/> fails to write to the journal, we will retry it after a back off.
        /// </summary>
        [Serializable]
        internal protected class RetryPersistence : IShardCommand
        {
            public readonly StateChange Payload;

            public RetryPersistence(StateChange payload)
            {
                Payload = payload;
            }
        }

        /// <summary>
        /// The Snapshot tick for the shards.
        /// </summary>
        [Serializable]
        internal protected sealed class SnapshotTick : IShardCommand
        {
            public static readonly SnapshotTick Instance = new SnapshotTick();

            private SnapshotTick()
            {
            }
        }

        /// <summary>
        /// When an remembering entries and the entity stops without issuing a <see cref="Shard.Passivate"/>, 
        /// we restart it after a back off using this message.
        /// </summary>
        [Serializable]
        internal protected sealed class RestartEntity : IShardCommand
        {
            public readonly EntityId EntityId;

            public RestartEntity(string entityId)
            {
                EntityId = entityId;
            }
        }

        internal protected abstract class StateChange
        {
            public readonly EntityId EntityId;

            protected StateChange(EntityId entityId)
            {
                EntityId = entityId;
            }
        }

        /// <summary>
        /// <see cref="ShardState"/> change for starting an entity in this `Shard`
        /// </summary>
        [Serializable]
        internal protected sealed class EntityStarted : StateChange
        {
            public EntityStarted(string entityId) : base(entityId)
            {
            }
        }

        /// <summary>
        /// <see cref="ShardState"/> change for an entity which has terminated.
        /// </summary>
        [Serializable]
        internal protected sealed class EntityStopped : StateChange
        {
            public EntityStopped(string entityId) : base(entityId)
            {
            }
        }

        [Serializable]
        public sealed class GetCurrentShardState : IShardQuery
        {
            public static readonly GetCurrentShardState Instance = new GetCurrentShardState();

            private GetCurrentShardState()
            {
            }
        }

        [Serializable]
        public sealed class CurrentShardState
        {
            public readonly string ShardId;
            public readonly string[] EntityIds;

            public CurrentShardState(string shardId, string[] entityIds)
            {
                ShardId = shardId;
                EntityIds = entityIds;
            }
        }

        [Serializable]
        public sealed class GetShardStats : IShardQuery
        {
            public static readonly GetShardStats Instance = new GetShardStats();

            private GetShardStats()
            {
            }
        }

        [Serializable]
        public sealed class ShardStats
        {
            public readonly string ShardId;
            public readonly int EntityCount;

            public ShardStats(string shardId, int entityCount)
            {
                ShardId = shardId;
                EntityCount = entityCount;
            }
        }

        #endregion

        /// <summary>
        /// Persistent state of the Shard.
        /// </summary>
        [Serializable]
        internal protected struct ShardState : IClusterShardingSerializable
        {
            public static readonly ShardState Empty = new ShardState(ImmutableHashSet<string>.Empty);
            public readonly IImmutableSet<EntityId> Entries;

            public ShardState(IImmutableSet<EntityId> entries) : this()
            {
                Entries = entries;
            }
        }

        public readonly string TypeName;
        public readonly ShardId ShardId;
        public readonly Actor.Props EntityProps;
        public readonly ClusterShardingSettings Settings;
        public readonly IdExtractor ExtractEntityId;
        public readonly ShardResolver ExtractShardId;
        public readonly object HandOffStopMessage;

        protected IImmutableDictionary<IActorRef, EntityId> IdByRef = ImmutableDictionary<IActorRef, EntityId>.Empty;
        protected IImmutableDictionary<EntityId, IActorRef> RefById = ImmutableDictionary<EntityId, IActorRef>.Empty;
        protected IImmutableSet<IActorRef> Passivating = ImmutableHashSet<IActorRef>.Empty;

        protected IImmutableDictionary<EntityId, IImmutableList<Tuple<Msg, IActorRef>>> MessageBuffers =
            ImmutableDictionary<EntityId, IImmutableList<Tuple<Msg, IActorRef>>>.Empty;

        protected IActorRef HandOffStopper = null;

        protected ShardState State = ShardState.Empty;

        private ILoggingAdapter _log;

        protected Shard(
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            IdExtractor extractEntityId,
            ShardResolver extractShardId,
            object handOffStopMessage)
        {
            TypeName = typeName;
            ShardId = shardId;
            EntityProps = entityProps;
            Settings = settings;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;
        }

        protected ILoggingAdapter Log
        {
            get { return _log ?? (_log = Context.GetLogger()); }
        }

        protected int TotalBufferSize
        {
            get { return MessageBuffers.Aggregate(0, (sum, entity) => sum + entity.Value.Count); }
        }

        #region common shard methods

        protected virtual void Initialized()
        {
            Context.Parent.Tell(new ShardInitialized(ShardId));
        }

        protected virtual void ProcessChange<T>(T evt, Action<T> handler)
        {
            handler(evt);
        }

        protected bool HandleCommand(object message)
        {
            if (message is Terminated) HandleTerminated(((Terminated) message).ActorRef);
            else if (message is PersistentShardCoordinator.ICoordinatorMessage) HandleCoordinatorMessage(message as PersistentShardCoordinator.ICoordinatorMessage);
            else if (message is IShardCommand) HandleShardCommand(message as IShardCommand);
            else if (message is IShardRegionCommand) HandleShardRegionCommand(message as IShardRegionCommand);
            else if (message is IShardQuery) HandleShardRegionQuery(message as IShardQuery);
            else if (ExtractEntityId(message) != null) DeliverMessage(message, Sender);
            else return false;
            return true;
        }

        private void HandleShardRegionQuery(IShardQuery query)
        {
            if(query is GetCurrentShardState) Sender.Tell(new CurrentShardState(ShardId, RefById.Keys.ToArray()));
            else if (query is GetShardStats) Sender.Tell(new ShardStats(ShardId, State.Entries.Count));
        }

        protected virtual void EntityTerminated(IActorRef tref)
        {
            ShardId id;
            IImmutableList<Tuple<Msg, IActorRef>> buffer;
            if (IdByRef.TryGetValue(tref, out id) && MessageBuffers.TryGetValue(id, out buffer) && buffer.Count != 0)
            {
                Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                SendMessageBuffer(new EntityStarted(id));
            }
            else
            {
                ProcessChange(new EntityStopped(id), PassivateCompleted);
            }

            Passivating = Passivating.Remove(tref);
        }

        private void HandleShardCommand(IShardCommand message)
        {
            var restart = message as RestartEntity;
            if (restart != null)
                GetEntity(restart.EntityId);
        }

        private void HandleShardRegionCommand(IShardRegionCommand message)
        {
            var passivate = message as Passivate;
            if (passivate != null)
                Passivate(Sender, passivate.StopMessage);
            else
                Unhandled(message);
        }

        private void HandleCoordinatorMessage(PersistentShardCoordinator.ICoordinatorMessage message)
        {
            var handOff = message as PersistentShardCoordinator.HandOff;
            if (handOff != null)
            {
                if (handOff.Shard == ShardId) HandOff(Sender);
                else Log.Warning("Shard [{0}] can not hand off for another Shard [{1}]", ShardId, handOff.Shard);
            }
            else Unhandled(message);
        }

        private void HandOff(IActorRef replyTo)
        {
            if (HandOffStopper != null) Log.Warning("HandOff shard [{0}] received during existing handOff", ShardId);
            else
            {
                Log.Debug("HandOff shard [{0}]", ShardId);

                if (State.Entries.Count != 0)
                {
                    //  handOffStopper = Some(context.watch(context.actorOf(
                    //      handOffStopperProps(shardId, replyTo, idByRef.keySet, handOffStopMessage))))
                    HandOffStopper = Context.Watch(Context.ActorOf(
                        ShardRegion.HandOffStopper.Props(ShardId, replyTo, IdByRef.Keys, HandOffStopMessage)));

                    //During hand off we only care about watching for termination of the hand off stopper
                    Context.Become(message =>
                    {
                        var terminated = message as Terminated;
                        if (terminated != null)
                        {
                            HandleTerminated(terminated.ActorRef);
                            return true;
                        }
                        return false;
                    });
                }
                else
                {
                    replyTo.Tell(new PersistentShardCoordinator.ShardStopped(ShardId));
                    Context.Stop(Self);
                }
            }
        }

        private void HandleTerminated(IActorRef terminatedRef)
        {
            if (Equals(HandOffStopper, terminatedRef))
                Context.Stop(Self);
            else if (IdByRef.ContainsKey(terminatedRef) && HandOffStopper == null)
                EntityTerminated(terminatedRef);
        }

        private void Passivate(IActorRef entity, object stopMessage)
        {
            ShardId id;
            if (IdByRef.TryGetValue(entity, out id) && !MessageBuffers.ContainsKey(id))
            {
                Log.Debug("Passivating started on entity {0}", id);

                Passivating = Passivating.Add(entity);
                MessageBuffers = MessageBuffers.Add(id, ImmutableList<Tuple<object, IActorRef>>.Empty);

                entity.Tell(stopMessage);
            }
        }

        protected void PassivateCompleted(EntityStopped evt)
        {
            var id = evt.EntityId;
            Log.Debug("Entity stopped [{0}]", id);

            var entity = RefById[id];
            IdByRef = IdByRef.Remove(entity);
            RefById = RefById.Remove(id);

            State = new ShardState(State.Entries.Remove(id));
            MessageBuffers = MessageBuffers.Remove(id);
        }

        protected void SendMessageBuffer(EntityStarted message)
        {
            var id = message.EntityId;

            // Get the buffered messages and remove the buffer
            IImmutableList<Tuple<Msg, IActorRef>> buffer;
            if (MessageBuffers.TryGetValue(id, out buffer)) MessageBuffers = MessageBuffers.Remove(id);

            if (buffer.Count != 0)
            {
                Log.Debug("Sending message buffer for entity [{0}] ([{1}] messages)", id, buffer.Count);

                GetEntity(id);

                // Now there is no deliveryBuffer we can try to redeliver
                // and as the child exists, the message will be directly forwarded
                foreach (var pair in buffer)
                    DeliverMessage(pair.Item1, pair.Item2);
            }
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            var t = ExtractEntityId(message);
            var id = t.Item1;
            var payload = t.Item2;

            if (string.IsNullOrEmpty(id))
            {
                Log.Warning("Id must not be empty, dropping message [{0}]", message.GetType());
                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                IImmutableList<Tuple<Msg, IActorRef>> buffer;
                if (MessageBuffers.TryGetValue(id, out buffer))
                {
                    if (TotalBufferSize >= Settings.TunningParameters.BufferSize)
                    {
                        Log.Warning("Buffer is full, dropping message for entity [{0}]", message.GetType());
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        Log.Debug("Message for entity [{0}] buffered", id);
                        MessageBuffers.SetItem(id, buffer.Add(Tuple.Create(message, sender)));
                    }
                }
                else
                    DeliverTo(id, message, payload, sender);
            }
        }

        protected virtual void DeliverTo(string id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);

            if (Equals(child, ActorRefs.Nobody))
                GetEntity(id).Tell(payload, sender);
            else
                child.Tell(payload, sender);
        }

        protected IActorRef GetEntity(string id)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);
            if (Equals(child, ActorRefs.Nobody))
            {
                Log.Debug("Starting entity [{0}] in shard [{1}]", id, ShardId);

                child = Context.Watch(Context.ActorOf(EntityProps, name));
                IdByRef = IdByRef.SetItem(child, id);
                RefById = RefById.SetItem(id, child);
                State = new ShardState(State.Entries.Add(id));
            }

            return child;
        }

        #endregion
    }
}