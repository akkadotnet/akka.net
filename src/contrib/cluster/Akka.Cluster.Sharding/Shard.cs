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

    public class ShardActor : ActorBase
    {
        private readonly Shard _shardSemantic;

        public ShardActor(
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
        {
            _shardSemantic = new Shard(
                context: Context,
                unhandled: Unhandled,
                typeName: typeName,
                shardId: shardId,
                entityProps: entityProps,
                settings: settings,
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                handOffStopMessage: handOffStopMessage);
        }


        protected override bool Receive(object message)
        {
            return _shardSemantic.HandleCommand(message);
        }
    }


    //TODO: figure out how not to derive from persistent actor for the sake of alternative ddata based impl
    /// <summary>
    /// TBD
    /// </summary>
    public class Shard
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
        public abstract class StateChange : IClusterShardingSerializable
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
            public readonly IImmutableSet<string> EntityIds;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            /// <param name="entityIds">TBD</param>
            public CurrentShardState(string shardId, IImmutableSet<string> entityIds)
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

        public static Props Props(
            string typeName,
            ShardId shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
        {
            if (settings.RememberEntities)
                return Actor.Props.Create(() => new PersistentShardActor(typeName, shardId, entityProps, settings, extractEntityId, extractShardId, handOffStopMessage))
                .WithDeploy(Deploy.Local);
            else
                return Actor.Props.Create(() => new ShardActor(typeName, shardId, entityProps, settings, extractEntityId, extractShardId, handOffStopMessage))
                  .WithDeploy(Deploy.Local);
        }


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

        protected readonly IActorContext _context;
        protected readonly Action<object> _unhandled;

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
        public readonly ExtractEntityId ExtractEntityId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ExtractShardId ExtractShardId;
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
        /// <param name="context">TBD</param>
        /// <param name="unhandled">TBD</param>
        /// <param name="typeName">TBD</param>
        /// <param name="shardId">TBD</param>
        /// <param name="entityProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        public Shard(
            IActorContext context,
            Action<object> unhandled,
            string typeName,
            string shardId,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage)
        {
            _context = context;
            _unhandled = unhandled;
            TypeName = typeName;
            ShardId = shardId;
            EntityProps = entityProps;
            Settings = settings;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;

            Initialized();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log
        {
            get { return _log ?? (_log = _context.GetLogger()); }
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
        public virtual void Initialized()
        {
            _context.Parent.Tell(new ShardInitialized(ShardId));
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
        public bool HandleCommand(object message)
        {
            switch (message)
            {
                case Terminated t:
                    HandleTerminated(t.ActorRef);
                    return true;
                case PersistentShardCoordinator.ICoordinatorMessage cm:
                    HandleCoordinatorMessage(cm);
                    return true;
                case IShardCommand sc:
                    HandleShardCommand(sc);
                    return true;
                case ShardRegion.StartEntity se:
                    HandleStartEntity(se);
                    return true;
                case ShardRegion.StartEntityAck sea:
                    HandleStartEntityAck(sea);
                    return true;
                case IShardRegionCommand src:
                    HandleShardRegionCommand(src);
                    return true;
                case IShardQuery sq:
                    HandleShardRegionQuery(sq);
                    return true;
                case var _ when ExtractEntityId(message) != null:
                    DeliverMessage(message, _context.Sender);
                    return true;
            }
            return false;
        }

        private void HandleShardRegionQuery(IShardQuery query)
        {
            switch (query)
            {
                case GetCurrentShardState _:
                    _context.Sender.Tell(new CurrentShardState(ShardId, RefById.Keys.ToImmutableHashSet()));
                    break;
                case GetShardStats _:
                    _context.Sender.Tell(new ShardStats(ShardId, State.Entries.Count));
                    break;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tref">TBD</param>
        protected virtual void EntityTerminated(IActorRef tref)
        {
            if (IdByRef.TryGetValue(tref, out var id))
            {
                IdByRef = IdByRef.Remove(tref);
                RefById = RefById.Remove(id);

                if (MessageBuffers.TryGetValue(id, out var buffer) && buffer.Count != 0)
                {
                    Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                    SendMessageBuffer(new EntityStarted(id));
                }
                else
                    ProcessChange(new EntityStopped(id), PassivateCompleted);

                Passivating = Passivating.Remove(tref);
            }
        }

        private void HandleShardCommand(IShardCommand message)
        {
            switch (message)
            {
                case RestartEntity restartEntity:
                    GetEntity(restartEntity.EntityId);
                    break;
                case RestartEntities restartEntities:
                    HandleRestartEntities(restartEntities.Entries);
                    break;
            }
        }

        private void HandleStartEntity(ShardRegion.StartEntity start)
        {
            Log.Debug("Got a request from [{0}] to start entity [{1}] in shard [{2}]", _context.Sender, start.EntityId, ShardId);
            GetEntity(start.EntityId);
            _context.Sender.Tell(new ShardRegion.StartEntityAck(start.EntityId, ShardId));
        }

        private void HandleStartEntityAck(ShardRegion.StartEntityAck ack)
        {
            if (ack.ShardId != ShardId && State.Entries.Contains(ack.EntityId))
            {
                Log.Debug("Entity [{0}] previously owned by shard [{1}] started in shard [{2}]", ack.EntityId, ShardId, ack.ShardId);
                ProcessChange(new EntityStopped(ack.EntityId), _ =>
                {
                    State = new ShardState(State.Entries.Remove(ack.EntityId));
                    MessageBuffers = MessageBuffers.Remove(ack.EntityId);
                });
            }
        }

        private void HandleRestartEntities(IImmutableSet<EntityId> ids)
        {
            _context.ActorOf(RememberEntityStarter.Props(_context.Parent, TypeName, ShardId, ids, Settings, _context.Sender));
        }


        private void HandleShardRegionCommand(IShardRegionCommand message)
        {
            if (message is Passivate passivate)
                Passivate(_context.Sender, passivate.StopMessage);
            else
                _unhandled(message);
        }

        private void HandleCoordinatorMessage(PersistentShardCoordinator.ICoordinatorMessage message)
        {
            switch (message)
            {
                case PersistentShardCoordinator.HandOff handOff when handOff.Shard == ShardId:
                    HandOff(_context.Sender);
                    break;
                case PersistentShardCoordinator.HandOff handOff:
                    Log.Warning("Shard [{0}] can not hand off for another Shard [{1}]", ShardId, handOff.Shard);
                    break;
                default:
                    _unhandled(message);
                    break;
            }
        }

        private void HandOff(IActorRef replyTo)
        {
            if (HandOffStopper != null) Log.Warning("HandOff shard [{0}] received during existing handOff", ShardId);
            else
            {
                Log.Debug("HandOff shard [{0}]", ShardId);

                if (State.Entries.Count != 0)
                {
                    HandOffStopper = _context.Watch(_context.ActorOf(
                        ShardRegion.HandOffStopper.Props(ShardId, replyTo, IdByRef.Keys, HandOffStopMessage)));

                    //During hand off we only care about watching for termination of the hand off stopper
                    _context.Become(message =>
                    {
                        if (message is Terminated terminated)
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
                    _context.Stop(_context.Self);
                }
            }
        }

        private void HandleTerminated(IActorRef terminatedRef)
        {
            if (Equals(HandOffStopper, terminatedRef))
                _context.Stop(_context.Self);
            else if (IdByRef.ContainsKey(terminatedRef) && HandOffStopper == null)
                EntityTerminated(terminatedRef);
        }

        private void Passivate(IActorRef entity, object stopMessage)
        {
            if (IdByRef.TryGetValue(entity, out var id))
            {
                if (!MessageBuffers.ContainsKey(id))
                {
                    Log.Debug("Passivating started on entity {0}", id);

                    Passivating = Passivating.Add(entity);
                    MessageBuffers = MessageBuffers.Add(id, ImmutableList<Tuple<object, IActorRef>>.Empty);

                    entity.Tell(stopMessage);
                }
                else
                {
                    Log.Debug("Passivation already in progress for {0}. Not sending stopMessage back to entity.", entity);
                }
            }
            else
            {
                Log.Debug("Unknown entity {0}. Not sending stopMessage back to entity.", entity);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        protected void PassivateCompleted(EntityStopped evt)
        {
            Log.Debug("Entity stopped after passivation [{0}]", evt.EntityId);
            State = new ShardState(State.Entries.Remove(evt.EntityId));
            MessageBuffers = MessageBuffers.Remove(evt.EntityId);
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
            {
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
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            var t = ExtractEntityId(message);
            var id = t.Item1;
            var payload = t.Item2;

            if (string.IsNullOrEmpty(id))
            {
                Log.Warning("Id must not be empty, dropping message [{0}]", message.GetType());
                _context.System.DeadLetters.Tell(message);
            }
            else
            {
                if (MessageBuffers.TryGetValue(id, out var buffer))
                {
                    if (TotalBufferSize >= Settings.TunningParameters.BufferSize)
                    {
                        Log.Warning("Buffer is full, dropping message for entity [{0}]", id);
                        _context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        Log.Debug("Message for entity [{0}] buffered", id);
                        MessageBuffers = MessageBuffers.SetItem(id, buffer.Add(Tuple.Create(message, sender)));
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
            var child = _context.Child(name);

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
            var child = _context.Child(name);
            if (Equals(child, ActorRefs.Nobody))
            {
                Log.Debug("Starting entity [{0}] in shard [{1}]", id, ShardId);

                child = _context.Watch(_context.ActorOf(EntityProps, name));
                IdByRef = IdByRef.SetItem(child, id);
                RefById = RefById.SetItem(id, child);
                State = new ShardState(State.Entries.Add(id));
            }

            return child;
        }

        #endregion
    }


    class RememberEntityStarter : ActorBase
    {
        private class Tick : INoSerializationVerificationNeeded
        {
            public static readonly Tick Instance = new Tick();
            private Tick()
            {
            }
        }


        public static Props Props(
          IActorRef region,
          string typeName,
          ShardId shardId,
          IImmutableSet<EntityId> ids,
          ClusterShardingSettings settings,
          IActorRef requestor)
        {
            return Actor.Props.Create(() => new RememberEntityStarter(region, typeName, shardId, ids, settings, requestor));
        }


        private readonly IActorRef _region;
        private readonly string _typeName;
        private readonly ShardId _shardId;
        private readonly IImmutableSet<EntityId> _ids;
        private readonly ClusterShardingSettings _settings;
        private readonly IActorRef _requestor;

        private IImmutableSet<EntityId> _waitingForAck;
        private readonly ICancelable _tickTask;


        public RememberEntityStarter(
            IActorRef region,
            string typeName,
            ShardId shardId,
            IImmutableSet<EntityId> ids,
            ClusterShardingSettings settings,
            IActorRef requestor
            )
        {
            _region = region;
            _typeName = typeName;
            _shardId = shardId;
            _ids = ids;
            _settings = settings;
            _requestor = requestor;

            _waitingForAck = ids;

            SendStart(ids);

            var resendInterval = settings.TunningParameters.RetryInterval;
            _tickTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(resendInterval, resendInterval, Self, Tick.Instance, ActorRefs.NoSender);
        }



        private void SendStart(IImmutableSet<EntityId> ids)
        {
            foreach (var id in ids)
            {
                _region.Tell(new ShardRegion.StartEntity(id));
            }
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ShardRegion.StartEntityAck ack:
                    _waitingForAck = _waitingForAck.Remove(ack.EntityId);
                    // inform whoever requested the start that it happened
                    _requestor.Tell(ack);
                    if (_waitingForAck.Count == 0) Context.Stop(Self);
                    return true;
                case Tick _:
                    SendStart(_waitingForAck);
                    return true;
            }
            return false;
        }

        protected override void PostStop()
        {
            _tickTask.Cancel();
        }
    }
}