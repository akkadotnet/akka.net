//-----------------------------------------------------------------------
// <copyright file="ShardRegion.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding
{
    using EntityId = String;
    using Msg = Object;
    using ShardId = String;

    /// <summary>
    /// This actor creates children shard actors on demand that it is told to be responsible for.
    /// The shard actors in turn create entity actors on demand.
    /// It delegates messages targeted to other shards to the responsible
    /// <see cref="ShardRegion"/> actor on other nodes.
    /// </summary>
    public sealed class ShardRegion : ActorBase, IWithTimers
    {
        #region messages

        /// <summary>
        /// Periodic tick to run some house-keeping.
        /// This message is continuously sent to `self` using a timer configured with `retryInterval`.
        /// </summary>
        [Serializable]
        internal sealed class Retry : IShardRegionCommand, INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Retry Instance = new Retry();
            private Retry() { }
        }

        /// <summary>
        /// Similar to <see cref="Retry"/> but used only when <see cref="ShardRegion"/> is starting and when we detect that
        /// the coordinator is moving.
        ///
        /// This is to ensure that a <see cref="ShardRegion"/> can register as soon as possible while the
        /// <see cref="ShardCoordinator"/> is in the process of recovering its state.
        ///
        /// This message is sent to `Self` using a interval lower then <see cref="Retry"/> (higher frequency).
        /// The interval increases exponentially until it equals <see cref="_retryInterval"/> in which case
        /// we stop to schedule it and let <see cref="Retry"/> take over.
        ///
        /// </summary>
        internal sealed class RegisterRetry : IShardRegionCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly RegisterRetry Instance = new RegisterRetry();
            private RegisterRetry() { }
        }

        /// <summary>
        /// When an remembering entities and the shard stops unexpected (e.g. persist failure), we
        /// restart it after a back off using this message.
        /// </summary>
        [Serializable]
        internal sealed class RestartShard
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId ShardId;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardId">TBD</param>
            public RestartShard(ShardId shardId)
            {
                ShardId = shardId;
            }
        }

        /// <summary>
        /// When remembering entities and a shard is started, each entity id that needs to
        /// be running will trigger this message being sent through sharding. For this to work
        /// the message *must* be handled by the shard id extractor.
        /// </summary>
        [Serializable]
        public sealed class StartEntity : IClusterShardingSerializable, IEquatable<StartEntity>
        {
            /// <summary>
            /// An identifier of an entity to be started. Unique in scope of a given shard.
            /// </summary>
            public readonly EntityId EntityId;

            /// <summary>
            /// Creates a new instance of a <see cref="StartEntity"/> class, used for requesting
            /// to start an entity with provided <paramref name="entityId"/>.
            /// </summary>
            /// <param name="entityId">An identifier of an entity to be started on a given shard.</param>
            public StartEntity(EntityId entityId)
            {
                EntityId = entityId;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as StartEntity);
            }

            public bool Equals(StartEntity other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return EntityId.Equals(other.EntityId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return EntityId.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"StartEntity({EntityId})";

            #endregion
        }

        /// <summary>
        /// Sent back when a <see cref="StartEntity"/> message was received and triggered the entity
        /// to start(it does not guarantee the entity successfully started)
        /// </summary>
        [Serializable]
        internal sealed class StartEntityAck : IClusterShardingSerializable, IDeadLetterSuppression, IEquatable<StartEntityAck>
        {
            /// <summary>
            /// An identifier of a newly started entity. Unique in scope of a given shard.
            /// </summary>
            public readonly EntityId EntityId;

            /// <summary>
            /// An identifier of a shard, on which an entity identified by <see cref="EntityId"/> is hosted.
            /// </summary>
            public readonly ShardId ShardId;

            /// <summary>
            /// Creates a new instance of a <see cref="StartEntityAck"/> class, used to confirm that
            /// <see cref="StartEntity"/> request has succeed.
            /// </summary>
            /// <param name="entityId">An identifier of a newly started entity.</param>
            /// <param name="shardId">An identifier of a shard hosting started entity.</param>
            public StartEntityAck(EntityId entityId, ShardId shardId)
            {
                EntityId = entityId;
                ShardId = shardId;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as StartEntityAck);
            }

            public bool Equals(StartEntityAck other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return EntityId.Equals(other.EntityId)
                    && ShardId.Equals(other.ShardId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = EntityId.GetHashCode();
                    hashCode = (hashCode * 397) ^ ShardId.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"StartEntityAck(entityId:{EntityId}, shardId:{ShardId})";

            #endregion
        }

        #endregion

        /// <summary>
        /// INTERNAL API. Sends stopMessage (e.g. <see cref="PoisonPill"/>) to the entities and when all of
        /// them have terminated it replies with <see cref="ShardCoordinator.ShardStopped"/>.
        /// If the entities don't terminate after `handoffTimeout` it will try stopping them forcefully.
        /// </summary>
        internal class HandOffStopper : ReceiveActor, IWithTimers
        {
            private sealed class StopTimeout
            {
                public static readonly StopTimeout Instance = new StopTimeout();

                private StopTimeout()
                {
                }
            }

            private sealed class StopTimeoutWarning
            {
                public static readonly StopTimeoutWarning Instance = new StopTimeoutWarning();

                private StopTimeoutWarning()
                {
                }
            }

            private static readonly TimeSpan StopTimeoutWarningAfter = TimeSpan.FromSeconds(5);

            private ILoggingAdapter _log;
            /// <summary>
            /// TBD
            /// </summary>
            public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

            public ITimerScheduler Timers { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="shard">TBD</param>
            /// <param name="replyTo">TBD</param>
            /// <param name="entities">TBD</param>
            /// <param name="stopMessage">TBD</param>
            /// <param name="handoffTimeout">TBD</param>
            /// <returns>TBD</returns>
            public static Props Props(
                string typeName,
                ShardId shard,
                IActorRef replyTo,
                IImmutableSet<IActorRef> entities,
                object stopMessage,
                TimeSpan handoffTimeout)
            {
                return Actor.Props.Create(() => new HandOffStopper(typeName, shard, replyTo, entities, stopMessage, handoffTimeout))
                    .WithDeploy(Deploy.Local);
            }

            /// <summary>
            ///Sends stopMessage (e.g. `PoisonPill`) to the entities and when all of
            /// them have terminated it replies with `ShardStopped`.
            /// If the entities don't terminate after `handoffTimeout` it will try stopping them forcefully.
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="shard">TBD</param>
            /// <param name="replyTo">TBD</param>
            /// <param name="entities">TBD</param>
            /// <param name="stopMessage">TBD</param>
            /// <param name="handoffTimeout">TBD</param>
            public HandOffStopper(
                string typeName,
                ShardId shard,
                IActorRef replyTo,
                IImmutableSet<IActorRef> entities,
                object stopMessage,
                TimeSpan handoffTimeout)
            {
                var remaining = new HashSet<IActorRef>(entities);

                Receive<Terminated>(t =>
                {
                    remaining.Remove(t.ActorRef);
                    if (remaining.Count == 0)
                    {
                        replyTo.Tell(new ShardCoordinator.ShardStopped(shard));
                        Context.Stop(Self);
                    }
                });
                Receive<StopTimeoutWarning>(s =>
                {
                    Log.Warning(
                        $"{{0}}: [{remaining.Count}] of the entities in shard [{{1}}] not stopped after [{{2}}]. " +
                        "Maybe the handOffStopMessage [{3}] is not handled? {4}",
                        typeName,
                        shard,
                        StopTimeoutWarningAfter,
                        stopMessage.GetType(),
                        (CoordinatedShutdown.Get(Context.System).ShutdownReason != null) ?
                            "" // the region will be shutdown earlier so would be confusing to say more
                            : $"Waiting additional [{handoffTimeout}] before stopping the remaining entities.");
                });
                Receive<StopTimeout>(s =>
                {
                    Log.Warning("{0}: HandOffStopMessage[{1}] is not handled by some of the entities in shard [{2}] after [{3}], " +
                        "stopping the remaining [{4}] entities.",
                        typeName, stopMessage.GetType().Name, shard, handoffTimeout, remaining.Count);

                    foreach (var r in remaining)
                        Context.Stop(r);
                });

                Timers.StartSingleTimer(StopTimeoutWarning.Instance, StopTimeoutWarning.Instance, StopTimeoutWarningAfter);
                Timers.StartSingleTimer(StopTimeout.Instance, StopTimeout.Instance, handoffTimeout);

                foreach (var aref in entities)
                {
                    Context.Watch(aref);
                    aref.Tell(stopMessage);
                }
            }
        }

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="ShardRegion"/> actor.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="entityProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="coordinatorPath">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        /// <param name="rememberEntitiesProvider">TBD</param>
        /// <returns>TBD</returns>
        internal static Props Props(
            string typeName,
            Func<string, Props> entityProps,
            ClusterShardingSettings settings,
            string coordinatorPath,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage,
            IRememberEntitiesProvider rememberEntitiesProvider)
        {
            return Actor.Props.Create(() => new ShardRegion(
                typeName,
                entityProps,
                settings,
                coordinatorPath,
                extractEntityId,
                extractShardId,
                handOffStopMessage,
                rememberEntitiesProvider))
                .WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="ShardRegion"/> actor when used in proxy only mode.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="coordinatorPath">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <returns>TBD</returns>
        internal static Props ProxyProps(
            string typeName,
            ClusterShardingSettings settings,
            string coordinatorPath,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return Actor.Props.Create(() => new ShardRegion(
                typeName,
                null,
                settings,
                coordinatorPath,
                extractEntityId,
                extractShardId,
                PoisonPill.Instance,
                null))
                .WithDeploy(Deploy.Local);
        }

        private readonly string _typeName;
        private readonly Func<string, Props> _entityProps;
        private readonly ClusterShardingSettings _settings;
        private readonly string _coordinatorPath;
        private readonly ExtractEntityId _extractEntityId;
        private readonly ExtractShardId _extractShardId;
        private readonly object _handOffStopMessage;

        private readonly IRememberEntitiesProvider _rememberEntitiesProvider;
        private readonly bool _verboseDebug;
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private IImmutableSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering);

        // membersByAge contains members with these status
        private static readonly ImmutableHashSet<MemberStatus> MemberStatusOfInterest = ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Exiting);

        private IImmutableDictionary<IActorRef, IImmutableSet<ShardId>> _regions = ImmutableDictionary<IActorRef, IImmutableSet<ShardId>>.Empty;
        private IImmutableDictionary<ShardId, IActorRef> _regionByShard = ImmutableDictionary<ShardId, IActorRef>.Empty;
        private MessageBufferMap<ShardId> _shardBuffers = new MessageBufferMap<ShardId>();
        private IImmutableDictionary<ShardId, IActorRef> _shards = ImmutableDictionary<ShardId, IActorRef>.Empty;
        private IImmutableDictionary<IActorRef, ShardId> _shardsByRef = ImmutableDictionary<IActorRef, ShardId>.Empty;
        private IImmutableSet<ShardId> _startingShards = ImmutableHashSet<ShardId>.Empty;
        private IImmutableSet<IActorRef> _handingOff = ImmutableHashSet<IActorRef>.Empty;

        private IActorRef _coordinator;
        private int _retryCount;
        private readonly TimeSpan _retryInterval;
        private readonly TimeSpan _initRegistrationDelay;
        private TimeSpan _nextRegistrationDelay;
        private bool _loggedFullBufferWarning;
        private const int RetryCountThreshold = 5;
        private bool _gracefulShutdownInProgress;

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);
        private readonly TaskCompletionSource<Done> _gracefulShutdownProgress = new TaskCompletionSource<Done>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="entityProps">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="coordinatorPath">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="handOffStopMessage">TBD</param>
        /// <param name="rememberEntitiesProvider">TBD</param>
        public ShardRegion(
            string typeName,
            Func<string, Props> entityProps,
            ClusterShardingSettings settings,
            string coordinatorPath,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            object handOffStopMessage,
            IRememberEntitiesProvider rememberEntitiesProvider)
        {
            _typeName = typeName;
            _entityProps = entityProps;
            _settings = settings;
            _coordinatorPath = coordinatorPath;
            _extractEntityId = extractEntityId;
            _extractShardId = extractShardId;
            _handOffStopMessage = handOffStopMessage;
            _rememberEntitiesProvider = rememberEntitiesProvider;

            _verboseDebug = Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging");

            _retryInterval = _settings.TuningParameters.RetryInterval;
            _initRegistrationDelay = TimeSpan.FromMilliseconds(100).Max(new TimeSpan(_retryInterval.Ticks / 2 / 2 / 2));
            _nextRegistrationDelay = _initRegistrationDelay;

            SetupCoordinatedShutdown();
        }

        private void SetupCoordinatedShutdown()
        {
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterShardingShutdownRegion, "region-shutdown", () =>
            {
                if (_cluster.IsTerminated || _cluster.SelfMember.Status == MemberStatus.Down)
                {
                    return Task.FromResult(Done.Instance);
                }
                else
                {
                    self.Tell(GracefulShutdown.Instance);
                    return _gracefulShutdownProgress.Task;
                }
            });
        }

        public ITimerScheduler Timers { get; set; }

        /// <summary>
        /// When leaving the coordinator singleton is started rather quickly on next
        /// oldest node and therefore it is good to send the Register and GracefulShutdownReq to
        /// the likely locations of the coordinator.
        /// </summary>
        /// <returns></returns>
        private List<ActorSelection> CoordinatorSelection
        {
            get
            {
                IEnumerable<Member> SelectMembers()
                {
                    foreach (var m in _membersByAge)
                    {
                        yield return m;
                        if (m.Status == MemberStatus.Up)
                            break;
                    }
                }

                return SelectMembers()
                    .Select(m => Context.ActorSelection(new RootActorPath(m.Address) + _coordinatorPath)).ToList();
            }
        }

        private object RegistrationMessage
        {
            get
            {
                if (_entityProps != null)
                    return new ShardCoordinator.Register(Self);
                return new ShardCoordinator.RegisterProxy(Self);
            }
        }

        /// <inheritdoc cref="ActorBase.PreStart"/>
        /// <summary>
        /// Subscribe to MemberEvent, re-subscribe when restart
        /// </summary>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            Timers.StartPeriodicTimer(Retry.Instance, Retry.Instance, _retryInterval);
            StartRegistration();
            LogPassivateIdleEntities();
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
            _gracefulShutdownProgress.TrySetResult(Done.Instance);
        }

        private void LogPassivateIdleEntities()
        {
            if (_settings.ShouldPassivateIdleEntities)
                _log.Info("{0}: Idle entities will be passivated after [{1}]",
                    _typeName,
                    _settings.PassivateIdleEntityAfter);

            if (_settings.RememberEntities)
                _log.Debug("{0}: Idle entities will not be passivated because 'rememberEntities' is enabled.", _typeName);
        }

        private bool MatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_settings.Role) || member.HasRole(_settings.Role);
        }

        private void ChangeMembers(IImmutableSet<Member> newMembers)
        {
            var before = _membersByAge.FirstOrDefault();
            var after = newMembers.FirstOrDefault();
            _membersByAge = newMembers;
            if (!Equals(before, after))
            {
                if (_log.IsDebugEnabled)
                    _log.Debug("{0}: Coordinator moved from [{1}] to [{2}]",
                        _typeName,
                        before?.Address.ToString() ?? string.Empty,
                        after?.Address.ToString() ?? string.Empty);

                _coordinator = null;
                StartRegistration();
            }
        }

        /// <inheritdoc cref="ActorBase.Receive"/>
        protected override bool Receive(object message)
        {
            switch (message)
            {

                case Terminated t:
                    HandleTerminated(t);
                    return true;
                case ShardInitialized si:
                    InitializeShard(si.ShardId, Sender);
                    return true;
                case ClusterEvent.IClusterDomainEvent cde:
                    HandleClusterEvent(cde);
                    return true;
                case ClusterEvent.CurrentClusterState ccs:
                    HandleClusterState(ccs);
                    return true;
                case ShardCoordinator.ICoordinatorMessage cm:
                    HandleCoordinatorMessage(cm);
                    return true;
                case IShardRegionCommand src:
                    HandleShardRegionCommand(src);
                    return true;
                case IShardRegionQuery srq:
                    HandleShardRegionQuery(srq);
                    return true;
                case RestartShard _:
                    DeliverMessage(message, Sender);
                    return true;
                case StartEntity _:
                    DeliverStartEntity(message, Sender);
                    return true;
                case var _ when _extractEntityId(message).HasValue:
                    DeliverMessage(message, Sender);
                    return true;
                default:
                    _log.Warning("{0}: Message does not have an extractor defined in shard so it was ignored: {1}", _typeName, message);
                    return false;
            }
        }

        private void InitializeShard(ShardId id, IActorRef shardRef)
        {
            _log.Debug("{0}: Shard was initialized [{1}]", _typeName, id);
            _startingShards = _startingShards.Remove(id);
            DeliverBufferedMessages(id, shardRef);
        }

        private void StartRegistration()
        {
            _nextRegistrationDelay = _initRegistrationDelay;

            Register();
            ScheduleNextRegistration();
        }

        private void ScheduleNextRegistration()
        {
            if (_nextRegistrationDelay < _retryInterval)
            {
                Timers.StartSingleTimer(RegisterRetry.Instance, RegisterRetry.Instance, _nextRegistrationDelay);
                // exponentially increasing retry interval until reaching the normal retryInterval
                _nextRegistrationDelay += _nextRegistrationDelay;
            }
        }

        private void FinishRegistration()
        {
            Timers.Cancel(RegisterRetry.Instance);
        }

        private void Register()
        {
            var actorSelections = CoordinatorSelection;
            foreach (var coordinator in actorSelections)
                coordinator.Tell(RegistrationMessage);

            if (_shardBuffers.NonEmpty && _retryCount >= RetryCountThreshold)
            {
                if (actorSelections.Count > 0)
                {
                    var coordinatorMessage = _cluster.State.Unreachable.Contains(_membersByAge.First())
                        ? $"Coordinator [{_membersByAge.First()}] is unreachable."
                        : $"Coordinator [{_membersByAge.First()}] is reachable.";

                    var bufferSize = _shardBuffers.TotalCount;
                    if (bufferSize > 0)
                    {
                        if (_log.IsWarningEnabled)
                        {
                            _log.Warning(
                                "{0}: Trying to register to coordinator at [{1}], but no acknowledgement. Total [{2}] buffered messages. [{3}]",
                                _typeName,
                                string.Join(", ", actorSelections.Select(i => i.PathString)),
                                bufferSize,
                                coordinatorMessage);
                        }
                    }
                    else if (_log.IsDebugEnabled)
                    {
                        _log.Debug(
                          "{0}: Trying to register to coordinator at [{1}], but no acknowledgement. No buffered messages yet. [{2}]",
                          _typeName,
                          string.Join(", ", actorSelections.Select(i => i.PathString)),
                          coordinatorMessage);
                    }
                }
                else
                {
                    // Members start off as "Removed"
                    var partOfCluster = _cluster.SelfMember.Status != MemberStatus.Removed;
                    var possibleReason = partOfCluster
                        ? "Has Cluster Sharding been started on every node and nodes been configured with the correct role(s)?"
                        : "Probably, no seed-nodes configured and manual cluster join not performed?";

                    var bufferSize = _shardBuffers.TotalCount;
                    if (bufferSize > 0)
                    {
                        _log.Warning("{0}: No coordinator found to register. {1} Total [{2}] buffered messages.",
                            _typeName,
                            possibleReason,
                            bufferSize);
                    }
                    else
                    {
                        _log.Debug("{0}: No coordinator found to register. {1} No buffered messages yet.",
                            _typeName, possibleReason);
                    }
                }
            }
        }

        private void DeliverStartEntity(object message, IActorRef sender)
        {
            try
            {
                DeliverMessage(message, sender);
            }
            catch (Exception ex)
            {
                //case ex: MatchError ⇒
                _log.Error(ex, "{0}: When using remember-entities the shard id extractor must handle ShardRegion.StartEntity(id).", _typeName);
            }
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            if (message is RestartShard restart)
            {
                var shardId = restart.ShardId;
                if (_regionByShard.TryGetValue(shardId, out var regionRef))
                {
                    if (Self.Equals(regionRef))
                        GetShard(shardId);
                }
                else
                {
                    if (!_shardBuffers.Contains(shardId))
                    {
                        _log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", _typeName, shardId, _coordinator);
                        _coordinator?.Tell(new ShardCoordinator.GetShardHome(shardId));
                    }
                    var buffer = _shardBuffers.GetOrEmpty(shardId);

                    _log.Debug("{0}: Buffer message for shard [{1}]. Total [{2}] buffered messages.",
                        _typeName,
                        shardId,
                        buffer.Count + 1);
                    _shardBuffers.Append(shardId, message, sender);
                }
            }
            else
            {
                var shardId = _extractShardId(message);
                if (_regionByShard.TryGetValue(shardId, out var region))
                {
                    if (region.Equals(Self))
                    {
                        var sref = GetShard(shardId);
                        if (Equals(sref, ActorRefs.Nobody))
                            BufferMessage(shardId, message, sender);
                        else
                        {
                            if (_shardBuffers.Contains(shardId))
                            {
                                // Since now messages to a shard is buffered then those messages must be in right order
                                BufferMessage(shardId, message, sender);
                                DeliverBufferedMessages(shardId, sref);
                            }
                            else
                                sref.Tell(message, sender);
                        }
                    }
                    else
                    {
                        if (_verboseDebug)
                            _log.Debug("{0}: Forwarding request for shard [{1}] to [{2}]", _typeName, shardId, region);
                        region.Tell(message, sender);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(shardId))
                    {
                        _log.Warning("{0}: Shard must not be empty, dropping message [{1}]", _typeName, message.GetType().Name);
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        if (!_shardBuffers.Contains(shardId))
                        {
                            _log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", _typeName, shardId, _coordinator);
                            _coordinator?.Tell(new ShardCoordinator.GetShardHome(shardId));
                        }

                        BufferMessage(shardId, message, sender);
                    }
                }
            }
        }

        private void BufferMessage(ShardId shardId, Msg message, IActorRef sender)
        {
            var totalBufferSize = _shardBuffers.TotalCount;
            if (totalBufferSize >= _settings.TuningParameters.BufferSize)
            {
                if (_loggedFullBufferWarning)
                    _log.Debug("{0}: Buffer is full, dropping message for shard [{1}]", _typeName, shardId);
                else
                {
                    _log.Warning("{0}: Buffer is full, dropping message for shard [{1}]", _typeName, shardId);
                    _loggedFullBufferWarning = true;
                }

                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                _shardBuffers.Append(shardId, message, sender);

                // log some insight to how buffers are filled up every 10% of the buffer capacity
                var total = totalBufferSize + 1;
                var bufferSize = _settings.TuningParameters.BufferSize;
                if (total % (bufferSize / 10) == 0)
                {
                    const string logMsg = "{0}: ShardRegion is using [{1} %] of its buffer capacity.";
                    if (total > bufferSize / 2)
                        _log.Warning(logMsg + " The coordinator might not be available. You might want to check cluster membership status.", _typeName, 100 * total / bufferSize);
                    else
                        _log.Info(logMsg, _typeName, 100 * total / bufferSize);
                }
            }
        }

        private void HandleShardRegionCommand(IShardRegionCommand command)
        {
            switch (command)
            {
                case Retry _:
                    // retryCount is used to avoid flooding the logs
                    // it's used inside register() whenever shardBuffers.nonEmpty
                    // therefore we update it if needed on each Retry msg
                    // the reason why it's updated here is because we don't want to increase it on each RegisterRetry, only on Retry
                    if (_shardBuffers.NonEmpty) _retryCount++;

                    // we depend on the coordinator each time, if empty we need to register
                    // otherwise we can try to deliver some buffered messages
                    if (_coordinator == null) Register();
                    else
                    {
                        // Note: we do try to deliver buffered messages even in the middle of
                        // a graceful shutdown every message that we manage to deliver is a win
                        TryRequestShardBufferHomes();
                    }

                    // eventually, also re-trigger a graceful shutdown if one is in progress
                    SendGracefulShutdownToCoordinatorIfInProgress();
                    TryCompleteGracefulShutdownIfInProgress();

                    break;

                case RegisterRetry _:
                    if (_coordinator == null)
                    {
                        Register();
                        ScheduleNextRegistration();
                    }
                    break;

                case GracefulShutdown _:
                    _log.Debug("{0}: Starting graceful shutdown of region and all its shards", _typeName);

                    var coordShutdown = CoordinatedShutdown.Get(Context.System);
                    if (coordShutdown.ShutdownReason != null)
                    {
                        // use a shorter timeout than the coordinated shutdown phase to be able to log better reason for the timeout
                        var timeout = coordShutdown.Timeout(CoordinatedShutdown.PhaseClusterShardingShutdownRegion) - TimeSpan.FromSeconds(1);
                        if (timeout > TimeSpan.Zero)
                        {
                            Timers.StartSingleTimer(GracefulShutdownTimeout.Instance, GracefulShutdownTimeout.Instance, timeout);
                        }
                    }

                    _gracefulShutdownInProgress = true;
                    SendGracefulShutdownToCoordinatorIfInProgress();
                    TryCompleteGracefulShutdownIfInProgress();
                    break;

                case GracefulShutdownTimeout _:
                    _log.Warning(
                        "{0}: Graceful shutdown of shard region timed out, region will be stopped. Remaining shards [{1}], " +
                        "remaining buffered messages [{2}].",
                        _typeName,
                        string.Join(", ", _shards.Keys),
                        _shardBuffers.TotalCount);
                    Context.Stop(Self);
                    break;

                default:
                    Unhandled(command);
                    break;
            }
        }

        private void HandleShardRegionQuery(IShardRegionQuery query)
        {
            switch (query)
            {
                case GetCurrentRegions _:
                    if (_coordinator != null) _coordinator.Forward(query);
                    else Sender.Tell(new CurrentRegions(ImmutableHashSet<Address>.Empty));
                    break;
                case GetClusterShardingStats _:
                    if (_coordinator != null)
                        _coordinator.Forward(query);
                    else
                        Sender.Tell(new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty));
                    break;

                case GetShardRegionState _:
                    ReplyToRegionStateQuery(Sender);
                    break;
                case GetShardRegionStats _:
                    ReplyToRegionStatsQuery(Sender);
                    break;
                case GetShardRegionStatus _:
                    Sender.Tell(new ShardRegionStatus(_typeName, _coordinator != null));
                    break;
                default:
                    Unhandled(query);
                    break;
            }
        }

        private void ReplyToRegionStateQuery(IActorRef sender)
        {
            QueryShardsAsync<Shard.CurrentShardState>(Shard.GetCurrentShardState.Instance)
                .ContinueWith(qr =>
                {
                    return new CurrentShardRegionState(
                        qr.Result.Responses.Select(state => new ShardState(state.ShardId, state.EntityIds)).ToImmutableHashSet(),
                        qr.Result.Failed);
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(sender);
        }

        private void ReplyToRegionStatsQuery(IActorRef sender)
        {
            QueryShardsAsync<Shard.ShardStats>(Shard.GetShardStats.Instance)
                .ContinueWith(qr =>
                {
                    return new ShardRegionStats(
                        qr.Result.Responses.ToImmutableDictionary(stats => stats.ShardId, stats => stats.EntityCount),
                        qr.Result.Failed);
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(sender);
        }

        /// <summary>
        /// Query all or a subset of shards, e.g. unresponsive shards that initially timed out.
        /// If the number of `shards` are less than this.shards.size, this could be a retry.
        /// Returns a partitioned set of any shards that may have not replied within the
        /// timeout and shards that did reply, to provide retry on only that subset.
        ///
        /// Logs a warning if any of the group timed out.
        ///
        /// To check subset unresponsive: {{{ queryShards[T](shards.filterKeys(u.contains), shardQuery) }}}
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        private Task<ShardsQueryResult<T>> QueryShardsAsync<T>(object message)
        {
            var timeout = _settings.ShardRegionQueryTimeout;
            if (timeout == TimeSpan.Zero)
            {
                //simulate timeout
                return Task.FromResult(new ShardsQueryResult<T>(_shards.Keys.ToImmutableHashSet(), ImmutableList<T>.Empty, _shards.Count, timeout));
            }

            var tasks = _shards.Select(entity => (Entity: entity.Key, Task: entity.Value.Ask<T>(message, timeout))).ToImmutableList();
            return Task.WhenAll(tasks.Select(i => i.Task)).ContinueWith(ps =>
            {
                var qr = ShardsQueryResult<T>.Create(tasks, _shards.Count, timeout);
                if (qr.Failed.Count > 0)
                    _log.Warning($"{0}: {qr}", _typeName);
                return qr;
            });
        }

        private void TryCompleteGracefulShutdownIfInProgress()
        {
            if (_gracefulShutdownInProgress && _shards.Count == 0 && _shardBuffers.IsEmpty)
            {
                _log.Debug("{0}: Completed graceful shutdown of region.", _typeName);
                Context.Stop(Self);     // all shards have been rebalanced, complete graceful shutdown
            }
        }

        private void SendGracefulShutdownToCoordinatorIfInProgress()
        {
            if (_gracefulShutdownInProgress)
            {
                var actorSelections = CoordinatorSelection;
                _log.Debug("{0}: Sending graceful shutdown to [{1}]", _typeName, string.Join(", ", actorSelections.Select(i => $"({i})")));
                actorSelections.ForEach(c => c.Tell(new ShardCoordinator.GracefulShutdownRequest(Self)));
            }
        }

        private void HandleCoordinatorMessage(ShardCoordinator.ICoordinatorMessage message)
        {
            switch (message)
            {
                case ShardCoordinator.HostShard hs:
                    {
                        if (_gracefulShutdownInProgress)
                        {
                            _log.Debug("{0}: Ignoring Host Shard request for [{1}] as region is shutting down", _typeName, hs.Shard);

                            // if the coordinator is sending HostShard to a region that is shutting down
                            // it means that it missed the shutting down message (coordinator moved?)
                            // we want to inform it as soon as possible so it doesn't keep trying to allocate the shard here
                            SendGracefulShutdownToCoordinatorIfInProgress();
                        }
                        else
                        {
                            var shard = hs.Shard;
                            _log.Debug("{0}: Host shard [{1}]", _typeName, shard);
                            _regionByShard = _regionByShard.SetItem(shard, Self);
                            UpdateRegionShards(Self, shard);

                            // Start the shard, if already started this does nothing
                            GetShard(shard);

                            Sender.Tell(new ShardCoordinator.ShardStarted(shard));
                        }
                    }
                    break;
                case ShardCoordinator.ShardHome home:
                    _log.Debug("{0}: Shard [{1}] located at [{2}]", _typeName, home.Shard, home.Ref);

                    if (_regionByShard.TryGetValue(home.Shard, out var region))
                    {
                        if (region.Equals(Self) && !home.Ref.Equals(Self))
                        {
                            // should not happen, inconsistency between ShardRegion and ShardCoordinator
                            throw new IllegalStateException($"{_typeName}: Unexpected change of shard [{home.Shard}] from self to [{home.Ref}]");
                        }
                    }

                    _regionByShard = _regionByShard.SetItem(home.Shard, home.Ref);
                    UpdateRegionShards(home.Ref, home.Shard);

                    if (!home.Ref.Equals(Self))
                        Context.Watch(home.Ref);

                    if (home.Ref.Equals(Self))
                    {
                        var shardRef = GetShard(home.Shard);
                        if (!Equals(shardRef, ActorRefs.Nobody))
                            DeliverBufferedMessages(home.Shard, shardRef);
                    }
                    else
                        DeliverBufferedMessages(home.Shard, home.Ref);
                    break;
                case ShardCoordinator.RegisterAck ra:
                    _coordinator = ra.Coordinator;
                    Context.Watch(_coordinator);
                    FinishRegistration();
                    TryRequestShardBufferHomes();
                    break;
                case ShardCoordinator.BeginHandOff bho:
                    {
                        var shard = bho.Shard;
                        _log.Debug("{0}: BeginHandOff shard [{1}]", _typeName, shard);
                        if (_regionByShard.TryGetValue(shard, out var regionRef))
                        {
                            if (!_regions.TryGetValue(regionRef, out var updatedShards))
                                updatedShards = ImmutableHashSet<ShardId>.Empty;

                            updatedShards = updatedShards.Remove(shard);

                            _regions = updatedShards.Count == 0
                                ? _regions.Remove(regionRef)
                                : _regions.SetItem(regionRef, updatedShards);

                            _regionByShard = _regionByShard.Remove(shard);
                        }

                        Sender.Tell(new ShardCoordinator.BeginHandOffAck(shard));
                    }
                    break;
                case ShardCoordinator.HandOff ho:
                    {
                        var shard = ho.Shard;
                        _log.Debug("{0}: HandOff shard [{1}]", _typeName, shard);

                        // must drop requests that came in between the BeginHandOff and now,
                        // because they might be forwarded from other regions and there
                        // is a risk or message re-ordering otherwise
                        if (_shardBuffers.Contains(shard))
                        {
                            var dropped = _shardBuffers
                                .Drop(shard, "Avoiding reordering of buffered messages at shard handoff", Context.System.DeadLetters);
                            if (dropped > 0)
                                _log.Warning("{0}: Dropping [{1}] buffered messages to shard [{2}] during hand off to avoid re-ordering",
                                    _typeName,
                                    dropped,
                                    shard);
                            _loggedFullBufferWarning = false;
                        }

                        if (_shards.TryGetValue(shard, out var actorRef))
                        {
                            _handingOff = _handingOff.Add(actorRef);
                            actorRef.Forward(message);
                        }
                        else
                            Sender.Tell(new ShardCoordinator.ShardStopped(shard));
                    }
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        private void UpdateRegionShards(IActorRef regionRef, string shard)
        {
            if (!_regions.TryGetValue(regionRef, out var shards))
                shards = ImmutableSortedSet<ShardId>.Empty;
            _regions = _regions.SetItem(regionRef, shards.Add(shard));
        }

        /// <summary>
        /// Send GetShardHome for all shards with buffered messages
        /// If coordinator is empty, nothing happens
        /// </summary>
        private void TryRequestShardBufferHomes()
        {
            if (_coordinator != null)
            {
                foreach (var buffer in _shardBuffers)
                {
                    _log.Debug("{0}: Requesting shard home for [{1}] from coordinator at [{2}]. [{3}] buffered messages.",
                        _typeName,
                        buffer.Key,
                        _coordinator,
                        buffer.Value.Count);

                    _coordinator.Tell(new ShardCoordinator.GetShardHome(buffer.Key));
                }
            }

            if (_retryCount >= RetryCountThreshold && _retryCount % RetryCountThreshold == 0 && _log.IsWarningEnabled)
            {
                _log.Warning("{0}: Requested shard homes [{1}] from coordinator at [{2}]. [{3}] total buffered messages.",
                    _typeName,
                    string.Join(", ", _shardBuffers.Select(i => i.Key).OrderBy(i => i)),
                    _coordinator,
                    _shardBuffers.TotalCount
                    );
            }
        }

        private void DeliverBufferedMessages(ShardId shardId, IActorRef receiver)
        {
            if (_shardBuffers.Contains(shardId))
            {
                var buffer = _shardBuffers.GetOrEmpty(shardId);
                _log.Debug("{0}: Deliver [{1}] buffered messages for shard [{2}]", _typeName, buffer.Count, shardId);

                foreach (var m in buffer)
                {
                    if (m.Message is RestartShard && !receiver.Equals(Self))
                    {
                        _log.Debug("{0}: Dropping buffered message {1}, these are only processed by a local ShardRegion.",
                            _typeName,
                            m.Message);
                    }
                    else
                    {
                        receiver.Tell(m.Message, m.Ref);
                    }
                }

                _shardBuffers.Remove(shardId);
            }

            _loggedFullBufferWarning = false;
            _retryCount = 0;
        }

        private IActorRef GetShard(ShardId id)
        {
            if (_startingShards.Contains(id))
                return ActorRefs.Nobody;

            if (!_shards.TryGetValue(id, out var shard))
            {
                if (_entityProps == null)
                    throw new IllegalStateException("Shard must not be allocated to a proxy only ShardRegion");

                if (_shardsByRef.Values.All(shardId => shardId != id))
                {
                    _log.Debug("{0}: Starting shard [{1}] in region", _typeName, id);

                    var name = Uri.EscapeDataString(id);
                    var shardRef = Context.Watch(Context.ActorOf(Shard.Props(
                        _typeName,
                        id,
                        _entityProps,
                        _settings,
                        _extractEntityId,
                        _extractShardId,
                        _handOffStopMessage,
                        _rememberEntitiesProvider)
                        .WithDispatcher(Context.Props.Dispatcher), name));

                    _shardsByRef = _shardsByRef.SetItem(shardRef, id);
                    _shards = _shards.SetItem(id, shardRef);
                    _startingShards = _startingShards.Add(id);
                    return shardRef;
                }
            }

            return shard ?? ActorRefs.Nobody;
        }

        private void HandleClusterState(ClusterEvent.CurrentClusterState state)
        {
            var members = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering).Union(state.Members.Where(m => MemberStatusOfInterest.Contains(m.Status) && MatchingRole(m)));
            ChangeMembers(members);
        }

        private void HandleClusterEvent(ClusterEvent.IClusterDomainEvent e)
        {
            switch (e)
            {
                case ClusterEvent.MemberUp mu:
                    AddMember(mu.Member);
                    break;
                case ClusterEvent.MemberLeft ml:
                    AddMember(ml.Member);
                    break;
                case ClusterEvent.MemberExited me:
                    AddMember(me.Member);
                    break;

                case ClusterEvent.MemberRemoved mr:
                    {
                        var m = mr.Member;
                        if (m.UniqueAddress == _cluster.SelfUniqueAddress)
                            Context.Stop(Self);
                        else if (MatchingRole(m))
                            ChangeMembers(_membersByAge.Remove(m));
                    }
                    break;

                case ClusterEvent.MemberDowned md:
                    if (md.Member.UniqueAddress == _cluster.SelfUniqueAddress)
                    {
                        _log.Info("{0}: Self downed, stopping ShardRegion [{1}]", _typeName, Self.Path);
                        Context.Stop(Self);
                    }
                    break;
                case ClusterEvent.IMemberEvent _:
                    // these are expected, no need to warn about them
                    break;
                default:
                    Unhandled(e);
                    break;
            }
        }

        private void AddMember(Member m)
        {
            if (MatchingRole(m) && MemberStatusOfInterest.Contains(m.Status))
            {
                // replace, it's possible that the status, or upNumber is changed
                ChangeMembers(_membersByAge.Remove(m).Add(m));
            }
        }

        private void HandleTerminated(Terminated terminated)
        {
            if (_coordinator != null && _coordinator.Equals(terminated.ActorRef))
            {
                _coordinator = null;
                StartRegistration();
            }
            else if (_regions.TryGetValue(terminated.ActorRef, out var shards))
            {
                _regionByShard = _regionByShard.RemoveRange(shards);
                _regions = _regions.Remove(terminated.ActorRef);

                if (_log.IsDebugEnabled)
                    if (_verboseDebug)
                        _log.Debug(
                          "{0}: Region [{1}] terminated with [{2}] shards [{3}]",
                          _typeName,
                          terminated.ActorRef,
                          shards.Count,
                          string.Join(", ", shards));
                    else
                        _log.Debug("{0}: Region [{1}] terminated with [{2}] shards",
                            _typeName, terminated.ActorRef, shards.Count);
            }
            else if (_shardsByRef.TryGetValue(terminated.ActorRef, out var shard))
            {
                _shardsByRef = _shardsByRef.Remove(terminated.ActorRef);
                _shards = _shards.Remove(shard);
                _startingShards = _startingShards.Remove(shard);
                if (_handingOff.Contains(terminated.ActorRef))
                {
                    _handingOff = _handingOff.Remove(terminated.ActorRef);
                    _log.Debug("{0}: Shard [{1}] handoff complete", _typeName, shard);
                }
                else
                {
                    // if persist fails it will stop
                    _log.Debug("{0}: Shard [{1}] terminated while not being handed off", _typeName, shard);
                    if (_settings.RememberEntities)
                        Context.System.Scheduler.ScheduleTellOnce(_settings.TuningParameters.ShardFailureBackoff, Self, new RestartShard(shard), Self);
                }

                // did this shard get removed because the ShardRegion is shutting down?
                // If so, we can try to speed-up the region shutdown. We don't need to wait for the next tick.
                TryCompleteGracefulShutdownIfInProgress();
            }
        }
    }
}
