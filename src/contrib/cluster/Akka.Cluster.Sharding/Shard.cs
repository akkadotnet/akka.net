//-----------------------------------------------------------------------
// <copyright file="Shard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Scheduler;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Coordination;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;
// ReSharper disable BuiltInTypeReferenceStyle

namespace Akka.Cluster.Sharding
{
    using EntityId = String;
    using Msg = Object;
    using ShardId = String;

    internal interface IShard
    {
        IActorContext Context { get; }
        IActorRef Self { get; }
        IActorRef Sender { get; }
        string TypeName { get; }
        ShardId ShardId { get; }
        Func<string, Props> EntityProps { get; }
        ClusterShardingSettings Settings { get; }
        ExtractEntityId ExtractEntityId { get; }
        ExtractShardId ExtractShardId { get; }
        object HandOffStopMessage { get; }
        ILoggingAdapter Log { get; }
        IActorRef HandOffStopper { get; set; }

        Shard.EntitiesManager Entities { get; set; }
        IActorRef RememberEntitiesStore { get; set; }
        bool VerboseDebug { get; set; }
        bool PreparingForShutdown { get; set; }
        bool RememberEntities { get; set; }

        Shard.ShardState State { get; set; }
        ImmutableDictionary<EntityId, IActorRef> RefById { get; set; }
        ImmutableDictionary<IActorRef, EntityId> IdByRef { get; set; }
        ImmutableDictionary<string, long> LastMessageTimestamp { get; set; }
        ImmutableHashSet<IActorRef> PassivatingRefs { get; set; }
        ImmutableDictionary<EntityId, ImmutableList<(Msg, IActorRef)>> MessageBuffers { get; set; }
        void Unhandled(object message);

        /*
        void ProcessChange<T>(T evt, Action<T> handler) where T : Shard.StateChange;
        void HandleEntityTerminated(IActorRef tref);
        void DeliverTo(string id, object message, object payload, IActorRef sender);
        */

        ICancelable PassivateIdleTask { get; }

        IStash Stash { get; }
        ITimerScheduler Timers { get; }
        Lease Lease { get; }
        TimeSpan LeaseRetryInterval { get; }
        /// <summary>
        /// Override to execute logic once the lease has been acquired
        /// Will be called on the actor thread
        /// </summary>
        void OnLeaseAcquired();
    }

    internal sealed class Shard : ReceiveActor, IShard, IWithTimers, IWithUnboundedStash
    {
        #region internal classes

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

        internal sealed class EntityTerminated
        {
            public EntityTerminated(IActorRef @ref)
            {
                Ref = @ref;
            }

            public IActorRef Ref { get; }
        }

        internal sealed class RememberedEntityIds
        {
            public RememberedEntityIds(IImmutableSet<string> ids)
            {
                Ids = ids;
            }

            public IImmutableSet<EntityId> Ids { get; }
        }

        internal sealed class RememberEntityStoreCrashed
        {
            public RememberEntityStoreCrashed(IActorRef store)
            {
                Store = store;
            }

            public IActorRef Store { get; }
        }

        internal sealed class RememberEntityTimeout
        {
            public RememberEntityTimeout(RememberEntitiesShardStore.ICommand operation)
            {
                Operation = operation;
            }

            public RememberEntitiesShardStore.ICommand Operation { get; }
        }

        internal abstract class ReceiveState
        {
            public abstract Receive Receive { get; }
        }

        #region Remember entities state machine classes

        internal abstract class EntityState
        {
            public abstract EntityState Transition(EntityState newState, EntitiesManager entities);

            public EntityState InvalidTransition(EntityState to, EntitiesManager entities)
            {
                var exception = new ArgumentException(
                    $"Transition from {this} to {to} not allowed, remember entities: {entities.RememberingEntities}");

                if (entities.FailOnIllegalTransition)
                    throw exception; // Crash shard

                // Log and ignore
                entities.Log.Error(exception, "Ignoring illegal state transition in shard");
                return to;
            }
        }

        /// <summary>
        /// Empty state rather than using optionals,
        /// is never really kept track of but used to verify state transitions
        /// and as return value instead of null
        /// </summary>
        internal sealed class NoState : EntityState
        {
            public static readonly NoState Instance = new NoState();
            private NoState() { }
            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                switch (newState)
                {
                    case RememberedButNotCreated _ when entities.RememberingEntities:
                        return RememberedButNotCreated.Instance;
                    case RememberingStart remembering: // we go via this state even if not really remembering
                        return remembering;
                    case Active active when !entities.RememberingEntities:
                        return active;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }
        }

        /// <summary>
        /// In this state we know the entity has been stored in the remember sore but
        /// it hasn't been created yet. E.g. on restart when first getting all the
        /// remembered entity ids.
        /// </summary>
        internal sealed class RememberedButNotCreated : EntityState
        {
            public static readonly RememberedButNotCreated Instance = new RememberedButNotCreated();
            private RememberedButNotCreated() { }
            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                switch (newState)
                {
                    case Active active:  // started on this shard
                        return active;
                    case RememberingStop msg:
                        return msg;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }
        }

        /// <summary>
        /// When remember entities is enabled an entity is in this state while
        /// its existence is being recorded in the remember entities store, or while the stop is queued up
        /// to be stored in the next batch.
        /// </summary>
        internal sealed class RememberingStart : EntityState
        {
            public static readonly RememberingStart Empty = new RememberingStart(ImmutableHashSet<IActorRef>.Empty);

            public static RememberingStart Create(Option<IActorRef> ackTo)
                => ackTo.HasValue ? new RememberingStart((new []{ackTo.Value}).ToImmutableHashSet()) : Empty;
            public static RememberingStart Create(IImmutableSet<IActorRef> ackTo)
                => ackTo.Count > 0 ? new RememberingStart(ackTo) : Empty;

            public IImmutableSet<IActorRef> AckTo { get; }

            private RememberingStart(IImmutableSet<IActorRef> ackTo)
            {
                AckTo = ackTo;
            }

            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                switch (newState)
                {
                    case Active active:
                        return active;
                    case RememberingStart r:
                        if (AckTo.Count == 0)
                            return r.AckTo.Count == 0
                                ? RememberingStart.Empty
                                : newState;
                        else
                            return r.AckTo.Count == 0
                                ? this
                                : new RememberingStart(AckTo.Union(r.AckTo));
                    default:
                        return InvalidTransition(newState, entities);
                }
            }
        }

