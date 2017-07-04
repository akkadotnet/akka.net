//-----------------------------------------------------------------------
// <copyright file="Shard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntityId = String;
    using Msg = Object;

    //TODO: figure out how not to derive from persistent actor for the sake of alternative ddata based impl
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class Shard : PersistentActor
    {
        #region messages

        /// <summary>
        /// TBD
        /// </summary>
        protected interface IShardCommand { }
        /// <summary>
        /// TBD
        /// </summary>
        public interface IShardQuery { }

        /// <summary>
        /// When a <see cref="StateChange"/> fails to write to the journal, we will retry it after a back off.
        /// </summary>
        [Serializable]
        protected internal class RetryPersistence : IShardCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly StateChange Payload;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="payload">TBD</param>
            public RetryPersistence(StateChange payload)
            {
                Payload = payload;
            }
        }

        /// <summary>
        /// The Snapshot tick for the shards.
        /// </summary>
        [Serializable]
        protected internal sealed class SnapshotTick : IShardCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
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
        protected internal sealed class RestartEntity : IShardCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly EntityId EntityId;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entityId">TBD</param>
            public RestartEntity(string entityId)
            {
                EntityId = entityId;
            }
        }

        /// <summary>
        /// When initialising a shard with remember entities enabled the following message is used to restart
        /// batches of entity actors at a time.
        /// </summary>
        [Serializable]
        protected internal sealed class RestartEntities : IShardCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<EntityId> Entries;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entries">TBD</param>
            public RestartEntities(IImmutableSet<EntityId> entries)
            {
                Entries = entries;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class StateChange: IClusterShardingSerializable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly EntityId EntityId;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entityId">TBD</param>
            protected StateChange(EntityId entityId)
            {
                EntityId = entityId;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as StateChange;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return EntityId.Equals(other.EntityId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return EntityId?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="ShardState"/> change for starting an entity in this `Shard`
        /// </summary>
        [Serializable]
        public sealed class EntityStarted : StateChange
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entityId">TBD</param>
            public EntityStarted(string entityId) : base(entityId)
            {
            }
        }

        /// <summary>
        /// <see cref="ShardState"/> change for an entity which has terminated.
        /// </summary>
        [Serializable]
        public sealed class EntityStopped : StateChange
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entityId">TBD</param>
            public EntityStopped(string entityId) : base(entityId)
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class GetCurrentShardState : IShardQuery
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetCurrentShardState Instance = new GetCurrentShardState();

            private GetCurrentShardState()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class CurrentShardState
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string ShardId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string[] EntityIds;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            /// <param name="entityIds">TBD</param>
            public CurrentShardState(string shardId, string[] entityIds)
            {
                ShardId = shardId;
                EntityIds = entityIds;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class GetShardStats : IShardQuery
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetShardStats Instance = new GetShardStats();

            private GetShardStats()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardStats
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string ShardId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int EntityCount;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            /// <param name="entityCount">TBD</param>
            public ShardStats(string shardId, int entityCount)
            {
                ShardId = shardId;
                EntityCount = entityCount;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardStats;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardId.Equals(other.ShardId)
                    && EntityCount.Equals(other.EntityCount);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = ShardId?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ EntityCount;
                    return hashCode;
                }
            }

            #endregion
        }

        #endregion

        /// <summary>
        /// Persistent state of the Shard.
        /// </summary>
        [Serializable]
        public class ShardState : IClusterShardingSerializable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ShardState Empty = new ShardState(ImmutableHashSet<string>.Empty);

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<EntityId> Entries;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="entries">TBD</param>
            public ShardState(IImmutableSet<EntityId> entries)
            {
                Entries = entries;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardState;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Entries.SequenceEqual(other.Entries);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 13;

                    foreach (var v in Entries)
                    {
                        hashCode = (hashCode * 397) ^ (v?.GetHashCode() ?? 0);
                    }

                    return hashCode;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string TypeName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ShardId ShardId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Actor.Props EntityProps;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ClusterShardingSettings Settings;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IdExtractor ExtractEntityId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ShardResolver ExtractShardId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object HandOffStopMessage;

        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<IActorRef, EntityId> IdByRef = ImmutableDictionary<IActorRef, EntityId>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<EntityId, IActorRef> RefById = ImmutableDictionary<EntityId, IActorRef>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableSet<IActorRef> Passivating = ImmutableHashSet<IActorRef>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<EntityId, IImmutableList<Tuple<Msg, IActorRef>>> MessageBuffers =
            ImmutableDictionary<EntityId, IImmutableList<Tuple<Msg, IActorRef>>>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        protected IActorRef HandOffStopper = null;

        /// <summary>
        /// TBD
        /// </summary>
        protected ShardState State = ShardState.Empty;

        private ILoggingAdapter _log;

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

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log
        {
            get { return _log ?? (_log = Context.GetLogger()); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected int TotalBufferSize
        {
            get { return MessageBuffers.Aggregate(0, (sum, entity) => sum + entity.Value.Count); }
        }

        #region common shard methods

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual void Initialized()
        {
            Context.Parent.Tell(new ShardInitialized(ShardId));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>>
        /// <param name="evt">TBD</param>
        /// <param name="handler">TBD</param>
        protected virtual void ProcessChange<T>(T evt, Action<T> handler)
        {
            handler(evt);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tref">TBD</param>
        protected virtual void EntityTerminated(IActorRef tref)
        {
            if (IdByRef.TryGetValue(tref, out var id) && MessageBuffers.TryGetValue(id, out var buffer) && buffer.Count != 0)
            {
                Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                SendMessageBuffer(new EntityStarted(id));
            }
            else
                ProcessChange(new EntityStopped(id), PassivateCompleted);

            Passivating = Passivating.Remove(tref);
        }

        private void HandleShardCommand(IShardCommand message)
        {
            message.Match()
                .With<RestartEntity>(restartEntity => GetEntity(restartEntity.EntityId))
                .With<RestartEntities>(restartEntities => restartEntities.Entries.ForEach(entityId => GetEntity(entityId)));
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
            if (IdByRef.TryGetValue(entity, out var id) && !MessageBuffers.ContainsKey(id))
            {
                Log.Debug("Passivating started on entity {0}", id);

                Passivating = Passivating.Add(entity);
                MessageBuffers = MessageBuffers.Add(id, ImmutableList<Tuple<object, IActorRef>>.Empty);

                entity.Tell(stopMessage);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected void SendMessageBuffer(EntityStarted message)
        {
            var id = message.EntityId;

            // Get the buffered messages and remove the buffer
            if (MessageBuffers.TryGetValue(id, out var buffer))
                MessageBuffers = MessageBuffers.Remove(id);

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
                if (MessageBuffers.TryGetValue(id, out var buffer))
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="payload">TBD</param>
        /// <param name="sender">TBD</param>
        protected virtual void DeliverTo(string id, object message, object payload, IActorRef sender)
        {
            var name = Uri.EscapeDataString(id);
            var child = Context.Child(name);

            if (Equals(child, ActorRefs.Nobody))
                GetEntity(id).Tell(payload, sender);
            else
                child.Tell(payload, sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
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