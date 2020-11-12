//-----------------------------------------------------------------------
// <copyright file="ShardRegion.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                var remaining = entities;

                Receive<Terminated>(t =>
                {
                    remaining = remaining.Remove(t.ActorRef);
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

        private readonly string TypeName;
        private readonly Func<string, Props> EntityProps;
        private readonly ClusterShardingSettings Settings;
        private readonly string CoordinatorPath;
        private readonly ExtractEntityId ExtractEntityId;
        private readonly ExtractShardId ExtractShardId;
        private readonly object HandOffStopMessage;

        private readonly IRememberEntitiesProvider _rememberEntitiesProvider;
        private readonly bool verboseDebug;
        private readonly Cluster Cluster = Cluster.Get(Context.System);
        private readonly ILoggingAdapter log = Context.GetLogger();

        private IImmutableSet<Member> MembersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering);

        // membersByAge contains members with these status
        private static readonly ImmutableHashSet<MemberStatus> MemberStatusOfInterest = ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Exiting);

        private IImmutableDictionary<IActorRef, IImmutableSet<ShardId>> Regions = ImmutableDictionary<IActorRef, IImmutableSet<ShardId>>.Empty;
        private IImmutableDictionary<ShardId, IActorRef> RegionByShard = ImmutableDictionary<ShardId, IActorRef>.Empty;
        private MessageBufferMap<ShardId> shardBuffers = new MessageBufferMap<ShardId>();
        private IImmutableDictionary<ShardId, IActorRef> Shards = ImmutableDictionary<ShardId, IActorRef>.Empty;
        private IImmutableDictionary<IActorRef, ShardId> ShardsByRef = ImmutableDictionary<IActorRef, ShardId>.Empty;
        private IImmutableSet<ShardId> StartingShards = ImmutableHashSet<ShardId>.Empty;
        private IImmutableSet<IActorRef> HandingOff = ImmutableHashSet<IActorRef>.Empty;

        private IActorRef _coordinator;
        private int _retryCount;
        private readonly TimeSpan _retryInterval;
        private readonly TimeSpan _initRegistrationDelay;
        private TimeSpan _nextRegistrationDelay;
        private bool _loggedFullBufferWarning;
        private const int RetryCountThreshold = 5;
        private bool gracefulShutdownInProgress;

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
            TypeName = typeName;
            EntityProps = entityProps;
            Settings = settings;
            CoordinatorPath = coordinatorPath;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;
            _rememberEntitiesProvider = rememberEntitiesProvider;

            verboseDebug = Context.System.Settings.Config.GetBoolean("akka.cluster.sharding.verbose-debug-logging");

            _retryInterval = Settings.TuningParameters.RetryInterval;
            _initRegistrationDelay = TimeSpan.FromMilliseconds(100).Max(new TimeSpan(_retryInterval.Ticks / 2 / 2 / 2));
            _nextRegistrationDelay = _initRegistrationDelay;

            SetupCoordinatedShutdown();
        }

        private void SetupCoordinatedShutdown()
        {
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterShardingShutdownRegion, "region-shutdown", () =>
            {
                if (Cluster.IsTerminated || Cluster.SelfMember.Status == MemberStatus.Down)
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
                    foreach (var m in MembersByAge)
                    {
                        yield return m;
                        if (m.Status == MemberStatus.Up)
                            break;
                    }
                }

                return SelectMembers()
                    .Select(m => Context.ActorSelection(new RootActorPath(m.Address) + CoordinatorPath)).ToList();
            }
        }

        private object RegistrationMessage
        {
            get
            {
                if (EntityProps != null)
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
            Cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            Timers.StartPeriodicTimer(Retry.Instance, Retry.Instance, _retryInterval);
            StartRegistration();
            LogPassivateIdleEntities();
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            base.PostStop();
            Cluster.Unsubscribe(Self);
            _gracefulShutdownProgress.TrySetResult(Done.Instance);
        }

        private void LogPassivateIdleEntities()
        {
            if (Settings.ShouldPassivateIdleEntities)
                log.Info("{0}: Idle entities will be passivated after [{1}]",
                    TypeName,
                    Settings.PassivateIdleEntityAfter);

            if (Settings.RememberEntities)
                log.Debug("{0}: Idle entities will not be passivated because 'rememberEntities' is enabled.", TypeName);
        }

        private bool MatchingRole(Member member)
        {
            return string.IsNullOrEmpty(Settings.Role) || member.HasRole(Settings.Role);
        }

        private void ChangeMembers(IImmutableSet<Member> newMembers)
        {
            var before = MembersByAge.FirstOrDefault();
            var after = newMembers.FirstOrDefault();
            MembersByAge = newMembers;
            if (!Equals(before, after))
            {
                if (log.IsDebugEnabled)
                    log.Debug("{0}: Coordinator moved from [{1}] to [{2}]",
                        TypeName,
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
                case var _ when ExtractEntityId(message).HasValue:
                    DeliverMessage(message, Sender);
                    return true;
                default:
                    log.Warning("{0}: Message does not have an extractor defined in shard so it was ignored: {1}", TypeName, message);
                    return false;
            }
        }

        private void InitializeShard(ShardId id, IActorRef shardRef)
        {
            log.Debug("{0}: Shard was initialized [{1}]", TypeName, id);
            StartingShards = StartingShards.Remove(id);
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

            if (shardBuffers.NonEmpty && _retryCount >= RetryCountThreshold)
            {
                if (actorSelections.Count > 0)
                {
                    var coordinatorMessage = Cluster.State.Unreachable.Contains(MembersByAge.First())
                        ? $"Coordinator [{MembersByAge.First()}] is unreachable."
                        : $"Coordinator [{MembersByAge.First()}] is reachable.";

                    var bufferSize = shardBuffers.TotalCount;
                    if (bufferSize > 0)
                    {
                        if (log.IsWarningEnabled)
                        {
                            log.Warning(
                                "{0}: Trying to register to coordinator at [{1}], but no acknowledgement. Total [{2}] buffered messages. [{3}]",
                                TypeName,
                                string.Join(", ", actorSelections.Select(i => i.PathString)),
                                bufferSize,
                                coordinatorMessage);
                        }
                    }
                    else if (log.IsDebugEnabled)
                    {
                        log.Debug(
                          "{0}: Trying to register to coordinator at [{1}], but no acknowledgement. No buffered messages yet. [{2}]",
                          TypeName,
                          string.Join(", ", actorSelections.Select(i => i.PathString)),
                          coordinatorMessage);
                    }
                }
                else
                {
                    // Members start off as "Removed"
                    var partOfCluster = Cluster.SelfMember.Status != MemberStatus.Removed;
                    var possibleReason = partOfCluster
                        ? "Has Cluster Sharding been started on every node and nodes been configured with the correct role(s)?"
                        : "Probably, no seed-nodes configured and manual cluster join not performed?";

                    var bufferSize = shardBuffers.TotalCount;
                    if (bufferSize > 0)
                    {
                        log.Warning("{0}: No coordinator found to register. {1} Total [{2}] buffered messages.",
                            TypeName,
                            possibleReason,
                            bufferSize);
                    }
                    else
                    {
                        log.Debug("{0}: No coordinator found to register. {1} No buffered messages yet.",
                            TypeName, possibleReason);
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
                log.Error(ex, "{0}: When using remember-entities the shard id extractor must handle ShardRegion.StartEntity(id).", TypeName);
            }
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            if (message is RestartShard restart)
            {
                var shardId = restart.ShardId;
                if (RegionByShard.TryGetValue(shardId, out var regionRef))
                {
                    if (Self.Equals(regionRef))
                        GetShard(shardId);
                }
                else
                {
                    if (!shardBuffers.Contains(shardId))
                    {
                        log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", TypeName, shardId, _coordinator);
                        _coordinator?.Tell(new ShardCoordinator.GetShardHome(shardId));
                    }
                    var buffer = shardBuffers.GetOrEmpty(shardId);

                    log.Debug("{0}: Buffer message for shard [{1}]. Total [{2}] buffered messages.",
                        TypeName,
                        shardId,
                        buffer.Count + 1);
                    shardBuffers.Append(shardId, message, sender);
                }
            }
            else
            {
                var shardId = ExtractShardId(message);
                if (RegionByShard.TryGetValue(shardId, out var region))
                {
                    if (region.Equals(Self))
                    {
                        var sref = GetShard(shardId);
                        if (Equals(sref, ActorRefs.Nobody))
                            BufferMessage(shardId, message, sender);
                        else
                        {
                            if (shardBuffers.Contains(shardId))
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
                        if (verboseDebug)
                            log.Debug("{0}: Forwarding request for shard [{1}] to [{2}]", TypeName, shardId, region);
                        region.Tell(message, sender);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(shardId))
                    {
                        log.Warning("{0}: Shard must not be empty, dropping message [{1}]", TypeName, message.GetType().Name);
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        if (!shardBuffers.Contains(shardId))
                        {
                            log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", TypeName, shardId, _coordinator);
                            _coordinator?.Tell(new ShardCoordinator.GetShardHome(shardId));
                        }

                        BufferMessage(shardId, message, sender);
                    }
                }
            }
        }

        private void BufferMessage(ShardId shardId, Msg message, IActorRef sender)
        {
            var totalBufferSize = shardBuffers.TotalCount;
            if (totalBufferSize >= Settings.TuningParameters.BufferSize)
            {
                if (_loggedFullBufferWarning)
                    log.Debug("{0}: Buffer is full, dropping message for shard [{1}]", TypeName, shardId);
                else
                {
                    log.Warning("{0}: Buffer is full, dropping message for shard [{1}]", TypeName, shardId);
                    _loggedFullBufferWarning = true;
                }

                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                shardBuffers.Append(shardId, message, sender);

                // log some insight to how buffers are filled up every 10% of the buffer capacity
                var total = totalBufferSize + 1;
                var bufferSize = Settings.TuningParameters.BufferSize;
                if (total % (bufferSize / 10) == 0)
                {
                    const string logMsg = "{0}: ShardRegion is using [{1} %] of its buffer capacity.";
                    if (total > bufferSize / 2)
                        log.Warning(logMsg + " The coordinator might not be available. You might want to check cluster membership status.", TypeName, 100 * total / bufferSize);
                    else
                        log.Info(logMsg, TypeName, 100 * total / bufferSize);
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
                    if (shardBuffers.NonEmpty) _retryCount++;

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
                    log.Debug("{0}: Starting graceful shutdown of region and all its shards", TypeName);

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

                    gracefulShutdownInProgress = true;
                    SendGracefulShutdownToCoordinatorIfInProgress();
                    TryCompleteGracefulShutdownIfInProgress();
                    break;

                case GracefulShutdownTimeout _:
                    log.Warning(
                        "{0}: Graceful shutdown of shard region timed out, region will be stopped. Remaining shards [{1}], " +
                        "remaining buffered messages [{2}].",
                        TypeName,
                        string.Join(", ", Shards.Keys),
                        shardBuffers.TotalCount);
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
                    Sender.Tell(new ShardRegionStatus(TypeName, _coordinator != null));
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
            var timeout = Settings.ShardRegionQueryTimeout;
            if (timeout == TimeSpan.Zero)
            {
                //simulate timeout
                return Task.FromResult(new ShardsQueryResult<T>(Shards.Keys.ToImmutableHashSet(), ImmutableList<T>.Empty, Shards.Count, timeout));
            }

            var tasks = Shards.Select(entity => (Entity: entity.Key, Task: entity.Value.Ask<T>(message, timeout))).ToImmutableList();
            return Task.WhenAll(tasks.Select(i => i.Task)).ContinueWith(ps =>
            {
                var qr = ShardsQueryResult<T>.Create(tasks, Shards.Count, timeout);
                if (qr.Failed.Count > 0)
                    log.Warning($"{0}: {qr}", TypeName);
                return qr;
            });
        }

        private void TryCompleteGracefulShutdownIfInProgress()
        {
            if (gracefulShutdownInProgress && Shards.Count == 0 && shardBuffers.IsEmpty)
            {
                log.Debug("{0}: Completed graceful shutdown of region.", TypeName);
                Context.Stop(Self);     // all shards have been rebalanced, complete graceful shutdown
            }
        }

        private void SendGracefulShutdownToCoordinatorIfInProgress()
        {
            if (gracefulShutdownInProgress)
            {
                var actorSelections = CoordinatorSelection;
                log.Debug("{0}: Sending graceful shutdown to [{1}]", TypeName, string.Join(", ", actorSelections.Select(i => $"({i})")));
                actorSelections.ForEach(c => c.Tell(new ShardCoordinator.GracefulShutdownRequest(Self)));
            }
        }

        private void HandleCoordinatorMessage(ShardCoordinator.ICoordinatorMessage message)
        {
            switch (message)
            {
                case ShardCoordinator.HostShard hs:
                    {
                        if (gracefulShutdownInProgress)
                        {
                            log.Debug("{0}: Ignoring Host Shard request for [{1}] as region is shutting down", TypeName, hs.Shard);

                            // if the coordinator is sending HostShard to a region that is shutting down
                            // it means that it missed the shutting down message (coordinator moved?)
                            // we want to inform it as soon as possible so it doesn't keep trying to allocate the shard here
                            SendGracefulShutdownToCoordinatorIfInProgress();
                        }
                        else
                        {
                            var shard = hs.Shard;
                            log.Debug("{0}: Host shard [{1}]", TypeName, shard);
                            RegionByShard = RegionByShard.SetItem(shard, Self);
                            UpdateRegionShards(Self, shard);

                            // Start the shard, if already started this does nothing
                            GetShard(shard);

                            Sender.Tell(new ShardCoordinator.ShardStarted(shard));
                        }
                    }
                    break;
                case ShardCoordinator.ShardHome home:
                    log.Debug("{0}: Shard [{1}] located at [{2}]", TypeName, home.Shard, home.Ref);

                    if (RegionByShard.TryGetValue(home.Shard, out var region))
                    {
                        if (region.Equals(Self) && !home.Ref.Equals(Self))
                        {
                            // should not happen, inconsistency between ShardRegion and ShardCoordinator
                            throw new IllegalStateException($"{TypeName}: Unexpected change of shard [{home.Shard}] from self to [{home.Ref}]");
                        }
                    }

                    RegionByShard = RegionByShard.SetItem(home.Shard, home.Ref);
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
                        log.Debug("{0}: BeginHandOff shard [{1}]", TypeName, shard);
                        if (RegionByShard.TryGetValue(shard, out var regionRef))
                        {
                            if (!Regions.TryGetValue(regionRef, out var updatedShards))
                                updatedShards = ImmutableHashSet<ShardId>.Empty;

                            updatedShards = updatedShards.Remove(shard);

                            Regions = updatedShards.Count == 0
                                ? Regions.Remove(regionRef)
                                : Regions.SetItem(regionRef, updatedShards);

                            RegionByShard = RegionByShard.Remove(shard);
                        }

                        Sender.Tell(new ShardCoordinator.BeginHandOffAck(shard));
                    }
                    break;
                case ShardCoordinator.HandOff ho:
                    {
                        var shard = ho.Shard;
                        log.Debug("{0}: HandOff shard [{1}]", TypeName, shard);

                        // must drop requests that came in between the BeginHandOff and now,
                        // because they might be forwarded from other regions and there
                        // is a risk or message re-ordering otherwise
                        if (shardBuffers.Contains(shard))
                        {
                            var dropped = shardBuffers
                                .Drop(shard, "Avoiding reordering of buffered messages at shard handoff", Context.System.DeadLetters);
                            if (dropped > 0)
                                log.Warning("{0}: Dropping [{1}] buffered messages to shard [{2}] during hand off to avoid re-ordering",
                                    TypeName,
                                    dropped,
                                    shard);
                            _loggedFullBufferWarning = false;
                        }

                        if (Shards.TryGetValue(shard, out var actorRef))
                        {
                            HandingOff = HandingOff.Add(actorRef);
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
            if (!Regions.TryGetValue(regionRef, out var shards))
                shards = ImmutableSortedSet<ShardId>.Empty;
            Regions = Regions.SetItem(regionRef, shards.Add(shard));
        }

        /// <summary>
        /// Send GetShardHome for all shards with buffered messages
        /// If coordinator is empty, nothing happens
        /// </summary>
        private void TryRequestShardBufferHomes()
        {
            if (_coordinator != null)
            {
                foreach (var buffer in shardBuffers)
                {
                    log.Debug("{0}: Requesting shard home for [{1}] from coordinator at [{2}]. [{3}] buffered messages.",
                        TypeName,
                        buffer.Key,
                        _coordinator,
                        buffer.Value.Count);

                    _coordinator.Tell(new ShardCoordinator.GetShardHome(buffer.Key));
                }
            }

            if (_retryCount >= RetryCountThreshold && _retryCount % RetryCountThreshold == 0 && log.IsWarningEnabled)
            {
                log.Warning("{0}: Requested shard homes [{1}] from coordinator at [{2}]. [{3}] total buffered messages.",
                    TypeName,
                    string.Join(", ", shardBuffers.Select(i => i.Key).OrderBy(i => i)),
                    _coordinator,
                    shardBuffers.TotalCount
                    );
            }
        }

        private void DeliverBufferedMessages(ShardId shardId, IActorRef receiver)
        {
            if (shardBuffers.Contains(shardId))
            {
                var buffer = shardBuffers.GetOrEmpty(shardId);
                log.Debug("{0}: Deliver [{1}] buffered messages for shard [{2}]", TypeName, buffer.Count, shardId);

                foreach (var m in buffer)
                {
                    if (m.Message is RestartShard && !receiver.Equals(Self))
                    {
                        log.Debug("{0}: Dropping buffered message {1}, these are only processed by a local ShardRegion.",
                            TypeName,
                            m.Message);
                    }
                    else
                    {
                        receiver.Tell(m.Message, m.Ref);
                    }
                }

                shardBuffers.Remove(shardId);
            }

            _loggedFullBufferWarning = false;
            _retryCount = 0;
        }

        private IActorRef GetShard(ShardId id)
        {
            if (StartingShards.Contains(id))
                return ActorRefs.Nobody;

            if (!Shards.TryGetValue(id, out var shard))
            {
                if (EntityProps == null)
                    throw new IllegalStateException("Shard must not be allocated to a proxy only ShardRegion");

                if (ShardsByRef.Values.All(shardId => shardId != id))
                {
                    log.Debug("{0}: Starting shard [{1}] in region", TypeName, id);

                    var name = Uri.EscapeDataString(id);
                    var shardRef = Context.Watch(Context.ActorOf(Shard.Props(
                        TypeName,
                        id,
                        EntityProps,
                        Settings,
                        ExtractEntityId,
                        ExtractShardId,
                        HandOffStopMessage,
                        _rememberEntitiesProvider)
                        .WithDispatcher(Context.Props.Dispatcher), name));

                    ShardsByRef = ShardsByRef.SetItem(shardRef, id);
                    Shards = Shards.SetItem(id, shardRef);
                    StartingShards = StartingShards.Add(id);
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
                        if (m.UniqueAddress == Cluster.SelfUniqueAddress)
                            Context.Stop(Self);
                        else if (MatchingRole(m))
                            ChangeMembers(MembersByAge.Remove(m));
                    }
                    break;

                case ClusterEvent.MemberDowned md:
                    if (md.Member.UniqueAddress == Cluster.SelfUniqueAddress)
                    {
                        log.Info("{0}: Self downed, stopping ShardRegion [{1}]", TypeName, Self.Path);
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
                ChangeMembers(MembersByAge.Remove(m).Add(m));
            }
        }

        private void HandleTerminated(Terminated terminated)
        {
            if (_coordinator != null && _coordinator.Equals(terminated.ActorRef))
            {
                _coordinator = null;
                StartRegistration();
            }
            else if (Regions.TryGetValue(terminated.ActorRef, out var shards))
            {
                RegionByShard = RegionByShard.RemoveRange(shards);
                Regions = Regions.Remove(terminated.ActorRef);

                if (log.IsDebugEnabled)
                    if (verboseDebug)
                        log.Debug(
                          "{0}: Region [{1}] terminated with [{2}] shards [{3}]",
                          TypeName,
                          terminated.ActorRef,
                          shards.Count,
                          string.Join(", ", shards));
                    else
                        log.Debug("{0}: Region [{1}] terminated with [{2}] shards",
                            TypeName, terminated.ActorRef, shards.Count);
            }
            else if (ShardsByRef.TryGetValue(terminated.ActorRef, out var shard))
            {
                ShardsByRef = ShardsByRef.Remove(terminated.ActorRef);
                Shards = Shards.Remove(shard);
                StartingShards = StartingShards.Remove(shard);
                if (HandingOff.Contains(terminated.ActorRef))
                {
                    HandingOff = HandingOff.Remove(terminated.ActorRef);
                    log.Debug("{0}: Shard [{1}] handoff complete", TypeName, shard);
                }
                else
                {
                    // if persist fails it will stop
                    log.Debug("{0}: Shard [{1}] terminated while not being handed off", TypeName, shard);
                    if (Settings.RememberEntities)
                        Context.System.Scheduler.ScheduleTellOnce(Settings.TuningParameters.ShardFailureBackoff, Self, new RestartShard(shard), Self);
                }

                // did this shard get removed because the ShardRegion is shutting down?
                // If so, we can try to speed-up the region shutdown. We don't need to wait for the next tick.
                TryCompleteGracefulShutdownIfInProgress();
            }
        }
    }
}