        internal sealed class RememberingStop : EntityState
        {
            public static readonly RememberingStop Instance = new RememberingStop();
            private RememberingStop() { }
            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                if (newState is NoState msg)
                    return msg;
                return InvalidTransition(newState, entities);
            }
        }

        internal abstract class WithRef : EntityState
        {
            public abstract IActorRef Ref { get; }
        }

        internal sealed class Active : WithRef
        {
            public Active(IActorRef @ref)
            {
                Ref = @ref;
            }

            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                switch (newState)
                {
                    case Passivating passivating:
                        return passivating;
                    case WaitingForRestart msg:
                        return msg;
                    case NoState msg when !entities.RememberingEntities:
                        return msg;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            public override IActorRef Ref { get; }
        }

        internal sealed class Passivating : WithRef
        {
            public Passivating(IActorRef @ref)
            {
                Ref = @ref;
            }

            public override EntityState Transition(EntityState newState, EntitiesManager entities)
            {
                switch (newState)
                {
                    case RememberingStop msg :
                        return msg;
                    case NoState msg when !entities.RememberingEntities:
                        return msg;
                    default:
                        return InvalidTransition(newState, entities);
                }
            }

            public override IActorRef Ref { get; }
        }

        internal sealed class WaitingForRestart : EntityState
        {
            public static readonly WaitingForRestart Instance = new WaitingForRestart();
            private WaitingForRestart() { }
            public override EntityState Transition(EntityState newState, EntitiesManager entities)
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
        }

        #endregion
        internal sealed class EntitiesManager
        {
            public readonly ILoggingAdapter Log;
            public readonly bool RememberingEntities;
            public readonly bool FailOnIllegalTransition;

            private readonly bool _verboseDebug;
            
            private readonly ConcurrentDictionary<EntityId, EntityState> _entities = new ConcurrentDictionary<EntityId, EntityState>();
            // needed to look up entity by ref when a Passivating is received
            private readonly ConcurrentDictionary<IActorRef, EntityId> _byRef = new ConcurrentDictionary<IActorRef, string>();
            // optimization to not have to go through all entities to find batched writes
            private readonly HashSet<EntityId> _remembering = new HashSet<EntityId>();
            
            public EntitiesManager(ILoggingAdapter log, bool rememberingEntities, bool verboseDebug, bool failOnIllegalTransition)
            {
                Log = log;
                RememberingEntities = rememberingEntities;
                _verboseDebug = verboseDebug;
                FailOnIllegalTransition = failOnIllegalTransition;
            }

            public void AlreadyRemembered(IImmutableSet<EntityId> set)
            {
                foreach (var entityId in set)
                {
                    var state = EntityState(entityId).Transition(RememberedButNotCreated.Instance, this);
                    _entities.Put(entityId, state);
                }
            }

            public void RememberingStart(EntityId entityId, Option<IActorRef> ackTo)
            {
                var newState = Shard.RememberingStart.Create(ackTo);
                var state = EntityState(entityId).Transition(newState, this);
                _entities.Put(entityId, state);
                if (RememberingEntities)
                    _remembering.Add(entityId);
            }

            public void RememberingStop(EntityId entityId)
            {
                var state = EntityState(entityId);
                RemoveRefIfThereIsOne(state);
                _entities.Put(entityId, state.Transition(Shard.RememberingStop.Instance, this));
                if (RememberingEntities)
                    _remembering.Add(entityId);
            }

            public void WaitingForRestart(EntityId entityId)
            {
                if (!_entities.TryGetValue(entityId, out var state))
                {
                    state = NoState.Instance;
                }
                else if(state is WithRef wr)
                {
                    _byRef.TryRemove(wr.Ref, out _);
                    state = wr;
                }

                _entities.Put(entityId, state.Transition(Shard.WaitingForRestart.Instance, this));
            }

            public void RemoveEntity(EntityId entityId)
            {
                var state = EntityState(entityId);
                // just verify transition
                state.Transition(NoState.Instance, this);
                RemoveRefIfThereIsOne(state);
                _entities.TryRemove(entityId, out _);
                if (RememberingEntities)
                    _remembering.Remove(entityId);
            }

            public void AddEntity(EntityId entityId, IActorRef @ref)
            {
                var state = EntityState(entityId).Transition(new Active(@ref), this);
                _entities.Put(entityId, state);
                _byRef.Put(@ref, entityId);
                if (RememberingEntities)
                    _remembering.Remove(entityId);
            }

            public Option<IActorRef> Entity(EntityId entityId)
            {
                if (_entities.TryGetValue(entityId, out var entity) && entity is WithRef wr)
                    return new Option<IActorRef>(wr.Ref);

                return Option<IActorRef>.None;
            }

            public EntityState EntityState(EntityId id)
                => _entities.TryGetValue(id, out var state) ? state : NoState.Instance;

            // TODO: make sure there are no by reference or by value dictionary key difference shenanigans here.
            public Option<EntityId> EntityId(IActorRef @ref)
                => new Option<EntityId>(_byRef.GetOrElse(@ref, null));

            public bool IsPassivating(EntityId id)
                => _entities.TryGetValue(id, out var entity) && entity is Passivating;

            public void EntityPassivating(EntityId entityId)
            {
                if(_verboseDebug)
                    Log.Debug("[{0}] passivating", entityId);

                _entities.TryGetValue(entityId, out var entity);
                switch (entity)
                {
                    case WithRef wr:
                        var state = EntityState(entityId).Transition(new Passivating(wr.Ref), this);
                        _entities.Put(entityId, state);
                        break;
                    case var other:
                        throw new IllegalStateException(
                            $"Tried to passivate entity without an actor ref {entityId}. Current state {other}");
                }
            }

            private void RemoveRefIfThereIsOne(EntityState state)
            {
                if (state is WithRef wr)
                    _byRef.TryRemove(wr.Ref, out _);
            }

            // only called once during handoff
            public IImmutableSet<IActorRef> ActiveEntities => _byRef.Keys.ToImmutableHashSet();

            public int NrActiveEntities => _byRef.Count;

            // only called for getting shard stats
            public IImmutableSet<EntityId> ActiveEntityIds => _byRef.Values.ToImmutableHashSet();

            /// <returns>(remembering start, remembering stop)</returns>
            public (IImmutableDictionary<EntityId, RememberingStart>, IImmutableSet<EntityId>) PendingRememberEntities()
            {
                if (_remembering.Count == 0)
                    return (ImmutableDictionary<string, RememberingStart>.Empty, ImmutableHashSet<string>.Empty);

                var starts = ImmutableDictionary.CreateBuilder<string, RememberingStart>();
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
                        case var wat:
                            throw new IllegalStateException($"{entityId} was in the remembering set but has state {wat}");
                    }
                }

                return (starts.ToImmutable(), stops.ToImmutable());
            }

            public bool PendingRememberedEntitiesExist => _remembering.Count > 0;

            public bool EntityExists(EntityId id) => _entities.TryGetValue(id, out _);

            public int Size => _entities.Count;

            public override string ToString()
                => _entities.ToString();
        }

        internal sealed class WaitingForRememberEntitiesStore : ReceiveState
        {
            public WaitingForRememberEntitiesStore(
                IShard shard, 
                RememberEntitiesShardStore.Update update, 
                Stopwatch stopwatch)
            {
                _shard = shard;
                _update = update;
                _stopwatch = stopwatch;
            }

            private readonly IShard _shard;
            private readonly RememberEntitiesShardStore.Update _update;
            private readonly Stopwatch _stopwatch;

            public override Receive Receive => message =>
            {
                switch (message)
                {
                    case RememberEntitiesShardStore.UpdateDone msg:
                        _stopwatch.Stop();
                        _shard.Timers.Cancel(Shards.RememberEntityTimeoutKey);
                        var (storedStarts, storedStops) = msg;
                        var duration = _stopwatch.Elapsed;
                        if (_shard.VerboseDebug)
                            _shard.Log.Debug("{0}: Update done for ids, started [{1}], stopped [{2}]. Duration {3} ms",
                                _shard.TypeName,
                                string.Join(", ", storedStarts),
                                string.Join(", ", storedStops),
                                duration.TotalMilliseconds);
                        _shard.OnUpdateDone(storedStarts, storedStops);
                        return true;

                    case RememberEntityTimeout msg when msg.Operation is RememberEntitiesShardStore.Update:
                        _shard.Log.Error("{0}: Remember entity store did not respond, restarting shard",
                            _shard.TypeName);
                        throw new Exception(
                            $"Async write timed out after {_shard.Settings.TuningParameters.UpdatingStateTimeout.TotalMilliseconds} ms");

                    case ShardRegion.StartEntity msg:
                        _shard.HandleStartEntity(msg.EntityId, new Option<IActorRef>(_shard.Sender));
                        return true;

                    case Terminated msg:
                        _shard.HandleTerminated(msg.ActorRef);
                        return true;

                    case EntityTerminated msg:
                        _shard.EntityTerminated(msg.Ref);
                        return true;

                    case PersistentShardCoordinator.ICoordinatorMessage _:
                        _shard.Stash.Stash();
                        return true;

                    case RememberEntityStoreCrashed msg:
                        _shard.RememberEntityStoreCrashed(msg);
                        return true;

                    case var msg:
                        // shouldn't be any other message types, but just in case
                        _shard.Log.Warning(
                            "{0}: Stashing unexpected message [{1}] while waiting for remember entities update of starts [{2}], stops [{3}]",
                            _shard.TypeName,
                            msg.GetType().Name,
                            string.Join(", ", _update.Started),
                            string.Join(", ", _update.Stopped));
                        _shard.Stash.Stash();
                        return true;
                }
            };
        }

        #endregion

        #region messages

        /// <summary>
        /// A Shard command
        /// </summary>
        public interface IRememberEntityCommand { }

        /// <summary>
        /// When remembering entities and the entity stops without issuing a `Passivate`, we
        /// restart it after a back off using this message.
        /// </summary>
        public sealed class RestartTerminatedEntity : IRememberEntityCommand
        {
            public RestartTerminatedEntity(string entity)
            {
                Entity = entity;
            }

            public EntityId Entity { get; }
        }

        /// <summary>
        /// If the shard id extractor is changed, remembered entities will start in a different shard
        /// and this message is sent to the shard to not leak `entityId -> RememberedButNotStarted` entries
        /// </summary>
        public sealed class EntitiesMovedToOtherShard : IRememberEntityCommand
        {
            public EntitiesMovedToOtherShard(IImmutableSet<string> ids)
            {
                Ids = ids;
            }

            public IImmutableSet<ShardId> Ids { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public interface IShardCommand { }

        /// <summary>
        /// TBD
        /// </summary>
        public interface IShardQuery { }


        /// <summary>
        /// When an remembering entries and the entity stops without issuing a <see cref="Passivate"/>,
        /// we restart it after a back off using this message.
        /// </summary>
        [Serializable]
        public sealed class RestartEntity : IShardCommand
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
        public sealed class RestartEntities : IShardCommand
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

        [Serializable]
        public sealed class PassivateIdleTick : INoSerializationVerificationNeeded
        {
            public static readonly PassivateIdleTick Instance = new PassivateIdleTick();
            private PassivateIdleTick() { }
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
            public static readonly LeaseRetry Instance = new LeaseRetry();
            private LeaseRetry() { }
        }

        #endregion

        IActorContext IShard.Context => Context;
        IActorRef IShard.Self => Self;
        IActorRef IShard.Sender => Sender;
        void IShard.Unhandled(object message) => base.Unhandled(message);

        public ILoggingAdapter Log { get; } = Context.GetLogger();
        public string TypeName { get; }
        public string ShardId { get; }
        public Func<string, Props> EntityProps { get; }
        public ClusterShardingSettings Settings { get; }
        public ExtractEntityId ExtractEntityId { get; }
        public ExtractShardId ExtractShardId { get; }
        public object HandOffStopMessage { get; }
        public IActorRef HandOffStopper { get; set; }

        public EntitiesManager Entities { get; set; }
        public IActorRef RememberEntitiesStore { get; set; }
        public bool VerboseDebug { get; set; }
        public bool PreparingForShutdown { get; set; }
        public bool RememberEntities { get; set; }

        public ShardState State { get; set; } = ShardState.Empty;
        public ImmutableDictionary<string, IActorRef> RefById { get; set; } = ImmutableDictionary<string, IActorRef>.Empty;
        public ImmutableDictionary<IActorRef, string> IdByRef { get; set; } = ImmutableDictionary<IActorRef, string>.Empty;
        public ImmutableDictionary<string, long> LastMessageTimestamp { get; set; } = ImmutableDictionary<string, long>.Empty;
        public ImmutableHashSet<IActorRef> PassivatingRefs { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableDictionary<string, ImmutableList<(object, IActorRef)>> MessageBuffers { get; set; } = ImmutableDictionary<string, ImmutableList<(object, IActorRef)>>.Empty;
        public ICancelable PassivateIdleTask { get; }

        private EntityRecoveryStrategy RememberedEntitiesRecoveryStrategy { get; }

        public Lease Lease { get; }
        public TimeSpan LeaseRetryInterval { get; } = TimeSpan.FromSeconds(5); // won't be used
        public ITimerScheduler Timers { get; set; }
        public IStash Stash { get; set; }

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
            TypeName = typeName;
            ShardId = shardId;
            EntityProps = entityProps;
            Settings = settings;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;

            VerboseDebug =
                Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging", false);

            if (rememberEntitiesProvider != null)
            {
                RememberEntities = true;
                var store = Context.ActorOf(
                    rememberEntitiesProvider.ShardStoreProps(shardId).WithDeploy(Deploy.Local),
                    "RememberEntitiesStore");
                Context.WatchWith(store, new RememberEntityStoreCrashed(store));
                RememberEntitiesStore = store;
            }
            else
            {
                RememberEntitiesStore = null;
            }

            var failOnInvalidStateTransition =
                Context.System.Settings.Config.GetBoolean(
                    "akka.cluster.sharding.fail-on-invalid-entity-state-transition", false);
            Entities = new EntitiesManager(Log, settings.RememberEntities, VerboseDebug, failOnInvalidStateTransition);

            RememberedEntitiesRecoveryStrategy = Settings.TuningParameters.EntityRecoveryStrategy == "constant"
                ? EntityRecoveryStrategy.ConstantStrategy(
                    Context.System,
                    Settings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency,
                    Settings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities)
                : EntityRecoveryStrategy.AllStrategy;

            var idleInterval = TimeSpan.FromTicks(Settings.PassivateIdleEntityAfter.Ticks / 2);
            PassivateIdleTask = Settings.ShouldPassivateIdleEntities
                ? Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(idleInterval, idleInterval, Self, PassivateIdleTick.Instance, Self)
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
        protected override void PreStart()
        {
            this.AcquireLeaseIfNeeded();
        }

        protected override void PostStop()
        {
            PassivateIdleTask?.Cancel();
            base.PostStop();
        }

        public void OnLeaseAcquired()
        {
            Log.Debug("Shard initialized");
            Context.Parent.Tell(new ShardInitialized(ShardId));
            Context.UnbecomeStacked();
        }

        /*
        protected override bool Receive(object message) => this.HandleCommand(message);
        public void ProcessChange<T>(T evt, Action<T> handler) where T : StateChange => this.BaseProcessChange(evt, handler);
        public void HandleEntityTerminated(IActorRef tref) => this.BaseEntityTerminated(tref);
        public void DeliverTo(string id, object message, object payload, IActorRef sender) => this.BaseDeliverTo(id, message, payload, sender);
        */
    }

    internal static class Shards
    {
        #region common shard methods

        private const string LeaseRetryTimer = "lease-retry";
        internal const string RememberEntityTimeoutKey = "RememberEntityTimeout";

        /// <summary>
        /// Will call onLeaseAcquired when completed, also when lease isn't used
        /// </summary>
        /// <typeparam name="TShard"></typeparam>
        /// <param name="shard"></param>
        public static void AcquireLeaseIfNeeded<TShard>(this TShard shard) where TShard : IShard
        {
            if (shard.Lease != null)
            {
                shard.TryGetLease(shard.Lease);
                shard.Context.Become(shard.AwaitingLease());
            }
            else
                shard.TryLoadRememberedEntities();
        }

        public static void TryGetLease<TShard>(this TShard shard, Lease lease) where TShard : IShard
        {
            shard.Log.Info("Acquiring lease {0}", lease.Settings);

            var self = shard.Self;
            lease.Acquire(reason =>
            {
                self.Tell(new Shard.LeaseLost(reason));
            }).ContinueWith(r =>
            {
                if (r.IsFaulted || r.IsCanceled)
                    return new Shard.LeaseAcquireResult(false, r.Exception);
                return new Shard.LeaseAcquireResult(r.Result, null);
            }).PipeTo(shard.Self);
        }

        /// <summary>
        /// Don't send back ShardInitialized so that messages are buffered in the ShardRegion
        /// while awaiting the lease
        /// </summary>
        /// <typeparam name="TShard"></typeparam>
        /// <param name="shard"></param>
        /// <returns></returns>
        public static Receive AwaitingLease<TShard>(this TShard shard) where TShard : IShard
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case Shard.LeaseAcquireResult lar when lar.Acquired:
                        shard.Log.Debug("{0}: Lease acquired", shard.TypeName);
                        shard.TryLoadRememberedEntities();
                        break;

                    case Shard.LeaseAcquireResult lar when !lar.Acquired && lar.Reason == null:
                        shard.Log.Error(
                              "{0}: Failed to get lease for shard id [{1}]. Retry in {2}",
                              shard.TypeName,
                              shard.ShardId,
                              shard.LeaseRetryInterval);
                        shard.Timers.StartSingleTimer(LeaseRetryTimer, Shard.LeaseRetry.Instance, shard.LeaseRetryInterval);
                        break;

                    case Shard.LeaseAcquireResult lar when !lar.Acquired && lar.Reason != null:
                        shard.Log.Error(
                              lar.Reason,
                              "{0} Failed to get lease for shard id [{1}]. Retry in {2}",
                              shard.TypeName,
                              shard.ShardId,
                              shard.LeaseRetryInterval);
                        shard.Timers.StartSingleTimer(LeaseRetryTimer, Shard.LeaseRetry.Instance, shard.LeaseRetryInterval);
                        break;

                    case Shard.LeaseRetry _:
                        shard.TryGetLease(shard.Lease);
                        break;

                    case Shard.LeaseLost ll:
                        shard.HandleLeaseLost(ll);
                        break;

                    case var msg:
                        if(shard.VerboseDebug)
                            shard.Log.Debug(
                                "{0}: Got msg of type [{1}] from [{2}] while waiting for lease, stashing",
                                shard.TypeName,
                                msg.GetType().Name,
                                shard.Sender);
                        shard.Stash.Stash();
                        break;
                }
                return true;
            }

            return Receive;
        }

        // ===== remember entities initialization =====
        private static void TryLoadRememberedEntities<TShard>(this TShard shard) where TShard : IShard
        {
            if (shard.RememberEntitiesStore != null)
            {
                var store = shard.RememberEntitiesStore;
                shard.Log.Debug("{0}: Waiting for load of entity ids using [{1}] to complete", shard.TypeName, store);
                store.Tell(RememberEntitiesShardStore.GetEntities.Instance);
                shard.Timers.StartSingleTimer(
                    RememberEntityTimeoutKey, 
                    new Shard.RememberEntityTimeout(RememberEntitiesShardStore.GetEntities.Instance),
                    shard.Settings.TuningParameters.WaitingForStateTimeout);
                shard.Context.Become(shard.AwaitingRememberedEntities());
                return;
            }

            shard.ShardInitialized();
        }

        private static Receive AwaitingRememberedEntities<TShard>(this TShard shard) where TShard : IShard
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case RememberEntitiesShardStore.RememberedEntities msg:
                        shard.Timers.Cancel(RememberEntityTimeoutKey);
                        shard.OnEntitiesRemembered(msg.Entities);
                        return true;
                    case Shard.RememberEntityTimeout msg when msg.Operation is RememberEntitiesShardStore.GetEntities:
                        shard.LoadingEntityIdsFailed();
                        return true;
                    case var msg:
                        if (shard.VerboseDebug)
                            shard.Log.Debug("{0}: Got msg of type [{1}] from [{2}] while waiting for remember entities, stashing",
                                shard.TypeName,
                                msg.GetType().Name,
                                shard.Sender);
                        shard.Stash.Stash();
                        return true;
                }
            }

            return Receive;
        }

        private static void LoadingEntityIdsFailed<TShard>(this TShard shard) where TShard: IShard
        {
            shard.Log.Error(
                "{0}: Failed to load initial entity ids from remember entities store within [{1}], stopping shard for backoff and restart",
                shard.TypeName,
                shard.Settings.TuningParameters.WaitingForStateTimeout);
            // parent ShardRegion supervisor will notice that it terminated and will start it again, after backoff
            shard.Context.Stop(shard.Self);
        }

        private static void OnEntitiesRemembered<TShard>(this TShard shard, IImmutableSet<EntityId> ids) where TShard : IShard
        {
            if (ids.Count > 0)
            {
                shard.Entities.AlreadyRemembered(ids);
                shard.Log.Debug("{0}: Restarting set of [{1}] entities", shard.TypeName, ids.Count);
                shard.Context.ActorOf(RememberEntityStarter.Props(shard.Context.Parent, shard.Self, shard.ShardId, ids, shard.Settings),
                    "RememberEntitiesStarter");
            }

            shard.ShardInitialized();
        }

        private static void ShardInitialized<TShard>(this TShard shard) where TShard : IShard
        {
            shard.Log.Debug("{0}: Shard initialized", shard.TypeName);
            shard.Context.Parent.Tell(new ShardInitialized(shard.ShardId));
            shard.Context.Become(shard.Idle());
            shard.Stash.UnstashAll();
        }

        // ===== shard up and running =====

        // when not remembering entities, we stay in this state all the time
        public static Receive Idle<TShard>(this TShard shard) where TShard : IShard
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case Terminated t: 
                        shard.HandleTerminated(t.ActorRef);
                        return true;
                    case Shard.EntityTerminated et:
                        shard.EntityTerminated(et.Ref);
                        return true;
                    case PersistentShardCoordinator.ICoordinatorMessage msg:
                        shard.HandleCoordinatorMessage(msg);
                        return true;
                    case Shard.IRememberEntityCommand msg:
                        shard.HandleRememberEntityCommand(msg);
                        return true;
                    case ShardRegion.StartEntity se:
                        shard.HandleStartEntity(se.EntityId, new Option<IActorRef>(shard.Sender));
                        return true;
                    case Passivate msg:
                        shard.Passivate(shard.Sender, msg.StopMessage);
                        return true;
                    case Shard.IShardQuery msg:
                        shard.HandleShardRegionQuery(msg);
                        return true;
                    case Shard.PassivateIdleTick _:
                        shard.PassivateIdleEntities();
                        return true;
                    case Shard.LeaseLost msg:
                        shard.HandleLeaseLost(msg);
                        return true;
                    case Shard.RememberEntityStoreCrashed msg:
                        shard.RememberEntityStoreCrashed(msg);
                        return true;
                    case var msg when shard.ExtractEntityId(msg) != null:
                        shard.DeliverMessage(msg, shard.Sender);
                        return true;
                }
                return false;
            }

            return Receive;
        }

        private static void RememberUpdate<TShard>(this TShard shard, IImmutableSet<EntityId> add = null, IImmutableSet<EntityId> remove = null) where TShard : IShard
        {
            if (add == null)
                add = ImmutableHashSet<string>.Empty;
            if (remove == null)
                remove = ImmutableHashSet<string>.Empty;

            if (shard.RememberEntitiesStore != null)
            {
                shard.SendToRememberStore(shard.RememberEntitiesStore, storingStarts: add, storingStops: remove);
            }
            else
            {
                shard.OnUpdateDone(add, remove);
            }
        }

        private static void SendToRememberStore<TShard>(this TShard shard, IActorRef store, IImmutableSet<EntityId> storingStarts, IImmutableSet<EntityId> storingStops) where TShard : IShard
        {
            if (shard.VerboseDebug)
                shard.Log.Debug("{}: Remember update [{}] and stops [{}] triggered",
                    shard.TypeName,
                    string.Join(", ", storingStarts),
                    string.Join(", ", storingStops));

            var stopwatch = Stopwatch.StartNew();
            var update = new RememberEntitiesShardStore.Update(started: storingStarts, stopped: storingStops);
            store.Tell(update);
            shard.Timers.StartSingleTimer(
                RememberEntityTimeoutKey,
                new Shard.RememberEntityTimeout(update),
                shard.Settings.TuningParameters.UpdatingStateTimeout);

            shard.Become(new Shard.WaitingForRememberEntitiesStore(shard, update, stopwatch));
        }

        internal static void OnUpdateDone<TShard>(this TShard shard, IImmutableSet<EntityId> starts, IImmutableSet<EntityId> stops) where TShard : IShard
        {
            // entities can contain both ids from start/stops and pending ones, so we need
            // to mark the completed ones as complete to get the set of pending ones
            foreach (var entityId in starts)
            {
                var stateBeforeStart = shard.Entities.EntityState(entityId);
                // this will start the entity and transition the entity state in sessions to active
                shard.GetOrCreateEntity(entityId);
                shard.SendMessageBuffer(entityId);
                if(stateBeforeStart is Shard.RememberingStart msg)
                {
                    foreach (var actorRef in msg.AckTo)
                    {
                        actorRef.Tell(new ShardRegion.StartEntityAck(entityId, shard.ShardId));
                    }
                }

                shard.TouchLastMessageTimestamp(entityId);
            }

            foreach (var entityId in stops)
            {
                switch (shard.Entities.EntityState(entityId))
                {
                    case Shard.RememberingStop _:
                        // this updates entity state
                        shard.PassivateCompleted(entityId);
                        break;
                    case var state:
                        throw new IllegalStateException(
                            $"Unexpected state [{state}] when storing stop completed for entity id [{entityId}]");
                }
            }

            var (pendingStarts, pendingStops) = shard.Entities.PendingRememberEntities();
            if (pendingStarts.Count == 0 && pendingStops.Count == 0)
            {
                if (shard.VerboseDebug)
                    shard.Log.Debug("{0}: Update complete, no pending updates, going to idle", shard.TypeName);
                shard.Stash.UnstashAll();
                shard.Context.Become(shard.Idle());
            }
            else
            {
                // Note: no unstashing as long as we are batching, is that a problem?
                var pendingStartIds = pendingStarts.Keys.ToImmutableHashSet();
                if (shard.VerboseDebug)
                    shard.Log.Debug("{0}: Update complete, pending updates, doing another write. Starts [{1}], stops [{2}]",
                        shard.TypeName,
                        string.Join(", ", pendingStartIds),
                        string.Join(", ", pendingStops));
                shard.RememberUpdate(pendingStartIds, pendingStops);
            }
        }

        public static void HandleLeaseLost<TShard>(this TShard shard, Shard.LeaseLost message) where TShard : IShard
        {
            // The shard region will re-create this when it receives a message for this shard
            shard.Log.Error(
                message.Reason, 
                "{0}: Shard id [{1}] lease lost, stopping shard and killing [{2}] entities.", 
                shard.TypeName, 
                shard.ShardId,
                shard.Entities.Size);
            // Stop entities ASAP rather than send termination message
            shard.Context.Stop(shard.Self);
        }

        private static void HandleRememberEntityCommand(this IShard shard, Shard.IRememberEntityCommand msg)
        {
            switch (msg)
            {
                case Shard.RestartTerminatedEntity r:
                {
                    var entityId = r.Entity;
                    switch (shard.Entities.EntityState(entityId))
                    {
                        case Shard.WaitingForRestart _:
                            if (shard.VerboseDebug)
                                shard.Log.Debug(
                                    "{0}: Restarting entity unexpectedly terminated entity [{1}]",
                                    shard.TypeName,
                                    entityId);
                            shard.GetOrCreateEntity(entityId);
                            break;
                        case Shard.Active _:
                            // it up could already have been started, that's fine
                            if (shard.VerboseDebug)
                                shard.Log.Debug(
                                    "{0}: Got RestartTerminatedEntity for [{1}] but it is already running",
                                    shard.TypeName,
                                    entityId);
                            break;
                        case var other:
                            throw new IllegalStateException(
                                $"Unexpected state for [{entityId}] when getting RestartTerminatedEntity: [{other}]");
                    }

                    break;
                }

                case Shard.EntitiesMovedToOtherShard m:
                {
                    var movedEntityIds = m.Ids;
                    shard.Log.Info(
                        "{0}: Clearing [{1}] remembered entities started elsewhere because of changed shard id extractor",
                        shard.TypeName,
                        movedEntityIds.Count);
                    foreach (var entityId in movedEntityIds)
                    {
                        switch (shard.Entities.EntityState(entityId))
                        {
                            case Shard.RememberedButNotCreated _:
                                shard.Entities.RememberingStop(entityId);
                                break;
                            case var other:
                                throw new IllegalStateException(
                                    $"Unexpected state for [{entityId}] when getting ShardIdsMoved: [{other}]");
                        }
                    }
                    shard.RememberUpdate(remove: movedEntityIds);
                    break;
                }
            }
        }

        // this could be because of a start message or due to a new message for the entity
        // if it is a start entity then start entity ack is sent after it is created
        internal static void HandleStartEntity<TShard>(this TShard shard, EntityId entityId, Option<IActorRef> ackTo) where TShard : IShard
        {
            switch (shard.Entities.EntityState(entityId))
            {
                case Shard.Active _:
                    if (shard.VerboseDebug)
                        shard.Log.Debug("{0}: Request to start entity [{1}] (Already started)", shard.TypeName, entityId);
                    shard.TouchLastMessageTimestamp(entityId);
                    if(ackTo.HasValue)
                        ackTo.Value.Tell(new ShardRegion.StartEntityAck(entityId, shard.ShardId));
                    break;
                case Shard.RememberingStart _:
                    shard.Entities.RememberingStart(entityId, ackTo);
                    break;
                case Shard.Passivating _:
                    // since StartEntity is handled in deliverMsg we can buffer a StartEntity to handle when
                    // passivation completes (triggering an immediate restart)
                    shard.MessageBuffers = shard.MessageBuffers.Add(
                        entityId, 
                        ImmutableList<(object, IActorRef)>.Empty
                            .Add((new ShardRegion.StartEntity(entityId), ackTo.GetOrElse(ActorRefs.NoSender))));
                    break;
                case Shard.RememberingStop _:
                    // Optimally: if stop is already write in progress, we want to stash, if it is batched
                    // for later write we'd want to cancel but for now
                    shard.Stash.Stash();
                    break;
                case Shard.NoState _:
                    // started manually from the outside, or the shard id extractor was changed since the entity
                    // was remembered we need to store that it was started
                    shard.Entities.RememberingStart(entityId, ackTo);
                    shard.RememberUpdate(add: ImmutableHashSet<string>.Empty.Add(entityId));
                    break;
            }
        }

        /*
        public static void BaseProcessChange<TShard, T>(this TShard shard, T evt, Action<T> handler)
            where TShard : IShard
            where T : Shard.StateChange
        {
            shard.Log.Debug("{0}: Calling BaseProcessChange for {1} and event [{2}]", shard.TypeName, shard, evt);
            handler(evt);
        }

        public static bool HandleCommand<TShard>(this TShard shard, object message) where TShard : IShard
        {
            switch (message)
            {
                case Terminated t:
                    shard.HandleTerminated(t.ActorRef);
                    return true;
                case PersistentShardCoordinator.ICoordinatorMessage cm:
                    shard.HandleCoordinatorMessage(cm);
                    return true;
                case Shard.IShardCommand sc:
                    shard.HandleShardCommand(sc);
                    return true;
                case ShardRegion.StartEntity se:
                    shard.HandleStartEntity(se);
                    return true;
                case ShardRegion.StartEntityAck sea:
                    shard.HandleStartEntityAck(sea);
                    return true;
                case IShardRegionCommand src:
                    shard.HandleShardRegionCommand(src);
                    return true;
                case Shard.IShardQuery sq:
                    shard.HandleShardRegionQuery(sq);
                    return true;
                case ShardRegion.RestartShard _:
                    return true;
                case Shard.PassivateIdleTick _:
                    shard.PassivateIdleEntities();
                    return true;

                case Shard.LeaseLost ll:
                    shard.HandleLeaseLost(ll);
                    return true;

                case var _ when shard.ExtractEntityId(message).HasValue:
                    shard.DeliverMessage(message, shard.Context.Sender);
                    return true;
            }
            return false;
        }
        */

        internal static void HandleShardRegionQuery<TShard>(this TShard shard, Shard.IShardQuery query) where TShard : IShard
        {
            switch (query)
            {
                case Shard.GetCurrentShardState _:
                    if (shard.VerboseDebug)
                        shard.Log.Debug(
                            "{}: GetCurrentShardState, full state: [{}], active: [{}]",
                            shard.TypeName,
                            shard.Entities,
                            string.Join(", ", shard.Entities.ActiveEntityIds));
                    shard.Context.Sender.Tell(new Shard.CurrentShardState(shard.ShardId, shard.Entities.ActiveEntityIds));
                    break;
                case Shard.GetShardStats _:
                    shard.Context.Sender.Tell(new Shard.ShardStats(shard.ShardId, shard.Entities.Size));
                    break;
            }
        }

        /*
        private static void HandleShardCommand<TShard>(this TShard shard, Shard.IShardCommand message) where TShard : IShard
        {
            switch (message)
            {
                case Shard.RestartEntity restartEntity:
                    shard.GetOrCreateEntity(restartEntity.EntityId);
                    break;
                case Shard.RestartEntities restartEntities:
                    shard.HandleRestartEntities(restartEntities.Entries);
                    break;
            }
        }

        private static void HandleStartEntityAck<TShard>(this TShard shard, ShardRegion.StartEntityAck ack) where TShard : IShard
        {
            if (ack.ShardId != shard.ShardId && shard.State.Entries.Contains(ack.EntityId))
            {
                shard.Log.Debug("Entity [{0}] previously owned by shard [{1}] started in shard [{2}]", ack.EntityId, shard.ShardId, ack.ShardId);
                shard.ProcessChange(new Shard.EntityStopped(ack.EntityId), _ =>
                {
                    shard.State = new Shard.ShardState(shard.State.Entries.Remove(ack.EntityId));
                    shard.MessageBuffers = shard.MessageBuffers.Remove(ack.EntityId);
                });
            }
        }

        private static void HandleRestartEntities<TShard>(this TShard shard, IImmutableSet<EntityId> ids) where TShard : IShard
        {
            shard.Context.ActorOf(RememberEntityStarter.Props(shard.Context.Parent, shard.TypeName, shard.ShardId, ids, shard.Settings, shard.Sender));
        }

        private static void HandleShardRegionCommand<TShard>(this TShard shard, IShardRegionCommand message) where TShard : IShard
        {
            if (message is Passivate passivate)
                shard.Passivate(shard.Sender, passivate.StopMessage);
            else
                shard.Unhandled(message);
        }
        */

        private static void HandleCoordinatorMessage<TShard>(this TShard shard, PersistentShardCoordinator.ICoordinatorMessage message) where TShard : IShard
        {
            switch (message)
            {
                case PersistentShardCoordinator.HandOff handOff when handOff.Shard == shard.ShardId:
                    shard.HandOff(shard.Sender);
                    break;
                case PersistentShardCoordinator.HandOff handOff:
                    shard.Log.Warning("{0}: Shard [{1}] can not hand off for another Shard [{2}]", shard.TypeName, shard.ShardId, handOff.Shard);
                    break;
                default:
                    shard.Unhandled(message);
                    break;
            }
        }

        private static void HandOff<TShard>(this TShard shard, IActorRef replyTo) where TShard : IShard
        {
            if(shard.HandOffStopper != null)
            {
                shard.Log.Warning("{0}: HandOff shard [{1}] received during existing handOff", shard.TypeName, shard.ShardId);
                return;
            }

            shard.Log.Debug("{}: HandOff shard [{}]", shard.TypeName, shard.ShardId);
            // does conversion so only do once
            var activeEntities = shard.Entities.ActiveEntities;
            if(shard.PreparingForShutdown)
            {
                shard.Log.Info(
                    "{0}: HandOff shard [{1}] while preparing for shutdown. Stopping right away.", 
                    shard.TypeName,
                    shard.ShardId);
                foreach (var entity in activeEntities)
                {
                    entity.Tell(shard.HandOffStopMessage);
                }
                replyTo.Tell(new PersistentShardCoordinator.ShardStopped(shard.ShardId));
                shard.Context.Stop(shard.Self);
            } 
            else if(activeEntities.Count > 0 && !shard.PreparingForShutdown)
            {
                var entityHandOffTimeout =
                    (shard.Settings.TuningParameters.HandOffTimeout - TimeSpan.FromSeconds(5)).Max(TimeSpan.FromSeconds(1));
                shard.Log.Debug("{0}: Starting HandOffStopper for shard [{1}] to terminate [{2}] entities.",
                    shard.TypeName,
                    shard.ShardId,
                    activeEntities.Count);
                foreach (var entity in activeEntities)
                {
                    shard.Context.Unwatch(entity);
                }

                shard.HandOffStopper = shard.Context.Watch(shard.Context.ActorOf(
                    ShardRegion.HandOffStopper.Props(shard.TypeName, shard.ShardId, replyTo, activeEntities, shard.HandOffStopMessage, entityHandOffTimeout),
                    "HandOffStopper"));

                //During hand off we only care about watching for termination of the hand off stopper
                shard.Context.Become(message =>
                {
                    switch (message)
                    {
                        case Terminated msg:
                            shard.HandleTerminated(msg.ActorRef);
                            return true;
                    }

                    return false;
                });
            }
            else
            {
                replyTo.Tell(new PersistentShardCoordinator.ShardStopped(shard.ShardId));
                shard.Context.Stop(shard.Self);
            }
        }

        internal static void HandleTerminated<TShard>(this TShard shard, IActorRef terminatedRef) where TShard : IShard
        {
            if (Equals(shard.HandOffStopper, terminatedRef))
                shard.Context.Stop(shard.Context.Self);
        }

        private static void Passivate<TShard>(this TShard shard, IActorRef entity, object stopMessage) where TShard : IShard
        {
            var id = shard.Entities.EntityId(entity);
            if (id.HasValue)
            {
                if (shard.Entities.IsPassivating(id.Value))
                {
                    shard.Log.Debug(
                        "{0}: Passivation already in progress for [{1}]. Not sending stopMessage back to entity",
                        shard.TypeName,
                        id);
                }else if (shard.MessageBuffers.GetValueOrDefault(id.Value) != null)
                {
                    shard.Log.Debug(
                        "{0}: Passivation when there are buffered messages for [{1}], ignoring passivation",
                        shard.TypeName, 
                        id);
                }
                else
                {
                    if (shard.VerboseDebug)
                        shard.Log.Debug("{0}: Passivation started for [{1}]", shard.TypeName, id);
                    shard.Entities.EntityPassivating(id.Value);
                    entity.Tell(stopMessage);
                }
            }
            else
            {
                shard.Log.Debug(
                    "{0}: Unknown entity passivating [{1}]. Not sending stopMessage back to entity", 
                    shard.TypeName,
                    entity);
            }
        }

        public static void TouchLastMessageTimestamp<TShard>(this TShard shard, EntityId id) where TShard : IShard
        {
            if (shard.PassivateIdleTask != null)
            {
                shard.LastMessageTimestamp = shard.LastMessageTimestamp.ContainsKey(id) 
                    ? shard.LastMessageTimestamp.SetItem(id, DateTime.UtcNow.Ticks) 
                    : shard.LastMessageTimestamp.Add(id, DateTime.UtcNow.Ticks);
            }
        }

        private static void PassivateIdleEntities<TShard>(this TShard shard) where TShard : IShard
        {
            var deadline = DateTime.Now.Ticks - shard.Settings.PassivateIdleEntityAfter.Ticks;
            var refsToPassivate = shard.LastMessageTimestamp
                .Where(e => e.Value < deadline && shard.Entities.Entity(e.Key).HasValue)
                .Select(e => shard.Entities.Entity(e.Key).Value)
                .ToList();

            if (refsToPassivate.Count > 0)
            {
                shard.Log.Debug("{}: Passivating [{}] idle entities", shard.TypeName, refsToPassivate.Count);
                foreach (var @ref in refsToPassivate)
                {
                    shard.Passivate(@ref, shard.HandOffStopMessage);
                }
            }
        }

        public static void PassivateCompleted<TShard>(this TShard shard, EntityId entityId) where TShard : IShard
        {
            var hasBufferedMessages = shard.MessageBuffers.GetOrElse(entityId, null) != null;

            shard.Entities.RemoveEntity(entityId);
            if (hasBufferedMessages)
            {
                shard.Log.Debug(
                    "{0}: Entity stopped after passivation [{1}], but will be started again due to buffered messages",
                    shard.TypeName,
                    entityId);
                if (shard.RememberEntities)
                {
                    // trigger start or batch in case we're already writing to the remember store
                    shard.Entities.RememberingStart(entityId, Option<IActorRef>.None);
                    if(!shard.Entities.PendingRememberedEntitiesExist)
                        shard.RememberUpdate(add: ImmutableHashSet<EntityId>.Empty.Add(entityId));
                }
                else
                {
                    shard.GetOrCreateEntity(entityId);
                    shard.SendMessageBuffer(entityId);
                }
            }
            else
            {
                shard.Log.Debug("{0}: Entity stopped after passivation [{1}]", shard.TypeName, entityId);
            }
        }

        // ===== buffering while busy saving a start or stop when remembering entities =====
        public static void AppendToMessageBuffer<TShard>(this TShard shard, EntityId id, object message, IActorRef sender)
            where TShard : IShard
        {
            if(MessageBufferTotalCount(shard.MessageBuffers) >= shard.Settings.TuningParameters.BufferSize)
            {
                if (shard.Log.IsDebugEnabled)
                    shard.Log.Debug(
                        "{0}: Buffer is full, dropping message of type [{1}] for entity [{2}]",
                        shard.TypeName,
                        message.GetType(),
                        id);
                shard.Context.System.DeadLetters.Tell(message, sender);
            }
            else
            {
                if (shard.Log.IsDebugEnabled)
                    shard.Log.Debug(
                        "{0}: Message of type [{1}] for entity [{2}] buffered", shard.TypeName, message.GetType(), id);

                if (!shard.MessageBuffers.TryGetValue(id, out var buffer))
                {
                    buffer = ImmutableList<(object, IActorRef)>.Empty;
                }
                buffer = buffer.Add((message, sender));
                shard.MessageBuffers = shard.MessageBuffers.SetItem(id, buffer);
            }
        }

        private static long MessageBufferTotalCount(ImmutableDictionary<EntityId, ImmutableList<(Msg, IActorRef)>> buffer)
            => buffer.Sum(kvp => kvp.Value.Count);

        // After entity started
        public static void SendMessageBuffer<TShard>(this TShard shard, EntityId entityId) where TShard : IShard
        {
            //Get the buffered messages and remove the buffer
            var messages = shard.MessageBuffers.GetOrElse(entityId, ImmutableList<(object, IActorRef)>.Empty);
            shard.MessageBuffers = shard.MessageBuffers.Remove(entityId);

            if (messages.Count > 0)
            {
                shard.GetOrCreateEntity(entityId);
                shard.Log.Debug(
                    "{}: Sending message buffer for entity [{}] ([{}] messages)", 
                    shard.TypeName, 
                    entityId,
                    messages.Count);

                // Now there is no deliveryBuffer we can try to redeliver
                // and as the child exists, the message will be directly forwarded
                foreach (var (payload, sender) in messages)
                {
                    if (payload is ShardRegion.StartEntity msg)
                    {
                        shard.HandleStartEntity(msg.EntityId, new Option<IActorRef>(sender));
                    }
                    else
                    {
                        shard.DeliverMessage(payload, sender);
                    }
                }
            }
            shard.TouchLastMessageTimestamp(entityId);
        }

        internal static void RememberEntityStoreCrashed<TShard>(this TShard shard, Shard.RememberEntityStoreCrashed msg)
            where TShard : IShard
        {
            throw new Exception($"Remember entities store [{msg.Store}] crashed");
        }

        internal static void DeliverMessage<TShard>(this TShard shard, object message, IActorRef sender) where TShard : IShard
        {
            var t = shard.ExtractEntityId(message);
            var entityId = t.Value.Item1;
            var payload = t.Value.Item2;

            if (string.IsNullOrEmpty(entityId))
            {
                shard.Log.Warning("{0}: Id must not be empty, dropping message [{1}]", shard.TypeName, message.GetType());
                shard.Context.System.DeadLetters.Tell(message);
            }
            else
            {
                if (payload is ShardRegion.StartEntity start)
                {
                    // Handling StartEntity both here and in the receives allows for sending it both as is and in an envelope
                    // to be extracted by the entity id extractor.

                    // we can only start a new entity if we are not currently waiting for another write
                    if (shard.Entities.PendingRememberedEntitiesExist)
                    {
                        if (shard.VerboseDebug)
                            shard.Log.Debug("{0}: StartEntity({1}) from [{2}], adding to batch", shard.TypeName, start.EntityId, sender);
                        shard.Entities.RememberingStart(entityId, new Option<IActorRef>(sender));
                    }
                    else
                    {
                        if (shard.VerboseDebug)
                            shard.Log.Debug("{0}: StartEntity({1}) from [{2}], starting", shard.TypeName, start.EntityId, sender);
                        shard.HandleStartEntity(start.EntityId, new Option<IActorRef>(sender));
                    }
                }
                else
                {
                    switch (shard.Entities.EntityState(entityId))
                    {
                        case Shard.Active msg:
                            if (shard.VerboseDebug)
                                shard.Log.Debug("{0}: Delivering message of type [{1}] to [{2}]", shard.TypeName,
                                    payload.GetType(), entityId);
                            shard.TouchLastMessageTimestamp(entityId);
                            msg.Ref.Tell(payload, sender);
                            break;
                        case Shard.RememberingStart _:
                        case Shard.RememberingStop _:
                        case Shard.Passivating _:
                            shard.AppendToMessageBuffer(entityId, message, sender);
                            break;
                        case Shard.WaitingForRestart state:
                        {
                            if (shard.VerboseDebug)
                                shard.Log.Debug(
                                    "{0}: Delivering message of type [{1}] to [{2}] (starting because [{3}])",
                                    shard.TypeName,
                                    payload.GetType(),
                                    entityId,
                                    state);
                            var actor = shard.GetOrCreateEntity(entityId);
                            shard.TouchLastMessageTimestamp(entityId);
                            actor.Tell(payload, sender);
                            break;
                        }                        
                        case Shard.RememberedButNotCreated state:
                        {
                            if (shard.VerboseDebug)
                                shard.Log.Debug(
                                    "{0}: Delivering message of type [{1}] to [{2}] (starting because [{3}])",
                                    shard.TypeName,
                                    payload.GetType(),
                                    entityId,
                                    state);
                            var actor = shard.GetOrCreateEntity(entityId);
                            shard.TouchLastMessageTimestamp(entityId);
                            actor.Tell(payload, sender);
                            break;
                        }

                        case Shard.NoState _:
                            if(!shard.RememberEntities)
                            {
                                // don't buffer if remember entities not enabled
                                shard.GetOrCreateEntity(entityId).Tell(payload, sender);
                                shard.TouchLastMessageTimestamp(entityId);
                            }
                            else
                            {
                                if (shard.Entities.PendingRememberedEntitiesExist)
                                {
                                    // No actor running and write in progress for some other entity id (can only happen with remember entities enabled)
                                    if (shard.VerboseDebug)
                                        shard.Log.Debug(
                                            "{}: Buffer message [{}] to [{}] (which is not started) because of write in progress for [{}]",
                                            shard.TypeName,
                                            payload.GetType(),
                                            entityId,
                                            shard.Entities.PendingRememberEntities());
                                    shard.AppendToMessageBuffer(entityId, message, sender);
                                    shard.Entities.RememberingStart(entityId, Option<IActorRef>.None);
                                }
                                else
                                {
                                    // No actor running and no write in progress, start actor and deliver message when started
                                    if (shard.VerboseDebug)
                                        shard.Log.Debug("{}: Buffering message [{}] to [{}] and starting actor",
                                            shard.TypeName,
                                            payload.GetType(),
                                            entityId);
                                    shard.AppendToMessageBuffer(entityId, message, sender);
                                    shard.Entities.RememberingStart(entityId, Option<IActorRef>.None);
                                    shard.RememberUpdate(add: ImmutableHashSet<EntityId>.Empty.Add(entityId));
                                }
                            }

                            break;
                    }
                }
            }
        }

        internal static void EntityTerminated<TShard>(this TShard shard, IActorRef @ref) where TShard : IShard
        {
            var id = shard.Entities.EntityId(@ref);
            if (id.HasValue)
            {
                var entityId = id.Value;
                if (shard.PassivateIdleTask != null)
                {
                    shard.LastMessageTimestamp = shard.LastMessageTimestamp.Remove(entityId);
                }

                switch (shard.Entities.EntityState(entityId))
                {
                    case Shard.RememberingStop _:
                        if (shard.VerboseDebug)
                            shard.Log.Debug(
                                "{0}: Stop of [{1}] arrived, already is among the pending stops", shard.TypeName,
                                entityId);
                        break;

                    case Shard.Active _:
                        if (shard.RememberEntitiesStore != null)
                        {
                            shard.Log.Debug(
                                "{0}: Entity [{1}] stopped without passivating, will restart after backoff",
                                shard.TypeName, 
                                entityId);
                            shard.Entities.WaitingForRestart(entityId);
                            var msg = new Shard.RestartTerminatedEntity(entityId);
                            shard.Timers.StartSingleTimer(msg, msg, shard.Settings.TuningParameters.EntityRestartBackoff);
                        }
                        else
                        {
                            shard.Log.Debug("{0}: Entity [{1}] terminated", shard.TypeName, entityId);
                            shard.Entities.RemoveEntity(entityId);
                        }
                        break;

                    case Shard.Passivating _:
                        if (shard.RememberEntitiesStore != null)
                        {
                            if (shard.Entities.PendingRememberedEntitiesExist)
                            {
                                // will go in next batch update
                                if (shard.VerboseDebug)
                                    shard.Log.Debug(
                                        "{0}: [{1}] terminated after passivating, arrived while updating, adding it to batch of pending stops",
                                        shard.TypeName,
                                        entityId);
                                shard.Entities.RememberingStop(entityId);
                            }
                            else
                            {
                                shard.Entities.RememberingStop(entityId);
                                shard.RememberUpdate(remove: ImmutableHashSet<string>.Empty.Add(entityId));
                            }
                        }
                        else
                        {
                            if (shard.MessageBuffers.GetOrElse(entityId, null) != null)
                            {
                                if(shard.VerboseDebug)
                                    shard.Log.Debug(
                                        "{0}: [{1}] terminated after passivating, buffered messages found, restarting",
                                        shard.TypeName,
                                        entityId);
                                shard.Entities.RemoveEntity(entityId);
                                shard.GetOrCreateEntity(entityId);
                                shard.SendMessageBuffer(entityId);
                            }
                            else
                            {
                                if (shard.VerboseDebug)
                                    shard.Log.Debug(
                                        "{0}: [{1}] terminated after passivating", shard.TypeName, entityId);
                                shard.Entities.RemoveEntity(entityId);
                            }
                        }

                        break;
                    case var unexpected:
                        var actorRef = shard.Entities.Entity(entityId);
                        shard.Log.Warning(
                            "{0}: Got a terminated for [{1}], entityId [{2}] which is in unexpected state [{3}]",
                            shard.TypeName,
                            actorRef,
                            entityId,
                            unexpected);
                        break;
                }
            }
            else
            {
                shard.Log.Warning("{0}: Unexpected entity terminated: {1}", shard.TypeName, @ref);
            }
        }

        /*
        public static void BaseEntityTerminated<TShard>(this TShard shard, IActorRef tref) where TShard : IShard
        {
            if (!shard.IdByRef.TryGetValue(tref, out var id)) return;
            shard.IdByRef = shard.IdByRef.Remove(tref);
            shard.RefById = shard.RefById.Remove(id);

            if (shard.PassivateIdleTask != null)
            {
                shard.LastMessageTimestamp = shard.LastMessageTimestamp.Remove(id);
            }

            if (shard.MessageBuffers.TryGetValue(id, out var buffer) && buffer.Count != 0)
            {
                shard.Log.Debug("Starting entity [{0}] again, there are buffered messages for it", id);
                shard.SendMessageBuffer(new Shard.EntityStarted(id));
            }
            else
            {
                shard.ProcessChange(new Shard.EntityStopped(id), stopped => shard.PassivateCompleted(stopped));
            }

            shard.PassivatingRefs = shard.PassivatingRefs.Remove(tref);
        }

        internal static void BaseDeliverTo<TShard>(this TShard shard, string id, object message, object payload, IActorRef sender) where TShard : IShard
        {
            shard.TouchLastMessageTimestamp(id);
            shard.GetOrCreateEntity(id).Tell(payload, sender);
        }
        */

        internal static IActorRef GetOrCreateEntity<TShard>(this TShard shard, string id) where TShard : IShard
        {
            var entity = shard.Entities.Entity(id);
            if (entity.HasValue)
                return entity.Value;

            var name = Uri.EscapeDataString(id);
            var a = shard.Context.ActorOf(shard.EntityProps(id), name);
            shard.Context.WatchWith(a, new Shard.EntityTerminated(a));
            shard.Log.Debug("{0}: Started entity [{1}] with entity id [{2}] in shard [{3}]", shard.TypeName, a, id, shard.ShardId);
            shard.Entities.AddEntity(id, a);
            shard.TouchLastMessageTimestamp(id);
            shard.EntityCreated(id);
            return a;
        }

        private static int EntityCreated<TShard>(this TShard shard, EntityId id) where TShard : IShard
        {
            return shard.Entities.NrActiveEntities;
        }

        internal static int TotalBufferSize<TShard>(this TShard shard) where TShard : IShard =>
            shard.MessageBuffers.Aggregate(0, (sum, entity) => sum + entity.Value.Count);

        #endregion

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
            return Actor.Props.Create(() =>
                new Shard(
                    typeName,
                    shardId,
                    entityProps,
                    settings,
                    extractEntityId,
                    extractShardId,
                    handOffStopMessage,
                    rememberEntitiesProvider)).WithDeploy(Deploy.Local);
        }



        // Become extensions
        internal static void Become(this IShard shard, Shard.ReceiveState receiveState)
        {
            shard.Context.Become(receiveState.Receive);
        }
    }
}
