//-----------------------------------------------------------------------
// <copyright file="Shard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster.Sharding.Internal;
using Akka.Coordination;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding
{
    using static Akka.Cluster.Sharding.ShardCoordinator;
    using EntityId = String;
    using ShardId = String;

    /// <summary>
    /// INTERNAL API
    /// 
    /// This actor creates children entity actors on demand that it is told to be
    /// responsible for.
    /// </summary>
    [InternalStableApi]
    internal sealed class Shard : ActorBase, IWithTimers, IWithUnboundedStash
    {
        #region messages

        /// <summary>
        /// A Shard command
        /// </summary>
        public interface IRememberEntityCommand { }

        /// <summary>
        /// When remembering entities and the entity stops without issuing a `Passivate`, we
        /// restart it after a back off using this message.
        /// </summary>
        public sealed class RestartTerminatedEntity : IRememberEntityCommand, IEquatable<RestartTerminatedEntity>
        {
            public RestartTerminatedEntity(EntityId entity)
            {
                Entity = entity;
            }

            public EntityId Entity { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as RestartTerminatedEntity);
            }

            public bool Equals(RestartTerminatedEntity other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Entity.Equals(other.Entity);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Entity.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"RestartTerminatedEntity({Entity})";

            #endregion
        }

        /// <summary>
        /// If the shard id extractor is changed, remembered entities will start in a different shard
        /// and this message is sent to the shard to not leak `entityId -> RememberedButNotStarted` entries
        /// </summary>
        public sealed class EntitiesMovedToOtherShard : IRememberEntityCommand, IEquatable<EntitiesMovedToOtherShard>
        {
            public EntitiesMovedToOtherShard(IImmutableSet<ShardId> ids)
            {
                Ids = ids;
            }

            public IImmutableSet<ShardId> Ids { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as EntitiesMovedToOtherShard);
            }

            public bool Equals(EntitiesMovedToOtherShard other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Ids.SetEquals(other.Ids);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in Ids)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"EntitiesMovedToOtherShard({string.Join(", ", Ids)})";

            #endregion
        }

        /// <summary>
        /// A query for information about the shard
        /// </summary>
        public interface IShardQuery { }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class GetCurrentShardState : IShardQuery, IClusterShardingSerializable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetCurrentShardState Instance = new();

            private GetCurrentShardState()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class CurrentShardState : IClusterShardingSerializable, IEquatable<CurrentShardState>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId ShardId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<EntityId> EntityIds;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            /// <param name="entityIds">TBD</param>
            public CurrentShardState(ShardId shardId, IImmutableSet<EntityId> entityIds)
            {
                ShardId = shardId;
                EntityIds = entityIds;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as CurrentShardState);
            }

            public bool Equals(CurrentShardState other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardId.Equals(other.ShardId)
                    && EntityIds.SetEquals(other.EntityIds);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = ShardId.GetHashCode();
                    foreach (var s in EntityIds)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"CurrentShardState(shardId:{ShardId}, entityIds:{string.Join(", ", EntityIds)})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class GetShardStats : IShardQuery, IClusterShardingSerializable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetShardStats Instance = new();

            private GetShardStats()
            {
            }

            /// <inheritdoc/>
            public override string ToString() => "GetShardStats";
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardStats : IClusterShardingSerializable, IEquatable<ShardStats>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId ShardId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int EntityCount;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            /// <param name="entityCount">TBD</param>
            public ShardStats(ShardId shardId, int entityCount)
            {
                ShardId = shardId;
                EntityCount = entityCount;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardStats);
            }

            public bool Equals(ShardStats other)
            {
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
                    int hashCode = ShardId.GetHashCode();
                    hashCode = (hashCode * 397) ^ EntityCount.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardStats(shardId:{ShardId}, entityCount:{EntityCount})";

            #endregion
        }

        [Serializable]
        public sealed class LeaseAcquireResult : IDeadLetterSuppression, INoSerializationVerificationNeeded
        {
            public readonly bool Acquired;

            public readonly Exception Reason;

            public LeaseAcquireResult(bool acquired, Exception reason)
            {
                Acquired = acquired;
                Reason = reason;
            }
        }

        [Serializable]
        public sealed class LeaseLost : IDeadLetterSuppression, INoSerializationVerificationNeeded
        {
            public readonly Exception Reason;

            public LeaseLost(Exception reason)
            {
                Reason = reason;
            }
        }

        [Serializable]
        public sealed class LeaseRetry : IDeadLetterSuppression, INoSerializationVerificationNeeded
        {
            public static readonly LeaseRetry Instance = new();
            private LeaseRetry() { }
        }


        private const string LeaseRetryTimer = "lease-retry";

        public static Props Props(
            string typeName,
            ShardId shardId,
            Func<string, Props> entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage,
             IRememberEntitiesProvider rememberEntitiesProvider)
        {
            return Actor.Props.Create(() => new Shard(
                typeName,
                shardId,
                entityProps,
                settings,
                extractEntityId,
                extractShardId,
                handOffStopMessage,
                rememberEntitiesProvider)).WithDeploy(Deploy.Local);
        }

        [Serializable]
        public sealed class PassivateIdleTick : INoSerializationVerificationNeeded
        {
            public static readonly PassivateIdleTick Instance = new();
            private PassivateIdleTick() { }
        }

        private sealed class EntityTerminated
        {
            public EntityTerminated(IActorRef @ref)
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; }
        }

        private sealed class RememberedEntityIds
        {
            public RememberedEntityIds(IImmutableSet<EntityId> ids)
            {
                Ids = ids;
            }

            public IImmutableSet<string> Ids { get; }
        }

        private sealed class RememberEntityStoreCrashed
        {
            public RememberEntityStoreCrashed(IActorRef store)
            {
                Store = store;
            }

            public IActorRef Store { get; }
        }

        private const string RememberEntityTimeoutKey = "RememberEntityTimeout";

        internal sealed class RememberEntityTimeout
        {
            public RememberEntityTimeout(RememberEntitiesShardStore.ICommand operation)
            {
                Operation = operation;
            }

            public RememberEntitiesShardStore.ICommand Operation { get; }
        }


        #endregion

        //
        // State machine for an entity:
        //                                                                       Started on another shard bc. shard id extractor changed (we need to store that)
        //                                                                +------------------------------------------------------------------+
        //                                                                |                                                                  |
        //              Entity id remembered on shard start     +-------------------------+    StartEntity or early message for entity       |
        //                   +--------------------------------->| RememberedButNotCreated |------------------------------+                   |
        //                   |                                  +-------------------------+                              |                   |
        //                   |                                                                                           |                   |
        //                   |                                                                                           |                   |
        //                   |   Remember entities                                                                       |                   |
        //                   |   message or StartEntity         +-------------------+   start stored and entity started  |                   |
        //                   |        +-----------------------> | RememberingStart  |-------------+                      v                   |
        // No state for id   |        |                         +-------------------+             |               +------------+             |
        //      +---+        |        |                                                           +-------------> |   Active   |             |
        //      |   |--------|--------+-----------------------------------------------------------+               +------------+             |
        //      +---+                 |   Non remember entities message or StartEntity                                   |                   |
        //        ^                   |                                                                                  |                   |
        //        |                   |                                                             entity terminated    |                   |
        //        |                   |      restart after backoff                                  without passivation  |    passivation    |
        //        |                   |      or message for entity    +-------------------+    remember ent.             |     initiated     \          +-------------+
        //        |                   +<------------------------------| WaitingForRestart |<---+-------------+-----------+--------------------|-------> | Passivating |
        //        |                   |                               +-------------------+    |             |                               /          +-------------+
        //        |                   |                                                        |             | remember entities             |     entity     |
        //        |                   |                                                        |             | not used                      |     terminated +--------------+
        //        |                   |                                                        |             |                               |                v              |
        //        |                   |            There were buffered messages for entity     |             |                               |      +-------------------+    |
        //        |                   +<-------------------------------------------------------+             |                               +----> |   RememberingStop |    | remember entities
        //        |                                                                                          |                                      +-------------------+    | not used
        //        |                                                                                          |                                                |              |
        //        |                                                                                          v                                                |              |
        //        +------------------------------------------------------------------------------------------+------------------------------------------------+<-------------+
        //                       stop stored/passivation complete
        //
        internal abstract class EntityState
        {
            public abstract EntityState Transition(EntityState newState, Entities entities);

            protected EntityState InvalidTransition(EntityState to, Entities entities)
            {
                var exception = new ArgumentException($"Transition from {this} to {to} not allowed, remember entities: {entities.RememberingEntities}");
                if (entities.FailOnIllegalTransition)
                {
                    // crash shard
                    throw exception;
                }
                else
                {
                    // log and ignore
                    entities.Log.Error(exception, "Ignoring illegal state transition in shard");
                    return to;
                }
            }

            public override string ToString()
            {
                return GetType().Name;
            }
        }

        /// <summary>
        /// Empty state rather than using optionals,
        /// is never really kept track of but used to verify state transitions
        /// and as return value instead of null
        /// </summary>
        internal sealed class NoState : EntityState
        {
            public static readonly NoState Instance = new();

            private NoState()
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case RememberedButNotCreated _ when entities.RememberingEntities:
                        return RememberedButNotCreated.Instance;
                    case RememberingStart remembering:
                        return remembering; // we go via this state even if not really remembering
                    case Active active when !entities.RememberingEntities:
                        return active;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            /// <inheritdoc/>
            public override string ToString() => "NoState";
        }

        /// <summary>
        /// In this state we know the entity has been stored in the remember sore but
        /// it hasn't been created yet. E.g. on restart when first getting all the
        /// remembered entity ids.
        /// </summary>
        internal sealed class RememberedButNotCreated : EntityState
        {
            public static readonly RememberedButNotCreated Instance = new();

            private RememberedButNotCreated()
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case Active active:
                        return active; // started on this shard
                    case RememberingStop _:
                        return RememberingStop.Instance; // started on other shard
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            /// <inheritdoc/>
            public override string ToString() => "RememberedButNotCreated";
        }

        /// <summary>
        /// When remember entities is enabled an entity is in this state while
        /// its existence is being recorded in the remember entities store, or while the stop is queued up
        /// to be stored in the next batch.
        /// </summary>
        internal sealed class RememberingStart : EntityState, IEquatable<RememberingStart>
        {
            private static readonly RememberingStart Empty = new(ImmutableHashSet<IActorRef>.Empty);

            public static RememberingStart Create(IActorRef ackTo)
            {
                if (ackTo == null)
                    return Empty;
                return new RememberingStart(ImmutableHashSet.Create(ackTo));
            }

            public RememberingStart(IImmutableSet<IActorRef> ackTo)
            {
                AckTo = ackTo;
            }

            public IImmutableSet<IActorRef> AckTo { get; }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case Active active:
                        return active;
                    case RememberingStart r:
                        if (AckTo.Count == 0)
                        {
                            if (r.AckTo.Count == 0)
                                return Empty;
                            else
                                return newState;
                        }
                        else
                        {
                            if (r.AckTo.Count == 0)
                                return this;
                            else
                                return new RememberingStart(AckTo.Union(r.AckTo));
                        }
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as RememberingStart);
            }

            public bool Equals(RememberingStart other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return AckTo.SetEquals(other.AckTo);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in AckTo)
                        hashCode = (hashCode * 397) ^ s.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"RememberingStart({string.Join(", ", AckTo)})";

            #endregion
        }

        /// <summary>
        /// When remember entities is enabled an entity is in this state while
        /// its stop is being recorded in the remember entities store, or while the stop is queued up
        /// to be stored in the next batch.
        /// </summary>
        internal sealed class RememberingStop : EntityState
        {
            public static readonly RememberingStop Instance = new();

            private RememberingStop()
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case NoState _:
                        return NoState.Instance;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            /// <inheritdoc/>
            public override string ToString() => "RememberingStop";
        }

        internal abstract class WithRef : EntityState, IEquatable<WithRef>
        {
            public WithRef(IActorRef @ref)
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as WithRef);
            }

            public bool Equals(WithRef other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Equals(Ref, other.Ref);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Ref?.GetHashCode() ?? 0;
            }

            /// <inheritdoc/>
            public override string ToString() => $"{GetType().Name}(Ref)";

            #endregion
        }

        internal sealed class Active : WithRef
        {
            public Active(IActorRef @ref)
                : base(@ref)
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case Passivating passivating:
                        return passivating;
                    case WaitingForRestart _:
                        return WaitingForRestart.Instance;
                    case NoState _ when !entities.RememberingEntities:
                        return NoState.Instance;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }
        }

        internal sealed class Passivating : WithRef
        {
            public Passivating(IActorRef @ref)
                : base(@ref)
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case RememberingStop _:
                        return RememberingStop.Instance;
                    case NoState _ when !entities.RememberingEntities:
                        return NoState.Instance;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }
        }

        internal sealed class WaitingForRestart : EntityState
        {
            public static readonly WaitingForRestart Instance = new();

            private WaitingForRestart()
            {
            }

            public override EntityState Transition(EntityState newState, Entities entities)
            {
                switch (newState)
                {
                    case RememberingStart remembering:
                        return remembering;
                    case Active active:
                        return active;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            /// <inheritdoc/>
            public override string ToString() => "WaitingForRestart";
        }

        internal sealed class Entities
        {
            private readonly Dictionary<EntityId, EntityState> _entities = new();
            // needed to look up entity by ref when a Passivating is received
            private readonly Dictionary<IActorRef, EntityId> _byRef = new();
            // optimization to not have to go through all entities to find batched writes
            private readonly HashSet<EntityId> _remembering = new();


            public Entities(
                ILoggingAdapter log,
                bool rememberingEntities,
                bool verboseDebug,
                bool failOnIllegalTransition)
            {
                Log = log;
                RememberingEntities = rememberingEntities;
                VerboseDebug = verboseDebug;
                FailOnIllegalTransition = failOnIllegalTransition;
            }

            public ILoggingAdapter Log { get; }

            public bool RememberingEntities { get; }

            public bool VerboseDebug { get; }

            public bool FailOnIllegalTransition { get; }

            public void AlreadyRemembered(IImmutableSet<EntityId> set)
            {
                foreach (var entityId in set)
                {
                    var state = EntityState(entityId).Transition(RememberedButNotCreated.Instance, this);
                    _entities[entityId] = state;
                }
            }

            public void RememberingStart(EntityId entityId, IActorRef ackTo)
            {
                var newState = Shard.RememberingStart.Create(ackTo);
                var state = EntityState(entityId).Transition(newState, this);
                _entities[entityId] = state;
                if (RememberingEntities)
                    _remembering.Add(entityId);
            }

            public void RememberingStop(EntityId entityId)
            {
                var state = EntityState(entityId);
                RemoveRefIfThereIsOne(state);
                _entities[entityId] = state.Transition(Shard.RememberingStop.Instance, this);
                if (RememberingEntities)
                    _remembering.Add(entityId);
            }

            public void WaitingForRestart(EntityId id)
            {
                EntityState state = EntityState(id);
                if (state is WithRef wr)
                    _byRef.Remove(wr.Ref);

                _entities[id] = state.Transition(Shard.WaitingForRestart.Instance, this);
            }

            public void RemoveEntity(EntityId entityId)
            {
                var state = EntityState(entityId);
                // just verify transition
                state.Transition(NoState.Instance, this);
                RemoveRefIfThereIsOne(state);
                _entities.Remove(entityId);
                if (RememberingEntities)
                    _remembering.Remove(entityId);
            }

            public void AddEntity(EntityId entityId, IActorRef @ref)
            {
                var state = EntityState(entityId).Transition(new Active(@ref), this);
                _entities[entityId] = state;
                _byRef[@ref] = entityId;
                if (RememberingEntities)
                    _remembering.Remove(entityId);
            }

            public IActorRef Entity(EntityId entityId)
            {
                if (_entities.TryGetValue(entityId, out var state))
                {
                    if (state is WithRef wr)
                        return wr.Ref;
                }
                return null;
            }

            public EntityState EntityState(EntityId id)
            {
                if (_entities.TryGetValue(id, out var state))
                    return state;
                return NoState.Instance;
            }

            public EntityId EntityId(IActorRef @ref)
            {
                if (_byRef.TryGetValue(@ref, out var entityId))
                    return entityId;
                return null;
            }

            public bool IsPassivating(EntityId id)
            {
                return EntityState(id) is Passivating;
            }

            public void EntityPassivating(EntityId entityId)
            {
                if (VerboseDebug)
                    Log.Debug("[{0}] passivating", entityId);
                var oldState = EntityState(entityId);
                if (oldState is WithRef wf)
                {
                    var state = wf.Transition(new Passivating(wf.Ref), this);
                    _entities[entityId] = state;
                }
                else
                {
                    throw new IllegalStateException($"Tried to passivate entity without an actor ref {entityId}. Current state {oldState}");
                }
            }

            private void RemoveRefIfThereIsOne(EntityState state)
            {
                if (state is WithRef wr)
                    _byRef.Remove(wr.Ref);
            }

            // only called once during handoff
            public IImmutableSet<IActorRef> ActiveEntities => _byRef.Keys.ToImmutableHashSet();

            public int NrActiveEntities => _byRef.Count;

            // only called for getting shard stats
            public IImmutableSet<EntityId> ActiveEntityIds => _byRef.Values.ToImmutableHashSet();

            public (IImmutableDictionary<EntityId, RememberingStart> Start, IImmutableSet<EntityId> Stop) PendingRememberEntities
            {
                get
                {
                    if (_remembering.Count == 0)
                    {
                        return (ImmutableDictionary<EntityId, RememberingStart>.Empty, ImmutableHashSet<EntityId>.Empty);
                    }
                    else
                    {
                        var starts = ImmutableDictionary.CreateBuilder<EntityId, RememberingStart>();
                        var stops = ImmutableHashSet.CreateBuilder<EntityId>();
                        foreach (var entityId in _remembering)
                        {
                            switch (EntityState(entityId))
                            {
                                case RememberingStart r:
                                    starts.Add(entityId, r);
                                    break;
                                case RememberingStop _:
                                    stops.Add(entityId);
                                    break;
                                case var state:
                                    throw new IllegalStateException($"{entityId} was in the remembering set but has state {state}");
                            }
                        }
                        return (starts.ToImmutable(), stops.ToImmutable());
                    }
                }
            }

            public bool PendingRememberedEntitiesExist => _remembering.Count > 0;

            public bool EntityIdExists(EntityId id) => _entities.ContainsKey(id);

            public int Count => _entities.Count;

            public override string ToString()
            {
                return string.Join(", ", _entities.Select(e => $"({e.Key}: {e.Value})"));
            }
        }


        private readonly string _typeName;
        private readonly string _shardId;
        private readonly Func<string, Props> _entityProps;
        private readonly ClusterShardingSettings _settings;
        private readonly ExtractEntityId _extractEntityId;
        private readonly ExtractShardId _extractShardId;
        private readonly object _handOffStopMessage;

        private readonly bool _verboseDebug;
        private readonly IActorRef _rememberEntitiesStore;
        private readonly bool _rememberEntities;
        private readonly Entities _entities;
        private readonly Dictionary<EntityId, DateTime> _lastMessageTimestamp = new();
        private readonly MessageBufferMap<EntityId> _messageBuffers = new();

        private IActorRef _handOffStopper;
        private readonly ICancelable _passivateIdleTask;
        private readonly Lease _lease;
        private readonly TimeSpan _leaseRetryInterval = TimeSpan.FromSeconds(5); // won't be used

        public ILoggingAdapter Log { get; } = Context.GetLogger();
        public IStash Stash { get; set; }
        public ITimerScheduler Timers { get; set; }

        public Shard(
            string typeName,
            string shardId,
            Func<string, Props> entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage,
            IRememberEntitiesProvider rememberEntitiesProvider)
        {
            _typeName = typeName;
            _shardId = shardId;
            _entityProps = entityProps;
            _settings = settings;
            _extractEntityId = extractEntityId;
            _extractShardId = extractShardId;
            _handOffStopMessage = handOffStopMessage;

            _verboseDebug = Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging");

            if (rememberEntitiesProvider != null)
            {
                var store = Context.ActorOf(rememberEntitiesProvider.ShardStoreProps(shardId).WithDeploy(Deploy.Local), "RememberEntitiesStore");
                Context.WatchWith(store, new RememberEntityStoreCrashed(store));
                _rememberEntitiesStore = store;
            }

            _rememberEntities = rememberEntitiesProvider != null;

            //private val flightRecorder = ShardingFlightRecorder(context.system)

            var failOnInvalidStateTransition =
                Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.fail-on-invalid-entity-state-transition");
            _entities = new Entities(Log, settings.RememberEntities, _verboseDebug, failOnInvalidStateTransition);

            var idleInterval = TimeSpan.FromTicks(_settings.PassivateIdleEntityAfter.Ticks / 2);
            _passivateIdleTask = _settings.ShouldPassivateIdleEntities
                ? Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(idleInterval, idleInterval, Self, PassivateIdleTick.Instance, Self)
                : null;

            if (settings.LeaseSettings != null)
            {
                _lease = LeaseProvider.Get(Context.System).GetLease(
                    $"{Context.System.Name}-shard-{typeName}-{shardId}",
                    settings.LeaseSettings.LeaseImplementation,
                    Cluster.Get(Context.System).SelfAddress.HostPort());

                _leaseRetryInterval = settings.LeaseSettings.LeaseRetryInterval;
            }
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return base.SupervisorStrategy();
        }

        protected override bool Receive(object message)
        {
            throw new IllegalStateException("Default receive never expected to actually be used");
        }

        protected override void PreStart()
        {
            AcquireLeaseIfNeeded();
        }

        // ===== lease handling initialization =====

        /// <summary>
        /// Will call onLeaseAcquired when completed, also when lease isn't used
        /// </summary>
        private void AcquireLeaseIfNeeded()
        {
            if (_lease != null)
            {
                TryGetLease(_lease);
                Context.Become(AwaitingLease);
            }
            else
                TryLoadRememberedEntities();
        }

        private void ReleaseLeaseIfNeeded()
        {
            if (_lease != null)
            {
                _lease.Release().ContinueWith(r =>
                {
                    if (r.IsFaulted || r.IsCanceled)
                    {
                        Log.Error(r.Exception,
                            "{0}: Failed to release lease of shardId [{1}]. Shard may not be able to run on another node until lease timeout occurs.", _typeName, _shardId);
                    }
                    else if (r.Result)
                    {
                        Log.Info("{0}: Lease of shardId [{1}] released.", _typeName, _shardId);
                    }
                    else
                    {
                        Log.Error(
                            "{0}: Failed to release lease of shardId [{1}]. Shard may not be able to run on another node until lease timeout occurs.", _typeName, _shardId);
                    }
                });
            }
        }


        /// <summary>
        /// Don't send back ShardInitialized so that messages are buffered in the ShardRegion
        /// while awaiting the lease
        /// </summary>
        /// <returns></returns>
        private bool AwaitingLease(object message)
        {
            switch (message)
            {
                case LeaseAcquireResult lar when lar.Acquired:
                    Log.Debug("{0}: Lease acquired", _typeName);
                    TryLoadRememberedEntities();
                    return true;

                case LeaseAcquireResult lar when !lar.Acquired && lar.Reason == null:
                    Log.Error("{0}: Failed to get lease for shard id [{1}]. Retry in {2}",
                        _typeName, _shardId, _leaseRetryInterval);
                    Timers.StartSingleTimer(LeaseRetryTimer, Shard.LeaseRetry.Instance, _leaseRetryInterval);
                    return true;

                case LeaseAcquireResult lar when !lar.Acquired && lar.Reason != null:
                    Log.Error(lar.Reason, "{0}: Failed to get lease for shard id [{1}]. Retry in {2}",
                        _typeName, _shardId, _leaseRetryInterval);
                    Timers.StartSingleTimer(LeaseRetryTimer, Shard.LeaseRetry.Instance, _leaseRetryInterval);
                    return true;

                case LeaseRetry _:
                    TryGetLease(_lease);
                    return true;

                case LeaseLost ll:
                    ReceiveLeaseLost(ll);
                    return true;
            }
            if (_verboseDebug)
                Log.Debug("{0}: Got msg of type [{1}] from [{2}] while waiting for lease, stashing",
                    _typeName,
                    message.GetType().Name,
                    Sender);
            Stash.Stash();
            return true;
        }

        private void TryGetLease(Lease lease)
        {
            Log.Info("{0}: Acquiring lease {1}", _typeName, lease.Settings);

            var self = Self;
            lease.Acquire(reason =>
            {
                self.Tell(new LeaseLost(reason));
            }).ContinueWith(r =>
            {
                if (r.IsFaulted || r.IsCanceled)
                    return new LeaseAcquireResult(false, r.Exception);
                return new LeaseAcquireResult(r.Result, null);
            }).PipeTo(Self);
        }

        // ===== remember entities initialization =====

        private void TryLoadRememberedEntities()
        {
            if (_rememberEntitiesStore != null)
            {
                Log.Debug("{0}: Waiting for load of entity ids using [{1}] to complete", _typeName, _rememberEntitiesStore);
                _rememberEntitiesStore.Tell(RememberEntitiesShardStore.GetEntities.Instance);
                Timers.StartSingleTimer(
                    RememberEntityTimeoutKey,
                    new RememberEntityTimeout(RememberEntitiesShardStore.GetEntities.Instance),
                    _settings.TuningParameters.UpdatingStateTimeout);
                Context.Become(AwaitingRememberedEntities);
            }
            else
                ShardInitialized();
        }

        private bool AwaitingRememberedEntities(object message)
        {
            switch (message)
            {
                case RememberEntitiesShardStore.RememberedEntities re:
                    Timers.Cancel(RememberEntityTimeoutKey);
                    OnEntitiesRemembered(re.Entities);
                    return true;
                case RememberEntityTimeout _:
                    LoadingEntityIdsFailed();
                    return true;
            }
            if (_verboseDebug)
                Log.Debug("{0}: Got msg of type [{1}] from [{2}] while waiting for remember entities, stashing",
                    _typeName,
                    message.GetType().Name,
                    Sender);
            Stash.Stash();
            return true;
        }


        private void LoadingEntityIdsFailed()
        {
            Log.Error("{0}: Failed to load initial entity ids from remember entities store within [{1}], stopping shard for backoff and restart",
                _typeName,
                _settings.TuningParameters.UpdatingStateTimeout);
            // parent ShardRegion supervisor will notice that it terminated and will start it again, after backoff
            Context.Stop(Self);
        }

        private void OnEntitiesRemembered(IImmutableSet<EntityId> ids)
        {
            if (ids.Count != 0)
            {
                _entities.AlreadyRemembered(ids);
                Log.Debug("{0}: Restarting set of [{1}] entities", _typeName, ids.Count);
                Context.ActorOf(
                  RememberEntityStarter.Props(Context.Parent, Self, _shardId, ids, _settings),
                  "RememberEntitiesStarter");
            }
            ShardInitialized();
        }

        private void ShardInitialized()
        {
            Log.Debug("{0}: Shard initialized", _typeName);
            Context.Parent.Tell(new ShardInitialized(_shardId));
            Context.Become(Idle);
            Stash.UnstashAll();
        }

        // ===== shard up and running =====

        // when not remembering entities, we stay in this state all the time
        private bool Idle(object message)
        {
            switch (message)
            {
                case Terminated t:
                    ReceiveTerminated(t.ActorRef);
                    return true;
                case EntityTerminated t:
                    ReceiveEntityTerminated(t.Ref);
                    return true;
                case ICoordinatorMessage msg:
                    ReceiveCoordinatorMessage(msg);
                    return true;
                case IRememberEntityCommand msg:
                    ReceiveRememberEntityCommand(msg);
                    return true;
                case ShardRegion.StartEntity msg:
                    StartEntity(msg.EntityId, Sender);
                    return true;
                case Passivate p:
                    Passivate(Sender, p.StopMessage);
                    return true;
                case IShardQuery msg:
                    ReceiveShardQuery(msg);
                    return true;
                case PassivateIdleTick _:
                    PassivateIdleEntities();
                    return true;
                case LeaseLost msg:
                    ReceiveLeaseLost(msg);
                    return true;
                case RememberEntityStoreCrashed msg:
                    ReceiveRememberEntityStoreCrashed(msg);
                    return true;
                case var msg when _extractEntityId(msg).HasValue:
                    DeliverMessage(msg, Sender);
                    return true;
            }
            return false;
        }


        private void RememberUpdate(IImmutableSet<EntityId> add = null, IImmutableSet<EntityId> remove = null)
        {
            add = add ?? ImmutableHashSet<EntityId>.Empty;
            remove = remove ?? ImmutableHashSet<EntityId>.Empty;
            if (_rememberEntitiesStore == null)
                OnUpdateDone(add, remove);
            else
                SendToRememberStore(_rememberEntitiesStore, storingStarts: add, storingStops: remove);
        }


        private void SendToRememberStore(IActorRef store, IImmutableSet<EntityId> storingStarts, IImmutableSet<EntityId> storingStops)
        {
            if (_verboseDebug)
                Log.Debug("{0}: Remember update [{1}] and stops [{2}] triggered",
                    _typeName,
                    string.Join(", ", storingStarts),
                    string.Join(", ", storingStops));

            //  if (flightRecorder != NoOpShardingFlightRecorder) {
            //    storingStarts.foreach { entityId =>
            //      flightRecorder.rememberEntityAdd(entityId)
            //    }
            //    storingStops.foreach { id =>
            //      flightRecorder.rememberEntityRemove(id)
            //    }
            //  }
            var startTime = DateTime.UtcNow;
            var update = new RememberEntitiesShardStore.Update(started: storingStarts, stopped: storingStops);
            store.Tell(update);
            Timers.StartSingleTimer(
                RememberEntityTimeoutKey,
                new RememberEntityTimeout(update),
                _settings.TuningParameters.WaitingForStateTimeout);

            Context.Become(WaitingForRememberEntitiesStore(update, startTime));
        }

        private Receive WaitingForRememberEntitiesStore(
            RememberEntitiesShardStore.Update update,
            DateTime startTime)
        {
            bool WaitingForRememberEntitiesStore(object message)
            {
                switch (message)
                {
                    // none of the current impls will send back a partial update, yet!
                    case RememberEntitiesShardStore.UpdateDone ud:
                        var duration = DateTime.UtcNow - startTime;
                        if (_verboseDebug)
                            Log.Debug("{0}: Update done for ids, started [{1}], stopped [{2}]. Duration {3} ms",
                                _typeName,
                                string.Join(", ", ud.Started),
                                string.Join(", ", ud.Stopped),
                                duration.TotalMilliseconds);
                        //flightRecorder.rememberEntityOperation(duration)
                        Timers.Cancel(RememberEntityTimeoutKey);
                        OnUpdateDone(ud.Started, ud.Stopped);
                        return true;
                    case RememberEntityTimeout t:
                        Log.Error("{0}: Remember entity store did not respond, restarting shard", _typeName);
                        throw new InvalidOperationException($"Async write timed out after {_settings.TuningParameters.UpdatingStateTimeout}");
                    case ShardRegion.StartEntity se:
                        StartEntity(se.EntityId, Sender);
                        return true;
                    case Terminated t:
                        ReceiveTerminated(t.ActorRef);
                        return true;
                    case EntityTerminated t:
                        ReceiveEntityTerminated(t.Ref);
                        return true;
                    case ICoordinatorMessage _:
                        Stash.Stash();
                        return true;
                    case IRememberEntityCommand cmd:
                        ReceiveRememberEntityCommand(cmd);
                        return true;
                    case LeaseLost l:
                        ReceiveLeaseLost(l);
                        return true;
                    case Passivate p:
                        if (_verboseDebug)
                            Log.Debug("{0}: Passivation of [{1}] arrived while updating",
                                _typeName,
                                _entities.EntityId(Sender) ?? $"Unknown actor {Sender}");
                        Passivate(Sender, p.StopMessage);
                        return true;
                    case IShardQuery msg:
                        ReceiveShardQuery(msg);
                        return true;
                    case PassivateIdleTick _:
                        Stash.Stash();
                        return true;
                    case RememberEntityStoreCrashed msg:
                        ReceiveRememberEntityStoreCrashed(msg);
                        return true;
                    case var msg when _extractEntityId(msg).HasValue:
                        DeliverMessage(msg, Sender);
                        return true;
                    case var msg:
                        // shouldn't be any other message types, but just in case
                        Log.Warning("{0}: Stashing unexpected message [{1}] while waiting for remember entities update of starts [{2}], stops [{3}]",
                            _typeName,
                            msg.GetType().Name,
                            string.Join(", ", update.Started),
                            string.Join(", ", update.Stopped));
                        Stash.Stash();
                        return true;
                }
            }
            return WaitingForRememberEntitiesStore;
        }

        private void OnUpdateDone(IImmutableSet<EntityId> starts, IImmutableSet<EntityId> stops)
        {
            // entities can contain both ids from start/stops and pending ones, so we need
            // to mark the completed ones as complete to get the set of pending ones
            foreach (var entityId in starts)
            {
                var stateBeforeStart = _entities.EntityState(entityId);
                // this will start the entity and transition the entity state in sessions to active
                GetOrCreateEntity(entityId);
                SendMsgBuffer(entityId);
                if (stateBeforeStart is RememberingStart rs)
                {
                    foreach (var a in rs.AckTo)
                        a.Tell(new ShardRegion.StartEntityAck(entityId, _shardId));
                }
                TouchLastMessageTimestamp(entityId);
            }
            foreach (var entityId in stops)
            {
                var state = _entities.EntityState(entityId);
                if (_entities.EntityState(entityId) is RememberingStop rs)
                {
                    // this updates entity state
                    PassivateCompleted(entityId);
                }
                else
                {
                    throw new IllegalStateException($"Unexpected state [{state}] when storing stop completed for entity id [{entityId}]");

                }
            }

            var (pendingStarts, pendingStops) = _entities.PendingRememberEntities;
            if (pendingStarts.Count == 0 && pendingStops.Count == 0)
            {
                if (_verboseDebug)
                    Log.Debug("{0}: Update complete, no pending updates, going to idle", _typeName);
                Stash.UnstashAll();
                Context.Become(Idle);
            }
            else
            {
                // Note: no unstashing as long as we are batching, is that a problem?
                var pendingStartIds = pendingStarts.Keys.ToImmutableHashSet();
                if (_verboseDebug)
                    Log.Debug("{0}: Update complete, pending updates, doing another write. Starts [{1}], stops [{2}]",
                        _typeName,
                        string.Join(", ", pendingStartIds),
                        string.Join(", ", pendingStops));
                RememberUpdate(pendingStartIds, pendingStops);
            }
        }

        private void ReceiveLeaseLost(LeaseLost msg)
        {
            // The shard region will re-create this when it receives a message for this shard
            Log.Error("{0}: Shard id [{1}] lease lost, stopping shard and killing [{2}] entities.{3}",
                _typeName,
                _shardId,
                _entities.Count,
                msg.Reason != null ? $" Reason for losing lease: {msg.Reason}" : "");
            // Stop entities ASAP rather than send termination message
            Context.Stop(Self);
        }

        private void ReceiveRememberEntityCommand(IRememberEntityCommand msg)
        {
            switch (msg)
            {
                case RestartTerminatedEntity r:
                    switch (_entities.EntityState(r.Entity))
                    {
                        case WaitingForRestart _:
                            if (_verboseDebug) Log.Debug("{0}: Restarting entity unexpectedly terminated entity [{1}]", _typeName, r.Entity);
                            GetOrCreateEntity(r.Entity);
                            break;
                        case Active _:
                            // it up could already have been started, that's fine
                            if (_verboseDebug)
                                Log.Debug("{0}: Got RestartTerminatedEntity for [{1}] but it is already running", _typeName, r.Entity);
                            break;
                        case var other:
                            throw new IllegalStateException($"Unexpected state for [{r.Entity}] when getting RestartTerminatedEntity: [{other}]");
                    }
                    break;

                case EntitiesMovedToOtherShard m:
                    Log.Info("{0}: Clearing [{1}] remembered entities started elsewhere because of changed shard id extractor",
                        _typeName,
                        m.Ids.Count);

                    foreach (var entityId in m.Ids)
                    {
                        switch (_entities.EntityState(entityId))
                        {
                            case RememberedButNotCreated _:
                                _entities.RememberingStop(entityId);
                                break;
                            case var other:
                                throw new IllegalStateException($"Unexpected state for [{entityId}] when getting ShardIdsMoved: [{other}]");
                        }
                    }
                    RememberUpdate(remove: m.Ids);
                    break;
            }
        }

        /// <summary>
        /// this could be because of a start message or due to a new message for the entity
        /// if it is a start entity then start entity ack is sent after it is created
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="ackTo"></param>
        private void StartEntity(EntityId entityId, IActorRef ackTo)
        {
            switch (_entities.EntityState(entityId))
            {
                case Active _:
                    if (_verboseDebug)
                        Log.Debug("{0}: Request to start entity [{1}] (Already started)", _typeName, entityId);
                    TouchLastMessageTimestamp(entityId);
                    ackTo?.Tell(new ShardRegion.StartEntityAck(entityId, _shardId));
                    break;
                case RememberingStart _:
                    _entities.RememberingStart(entityId, ackTo);
                    break;
                case EntityState state and (RememberedButNotCreated or WaitingForRestart):
                    // already remembered or waiting for backoff to restart, just start it -
                    // this is the normal path for initially remembered entities getting started
                    Log.Debug("{0}: Request to start entity [{1}] (in state [{2}])", _typeName, entityId, state);
                    GetOrCreateEntity(entityId);
                    TouchLastMessageTimestamp(entityId);
                    ackTo?.Tell(new ShardRegion.StartEntityAck(entityId, _shardId));
                    break;
                case Passivating _:
                    // since StartEntity is handled in deliverMsg we can buffer a StartEntity to handle when
                    // passivation completes (triggering an immediate restart)
                    _messageBuffers.Append(entityId, new ShardRegion.StartEntity(entityId), ackTo ?? ActorRefs.NoSender);
                    break;

                case RememberingStop _:
                    // Optimally: if stop is already write in progress, we want to stash, if it is batched for later write we'd want to cancel
                    // but for now
                    Stash.Stash();
                    break;
                case NoState _:
                    // started manually from the outside, or the shard id extractor was changed since the entity was remembered
                    // we need to store that it was started
                    Log.Debug("{0}: Request to start entity [{1}] and ack to [{2}]", _typeName, entityId, ackTo);
                    _entities.RememberingStart(entityId, ackTo);
                    RememberUpdate(add: ImmutableHashSet.Create(entityId));
                    break;
            }
        }

        private void ReceiveCoordinatorMessage(ICoordinatorMessage msg)
        {
            switch (msg)
            {
                case HandOff ho when ho.Shard == _shardId:
                    HandOff(Sender);
                    break;
                case HandOff ho:
                    Log.Warning("{0}: Shard [{1}] can not hand off for another Shard [{2}]", _typeName, _shardId, ho.Shard);
                    break;
                default:
                    Unhandled(msg);
                    break;
            }
        }

        private void ReceiveShardQuery(IShardQuery msg)
        {
            switch (msg)
            {
                case GetCurrentShardState _:
                    if (_verboseDebug)
                        Log.Debug("{0}: GetCurrentShardState, full state: [{1}], active: [{2}]",
                            _typeName,
                            _entities,
                            string.Join(", ", _entities.ActiveEntityIds));
                    Sender.Tell(new CurrentShardState(_shardId, _entities.ActiveEntityIds));
                    break;
                case GetShardStats _:
                    Sender.Tell(new ShardStats(_shardId, _entities.Count));
                    break;
            }
        }

        private void HandOff(IActorRef replyTo)
        {
            if (_handOffStopper != null)
                Log.Warning("{0}: HandOff shard [{1}] received during existing handOff", _typeName, _shardId);
            else
            {
                Log.Debug("{0}: HandOff shard [{1}]", _typeName, _shardId);

                // does conversion so only do once
                var activeEntities = _entities.ActiveEntities;
                if (activeEntities.Count > 0)
                {
                    var entityHandOffTimeout = (_settings.TuningParameters.HandOffTimeout - TimeSpan.FromSeconds(5)).Max(TimeSpan.FromSeconds(1));
                    Log.Debug("{0}: Starting HandOffStopper for shard [{1}] to terminate [{2}] entities.",
                        _typeName,
                        _shardId,
                        activeEntities.Count);
                    foreach (var e in activeEntities)
                        Context.Unwatch(e);
                    _handOffStopper = Context.Watch(Context.ActorOf(
                        ShardRegion.HandOffStopper.Props(_typeName, _shardId, replyTo, activeEntities, _handOffStopMessage, entityHandOffTimeout),
                        "HandOffStopper"));

                    //During hand off we only care about watching for termination of the hand off stopper
                    Context.Become((object message) =>
                    {
                        switch (message)
                        {
                            case Terminated t:
                                ReceiveTerminated(t.ActorRef);
                                return true;
                        }
                        return false;
                    });
                }
                else
                {
                    replyTo.Tell(new ShardStopped(_shardId));
                    Context.Stop(Self);
                }
            }
        }

        private void ReceiveTerminated(IActorRef @ref)
        {
            if (Equals(_handOffStopper, @ref))
                Context.Stop(Self);
        }

        private void ReceiveEntityTerminated(IActorRef @ref)
        {
            var entityId = _entities.EntityId(@ref);
            if (entityId != null)
            {
                if (_passivateIdleTask != null)
                {
                    _lastMessageTimestamp.Remove(entityId);
                }

                switch (_entities.EntityState(entityId))
                {
                    case RememberingStop _:
                        if (_verboseDebug)
                            Log.Debug("{0}: Stop of [{1}] arrived, already is among the pending stops", _typeName, entityId);
                        break;
                    case Active _:
                        if (_rememberEntitiesStore != null)
                        {
                            Log.Debug("{0}: Entity [{1}] stopped without passivating, will restart after backoff", _typeName, entityId);
                            _entities.WaitingForRestart(entityId);
                            var msg = new RestartTerminatedEntity(entityId);
                            Timers.StartSingleTimer(msg, msg, _settings.TuningParameters.EntityRestartBackoff);
                        }
                        else
                        {
                            Log.Debug("{0}: Entity [{1}] terminated", _typeName, entityId);
                            _entities.RemoveEntity(entityId);
                        }
                        break;

                    case Passivating _:
                        if (_rememberEntitiesStore != null)
                        {
                            if (_entities.PendingRememberedEntitiesExist)
                            {
                                // will go in next batch update
                                if (_verboseDebug)
                                    Log.Debug("{0}: [{1}] terminated after passivating, arrived while updating, adding it to batch of pending stops",
                                        _typeName,
                                        entityId);
                                _entities.RememberingStop(entityId);
                            }
                            else
                            {
                                _entities.RememberingStop(entityId);
                                RememberUpdate(remove: ImmutableHashSet.Create(entityId));
                            }
                        }
                        else
                        {
                            if (_messageBuffers.GetOrEmpty(entityId).NonEmpty)
                            {
                                if (_verboseDebug)
                                    Log.Debug("{0}: [{1}] terminated after passivating, buffered messages found, restarting",
                                        _typeName,
                                        entityId);
                                _entities.RemoveEntity(entityId);
                                GetOrCreateEntity(entityId);
                                SendMsgBuffer(entityId);
                            }
                            else
                            {
                                if (_verboseDebug)
                                    Log.Debug("{0}: [{1}] terminated after passivating", _typeName, entityId);
                                _entities.RemoveEntity(entityId);
                            }
                        }
                        break;
                    case var unexpected:
                        Log.Warning("{0}: Got a terminated for [{1}], entityId [{2}] which is in unexpected state [{3}]",
                            _typeName,
                            _entities.Entity(entityId),
                            entityId,
                            unexpected);
                        break;
                }
            }
            else
            {
                Log.Warning("{0}: Unexpected entity terminated: {1}", _typeName, @ref);
            }
        }

        private void Passivate(IActorRef entity, object stopMessage)
        {
            var entityId = _entities.EntityId(entity);
            if (entityId != null)
            {
                if (_entities.IsPassivating(entityId))
                {
                    Log.Debug("{0}: Passivation already in progress for [{1}]. Not sending stopMessage back to entity",
                        _typeName,
                        entityId);
                }
                else if (_messageBuffers.GetOrEmpty(entityId).NonEmpty)
                {
                    Log.Debug("{0}: Passivation when there are buffered messages for [{2}], ignoring passivation", _typeName, entityId);
                }
                else
                {
                    if (_verboseDebug)
                        Log.Debug("{0}: Passivation started for [{1}]", _typeName, entityId);
                    _entities.EntityPassivating(entityId);
                    entity.Tell(stopMessage);
                    //flightRecorder.entityPassivate(entityId);
                }
            }
            else
            {
                Log.Debug("{0}: Unknown entity passivating [{1}]. Not sending stopMessage back to entity", _typeName, entity);
            }
        }

        private void TouchLastMessageTimestamp(EntityId id)
        {
            if (_passivateIdleTask != null)
            {
                _lastMessageTimestamp[id] = DateTime.UtcNow;
            }
        }

        private void PassivateIdleEntities()
        {
            var deadline = DateTime.UtcNow - _settings.PassivateIdleEntityAfter;

            var refsToPassivate = _lastMessageTimestamp.Where(i => i.Value < deadline).Select(i => _entities.Entity(i.Key)).Where(i => i != null).ToList();
            if (refsToPassivate.Count > 0)
            {
                Log.Debug("{0}: Passivating [{1}] idle entities", _typeName, refsToPassivate.Count);
                foreach (var r in refsToPassivate)
                    Passivate(r, _handOffStopMessage);
            }
        }

        /// <summary>
        /// After entity stopped
        /// </summary>
        /// <param name="entityId"></param>
        private void PassivateCompleted(EntityId entityId)
        {
            var hasBufferedMessages = _messageBuffers.GetOrEmpty(entityId).NonEmpty;
            _entities.RemoveEntity(entityId);
            if (hasBufferedMessages)
            {
                Log.Debug("{0}: Entity stopped after passivation [{1}], but will be started again due to buffered messages",
                    _typeName,
                    entityId);
                //flightRecorder.entityPassivateRestart(entityId)
                if (_rememberEntities)
                {
                    // trigger start or batch in case we're already writing to the remember store
                    _entities.RememberingStart(entityId, null);
                    if (!_entities.PendingRememberedEntitiesExist)
                        RememberUpdate(ImmutableHashSet.Create(entityId));
                }
                else
                {
                    GetOrCreateEntity(entityId);
                    SendMsgBuffer(entityId);
                }
            }
            else
            {
                Log.Debug("{0}: Entity stopped after passivation [{1}]", _typeName, entityId);
            }
        }

        private void DeliverMessage(object msg, IActorRef snd)
        {
            var t = _extractEntityId(msg);
            var entityId = t.Value.Item1;
            var payload = t.Value.Item2;

            if (string.IsNullOrEmpty(entityId))
            {
                Log.Warning("{0}: Id must not be empty, dropping message [{1}]", _typeName, msg.GetType().Name);
                Context.System.DeadLetters.Tell(new Dropped(msg, "No recipient entity id", snd, Self));
            }
            else
            {
                switch (payload)
                {
                    case ShardRegion.StartEntity start:
                        // Handling StartEntity both here and in the receives allows for sending it both as is and in an envelope
                        // to be extracted by the entity id extractor.

                        // we can only start a new entity if we are not currently waiting for another write
                        if (_entities.PendingRememberedEntitiesExist)
                        {
                            if (_verboseDebug)
                                Log.Debug("{0}: StartEntity({1}) from [{2}], adding to batch", _typeName, start.EntityId, snd);
                            _entities.RememberingStart(entityId, ackTo: snd);
                        }
                        else
                        {
                            if (_verboseDebug)
                                Log.Debug("{0}: StartEntity({1}) from [{2}], starting", _typeName, start.EntityId, snd);
                            StartEntity(start.EntityId, snd);
                        }
                        break;
                    case var _:
                        switch (_entities.EntityState(entityId))
                        {
                            case Active a:
                                if (_verboseDebug)
                                    Log.Debug("{0}: Delivering message of type [{1}] to [{2}]", _typeName, payload.GetType().Name, entityId);
                                TouchLastMessageTimestamp(entityId);
                                a.Ref.Tell(payload, snd);
                                break;
                            case RememberingStart _:
                            case RememberingStop _:
                            case Passivating _:
                                AppendToMessageBuffer(entityId, msg, snd);
                                break;
                            case EntityState state and (WaitingForRestart or RememberedButNotCreated):
                                if (_verboseDebug)
                                    Log.Debug("{0}: Delivering message of type [{1}] to [{2}] (starting because [{3}])",
                                        _typeName,
                                        payload.GetType().Name,
                                        entityId,
                                        state);
                                var actor = GetOrCreateEntity(entityId);
                                TouchLastMessageTimestamp(entityId);
                                actor.Tell(payload, snd);
                                break;
                            case NoState _:
                                if (!_rememberEntities)
                                {
                                    // don't buffer if remember entities not enabled
                                    GetOrCreateEntity(entityId).Tell(payload, snd);
                                    TouchLastMessageTimestamp(entityId);
                                }
                                else
                                {
                                    if (_entities.PendingRememberedEntitiesExist)
                                    {
                                        // No actor running and write in progress for some other entity id (can only happen with remember entities enabled)
                                        if (_verboseDebug)
                                            Log.Debug("{0}: Buffer message [{1}] to [{2}] (which is not started) because of write in progress for [{3}]",
                                                _typeName,
                                                payload.GetType().Name,
                                                entityId,
                                                _entities.PendingRememberEntities);
                                        AppendToMessageBuffer(entityId, msg, snd);
                                        _entities.RememberingStart(entityId, ackTo: null);
                                    }
                                    else
                                    {
                                        // No actor running and no write in progress, start actor and deliver message when started
                                        if (_verboseDebug)
                                            Log.Debug("{0}: Buffering message [{1}] to [{2}] and starting actor",
                                                _typeName,
                                                payload.GetType().Name,
                                                entityId);
                                        AppendToMessageBuffer(entityId, msg, snd);
                                        _entities.RememberingStart(entityId, ackTo: null);
                                        RememberUpdate(add: ImmutableHashSet.Create(entityId));
                                    }
                                }
                                break;
                        }
                        break;
                }
            }
        }

        private IActorRef GetOrCreateEntity(EntityId id)
        {
            var child = _entities.Entity(id);
            if (child != null)
                return child;

            var name = Uri.EscapeDataString(id);
            var a = Context.ActorOf(_entityProps(id), name);
            Context.WatchWith(a, new EntityTerminated(a));
            Log.Debug("{0}: Started entity [{1}] with entity id [{2}] in shard [{3}]", _typeName, a, id, _shardId);
            _entities.AddEntity(id, a);
            TouchLastMessageTimestamp(id);
            EntityCreated(id);
            return a;
        }

        /// <summary>
        /// Called when an entity has been created. Returning the number
        /// of active entities.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private int EntityCreated(EntityId id) => _entities.NrActiveEntities;

        // ===== buffering while busy saving a start or stop when remembering entities =====

        private void AppendToMessageBuffer(EntityId id, object msg, IActorRef snd)
        {
            if (_messageBuffers.TotalCount >= _settings.TuningParameters.BufferSize)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug("{0}: Buffer is full, dropping message of type [{1}] for entity [{2}]",
                        _typeName,
                        msg.GetType().Name,
                        id);
                Context.System.DeadLetters.Tell(new Dropped(msg, $"Buffer for [{id}] is full", snd, Self));
            }
            else
            {
                if (Log.IsDebugEnabled)
                    Log.Debug("{0}: Message of type [{1}] for entity [{2}] buffered", _typeName, msg.GetType().Name, id);
                _messageBuffers.Append(id, msg, snd);
            }
        }

        /// <summary>
        /// After entity started
        /// </summary>
        /// <param name="entityId"></param>
        private void SendMsgBuffer(EntityId entityId)
        {
            //Get the buffered messages and remove the buffer
            var messages = _messageBuffers.GetOrEmpty(entityId);
            _messageBuffers.Remove(entityId);

            if (messages.NonEmpty)
            {
                GetOrCreateEntity(entityId);
                Log.Debug("{0}: Sending message buffer for entity [{1}] ([{2}] messages)", _typeName, entityId, messages.Count);
                // Now there is no deliveryBuffer we can try to redeliver
                // and as the child exists, the message will be directly forwarded
                foreach (var (Message, Ref) in messages)
                {
                    if (Message is ShardRegion.StartEntity se)
                        StartEntity(se.EntityId, Ref);
                    else
                        DeliverMessage(Message, Ref);
                }
                TouchLastMessageTimestamp(entityId);
            }
        }

        private void DropBufferFor(EntityId entityId, string reason)
        {
            var count = _messageBuffers.Drop(entityId, reason, Context.System.DeadLetters);
            if (Log.IsDebugEnabled && count > 0)
            {
                Log.Debug("{0}: Dropping [{1}] buffered messages for [{2}] because {3}", _typeName, count, entityId, reason);
            }
        }

        private void ReceiveRememberEntityStoreCrashed(RememberEntityStoreCrashed msg)
        {
            throw new InvalidOperationException($"Remember entities store [{msg.Store}] crashed");
        }

        protected override void PostStop()
        {
            ReleaseLeaseIfNeeded();
            _passivateIdleTask.CancelIfNotNull();
            Log.Debug("{0}: Shard [{1}] shutting down", _typeName, _shardId);
        }
    }
}
