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
using Akka.Event;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntityId = String;
    using Msg = Object;

    /// <summary>
    /// This actor creates children shard actors on demand that it is told to be responsible for.
    /// The shard actors in turn create entity actors on demand.
    /// It delegates messages targeted to other shards to the responsible
    /// <see cref="ShardRegion"/> actor on other nodes.
    /// </summary>
    public class ShardRegion : ActorBase
    {
        #region messages

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        internal sealed class Retry : IShardRegionCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Retry Instance = new Retry();
            private Retry() { }
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
        public sealed class StartEntity : IClusterShardingSerializable
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
                var other = obj as StartEntity;

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
        /// Sent back when a <see cref="StartEntity"/> message was received and triggered the entity
        /// to start(it does not guarantee the entity successfully started)
        /// </summary>
        [Serializable]
        public sealed class StartEntityAck : IClusterShardingSerializable, IDeadLetterSuppression
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
                var other = obj as StartEntityAck;

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
                    int hashCode = EntityId?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (ShardId?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }

            #endregion
        }

        #endregion

        /// <summary>
        /// INTERNAL API. Sends stopMessage (e.g. <see cref="PoisonPill"/>) to the entities and when all of them have terminated it replies with `ShardStopped`.
        /// </summary>
        internal class HandOffStopper : ReceiveActor
        {
            private ILoggingAdapter _log;
            /// <summary>
            /// TBD
            /// </summary>
            public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="replyTo">TBD</param>
            /// <param name="entities">TBD</param>
            /// <param name="stopMessage">TBD</param>
            /// <param name="handoffTimeout">TBD</param>
            /// <returns>TBD</returns>
            public static Props Props(ShardId shard, IActorRef replyTo, IEnumerable<IActorRef> entities, object stopMessage, TimeSpan handoffTimeout)
            {
                return Actor.Props.Create(() => new HandOffStopper(shard, replyTo, entities, stopMessage, handoffTimeout)).WithDeploy(Deploy.Local);
            }

            /// <summary>
            ///Sends stopMessage (e.g. `PoisonPill`) to the entities and when all of
            /// them have terminated it replies with `ShardStopped`.
            /// If the entities don't terminate after `handoffTimeout` it will try stopping them forcefully.
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="replyTo">TBD</param>
            /// <param name="entities">TBD</param>
            /// <param name="stopMessage">TBD</param>
            /// <param name="handoffTimeout">TBD</param>
            public HandOffStopper(ShardId shard, IActorRef replyTo, IEnumerable<IActorRef> entities, object stopMessage, TimeSpan handoffTimeout)
            {
                var remaining = new HashSet<IActorRef>(entities);

                Receive<ReceiveTimeout>(t =>
                {
                    Log.Warning("HandOffStopMessage[{0}] is not handled by some of the entities of the [{1}] shard after [{2}], " +
                        "stopping the remaining [{3}] entities.", stopMessage.GetType(), shard, handoffTimeout, remaining.Count);
                    foreach (var r in remaining)
                        Context.Stop(r);
                });
                Receive<Terminated>(t =>
                {
                    remaining.Remove(t.ActorRef);
                    if (remaining.Count == 0)
                    {
                        replyTo.Tell(new PersistentShardCoordinator.ShardStopped(shard));
                        Context.Stop(Self);
                    }
                });

                Context.SetReceiveTimeout(handoffTimeout);

                foreach (var aref in remaining)
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
        /// <param name="replicator"></param>
        /// <param name="majorityMinCap"></param>
        /// <returns>TBD</returns>
        internal static Props Props(string typeName, Func<string, Props> entityProps, ClusterShardingSettings settings, string coordinatorPath, ExtractEntityId extractEntityId, ExtractShardId extractShardId, object handOffStopMessage, IActorRef replicator, int majorityMinCap)
        {
            return Actor.Props.Create(() => new ShardRegion(typeName, entityProps, settings, coordinatorPath, extractEntityId, extractShardId, handOffStopMessage, replicator, majorityMinCap)).WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="ShardRegion"/> actor when used in proxy only mode.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="coordinatorPath">TBD</param>
        /// <param name="extractEntityId">TBD</param>
        /// <param name="extractShardId">TBD</param>
        /// <param name="replicator"></param>
        /// <param name="majorityMinCap"></param>
        /// <returns>TBD</returns>
        internal static Props ProxyProps(string typeName, ClusterShardingSettings settings, string coordinatorPath, ExtractEntityId extractEntityId, ExtractShardId extractShardId, IActorRef replicator, int majorityMinCap)
        {
            return Actor.Props.Create(() => new ShardRegion(typeName, null, settings, coordinatorPath, extractEntityId, extractShardId, PoisonPill.Instance, replicator, majorityMinCap)).WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string TypeName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Func<string, Props> EntityProps;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ClusterShardingSettings Settings;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string CoordinatorPath;
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

        private readonly IActorRef _replicator;
        private readonly int _majorityMinCap;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Cluster Cluster = Cluster.Get(Context.System);

        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableSet<Member> MembersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering);

        // membersByAge contains members with these status
        private static readonly ImmutableHashSet<MemberStatus> MemberStatusOfInterest = ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Exiting);

        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<IActorRef, IImmutableSet<ShardId>> Regions = ImmutableDictionary<IActorRef, IImmutableSet<ShardId>>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<ShardId, IActorRef> RegionByShard = ImmutableDictionary<ShardId, IActorRef>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<ShardId, IImmutableList<KeyValuePair<Msg, IActorRef>>> ShardBuffers = ImmutableDictionary<ShardId, IImmutableList<KeyValuePair<Msg, IActorRef>>>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<ShardId, IActorRef> Shards = ImmutableDictionary<ShardId, IActorRef>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableDictionary<IActorRef, ShardId> ShardsByRef = ImmutableDictionary<IActorRef, ShardId>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableSet<ShardId> StartingShards = ImmutableHashSet<ShardId>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        protected IImmutableSet<IActorRef> HandingOff = ImmutableHashSet<IActorRef>.Empty;

        private readonly ICancelable _retryTask;
        private IActorRef _coordinator;
        private int _retryCount;
        private bool _loggedFullBufferWarning;
        private const int RetryCountThreshold = 5;

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
        /// <param name="replicator"></param>
        /// <param name="majorityMinCap"></param>
        public ShardRegion(string typeName, Func<string, Props> entityProps, ClusterShardingSettings settings, string coordinatorPath, ExtractEntityId extractEntityId, ExtractShardId extractShardId, object handOffStopMessage, IActorRef replicator, int majorityMinCap)
        {
            TypeName = typeName;
            EntityProps = entityProps;
            Settings = settings;
            CoordinatorPath = coordinatorPath;
            ExtractEntityId = extractEntityId;
            ExtractShardId = extractShardId;
            HandOffStopMessage = handOffStopMessage;
            _replicator = replicator;
            _majorityMinCap = majorityMinCap;

            _retryTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.TunningParameters.RetryInterval, Settings.TunningParameters.RetryInterval, Self, Retry.Instance, Self);
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

        private ILoggingAdapter _log;
        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }
        /// <summary>
        /// TBD
        /// </summary>
        public bool GracefulShutdownInProgress { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public int TotalBufferSize { get { return ShardBuffers.Aggregate(0, (acc, entity) => acc + entity.Value.Count); } }

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

        /// <summary>
        /// TBD
        /// </summary>
        protected object RegistrationMessage
        {
            get
            {
                if (EntityProps != null)
                    return new PersistentShardCoordinator.Register(Self);
                return new PersistentShardCoordinator.RegisterProxy(Self);
            }
        }


        /// <inheritdoc cref="ActorBase.PreStart"/>
        /// <summary>
        /// Subscribe to MemberEvent, re-subscribe when restart
        /// </summary>
        protected override void PreStart()
        {
            Cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            LogPassivateIdleEntities();
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            base.PostStop();
            Cluster.Unsubscribe(Self);
            _gracefulShutdownProgress.TrySetResult(Done.Instance);
            _retryTask.Cancel();
        }

        private void LogPassivateIdleEntities()
        {
            if (Settings.ShouldPassivateIdleEntities)
                Log.Info("{0}: Idle entities will be passivated after [{1}]",
                    TypeName,
                    Settings.PassivateIdleEntityAfter);

            if (Settings.RememberEntities)
                Log.Debug("Idle entities will not be passivated because 'rememberEntities' is enabled.");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="member">TBD</param>
        /// <returns>TBD</returns>
        protected bool MatchingRole(Member member)
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
                if (Log.IsDebugEnabled)
                    Log.Debug("{0}: Coordinator moved from [{1}] to [{2}]",
                        TypeName,
                        before?.Address.ToString() ?? string.Empty,
                        after?.Address.ToString() ?? string.Empty);

                _coordinator = null;
                Register();
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
                case PersistentShardCoordinator.ICoordinatorMessage cm:
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
                    Log.Warning("{0}: Message does not have an extractor defined in shard so it was ignored: {1}", TypeName, message);
                    return false;
            }
        }

        private void InitializeShard(ShardId id, IActorRef shardRef)
        {
            Log.Debug("{0}: Shard was initialized [{1}]", TypeName, id);
            StartingShards = StartingShards.Remove(id);
            DeliverBufferedMessage(id, shardRef);
        }

        private void Register()
        {
            var actorSelections = CoordinatorSelection;
            foreach (var coordinator in actorSelections)
                coordinator.Tell(RegistrationMessage);

            if (ShardBuffers.Count != 0 && _retryCount >= RetryCountThreshold)
            {
                if (actorSelections.Count > 0)
                {
                    var coordinatorMessage = Cluster.State.Unreachable.Contains(MembersByAge.First()) 
                        ? $"Coordinator [{MembersByAge.First()}] is unreachable." 
                        : $"Coordinator [{MembersByAge.First()}] is reachable.";

                    Log.Warning("{0}: Trying to register to coordinator at [{1}], but no acknowledgement. Total [{2}] buffered messages. [{3}]", 
                        TypeName, 
                        string.Join(", ", actorSelections.Select(i => i.PathString)), 
                        TotalBufferSize, 
                        coordinatorMessage);
                }
                else
                {
                    // Members start off as "Removed"
                    var partOfCluster = Cluster.SelfMember.Status != MemberStatus.Removed;
                    var possibleReason = partOfCluster 
                        ? "Has Cluster Sharding been started on every node and nodes been configured with the correct role(s)?" 
                        : "Probably, no seed-nodes configured and manual cluster join not performed?";

                    Log.Warning("{0}: No coordinator found to register. {1} Total [{2}] buffered messages.", 
                        TypeName, possibleReason, TotalBufferSize);
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
                Log.Error(ex, "{0}: When using remember-entities the shard id extractor must handle ShardRegion.StartEntity(id).", TypeName);
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
                    if (!ShardBuffers.TryGetValue(shardId, out var buffer))
                    {
                        buffer = ImmutableList<KeyValuePair<object, IActorRef>>.Empty;
                        Log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", TypeName, shardId, _coordinator);
                        _coordinator?.Tell(new PersistentShardCoordinator.GetShardHome(shardId));
                    }

                    Log.Debug("{0}: Buffer message for shard [{1}]. Total [{2}] buffered messages.", TypeName, shardId, buffer.Count + 1);
                    ShardBuffers = ShardBuffers.SetItem(shardId, buffer.Add(new KeyValuePair<object, IActorRef>(message, sender)));
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
                            if (ShardBuffers.TryGetValue(shardId, out var buffer))
                            {
                                // Since now messages to a shard is buffered then those messages must be in right order
                                BufferMessage(shardId, message, sender);
                                DeliverBufferedMessage(shardId, sref);
                            }
                            else
                                sref.Tell(message, sender);
                        }
                    }
                    else
                    {
                        Log.Debug("{0}: Forwarding request for shard [{1}] to [{2}]", TypeName, shardId, region);
                        region.Tell(message, sender);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(shardId))
                    {
                        Log.Warning("{0}: Shard must not be empty, dropping message [{1}]", TypeName, message.GetType());
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        if (!ShardBuffers.ContainsKey(shardId))
                        {
                            Log.Debug("{0}: Request shard [{1}] home. Coordinator [{2}]", TypeName, shardId, _coordinator);
                            _coordinator?.Tell(new PersistentShardCoordinator.GetShardHome(shardId));
                        }

                        BufferMessage(shardId, message, sender);
                    }
                }
            }
        }

        private void BufferMessage(ShardId shardId, Msg message, IActorRef sender)
        {
            var totalBufferSize = TotalBufferSize;
            if (totalBufferSize >= Settings.TunningParameters.BufferSize)
            {
                if (_loggedFullBufferWarning)
                    Log.Debug("{0}: Buffer is full, dropping message for shard [{1}]", TypeName, shardId);
                else
                {
                    Log.Warning("{0}: Buffer is full, dropping message for shard [{1}]", TypeName, shardId);
                    _loggedFullBufferWarning = true;
                }

                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                if (!ShardBuffers.TryGetValue(shardId, out var buffer))
                    buffer = ImmutableList<KeyValuePair<Msg, IActorRef>>.Empty;
                ShardBuffers = ShardBuffers.SetItem(shardId, buffer.Add(new KeyValuePair<object, IActorRef>(message, sender)));

                // log some insight to how buffers are filled up every 10% of the buffer capacity
                var total = totalBufferSize + 1;
                var bufferSize = Settings.TunningParameters.BufferSize;
                if (total % (bufferSize / 10) == 0)
                {
                    const string logMsg = "{0}: ShardRegion is using [{1} %] of its buffer capacity.";
                    if (total > bufferSize / 2)
                        Log.Warning(logMsg + " The coordinator might not be available. You might want to check cluster membership status.", TypeName, 100 * total / bufferSize);
                    else
                        Log.Info(logMsg, TypeName, 100 * total / bufferSize);
                }
            }
        }

        private void HandleShardRegionCommand(IShardRegionCommand command)
        {
            switch (command)
            {
                case Retry _:
                    SendGracefulShutdownToCoordinator();

                    if (ShardBuffers.Count != 0) _retryCount++;

                    if (_coordinator == null) Register();
                    else
                    {
                        RequestShardBufferHomes();
                    }

                    TryCompleteGracefulShutdown();

                    break;

                case GracefulShutdown _:
                    Log.Debug("{0}: Starting graceful shutdown of region and all its shards", TypeName);
                    GracefulShutdownInProgress = true;
                    SendGracefulShutdownToCoordinator();
                    TryCompleteGracefulShutdown();
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
                case GetShardRegionState _:
                    ReplyToRegionStateQuery(Sender);
                    break;
                case GetShardRegionStats _:
                    ReplyToRegionStatsQuery(Sender);
                    break;
                case GetClusterShardingStats _:
                    if (_coordinator != null)
                        _coordinator.Forward(query);
                    else
                        Sender.Tell(new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty));
                    break;
                default:
                    Unhandled(query);
                    break;
            }
        }

        private void ReplyToRegionStateQuery(IActorRef sender)
        {
            AskAllShardsAsync<Shard.CurrentShardState>(Shard.GetCurrentShardState.Instance)
                .ContinueWith(shardStates =>
                {
                    if (shardStates.IsCanceled)
                        return new CurrentShardRegionState(ImmutableHashSet<ShardState>.Empty);

                    if (shardStates.IsFaulted)
                        throw shardStates.Exception; //TODO check if this is the right way

                    return new CurrentShardRegionState(shardStates.Result.Select(x => new ShardState(x.Item1, x.Item2.EntityIds.ToImmutableHashSet())).ToImmutableHashSet());
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(sender);
        }

        private void ReplyToRegionStatsQuery(IActorRef sender)
        {
            AskAllShardsAsync<Shard.ShardStats>(Shard.GetShardStats.Instance)
                .ContinueWith(shardStats =>
                {
                    if (shardStats.IsCanceled)
                        return new ShardRegionStats(ImmutableDictionary<string, int>.Empty);

                    if (shardStats.IsFaulted)
                        throw shardStats.Exception; //TODO check if this is the right way

                    return new ShardRegionStats(shardStats.Result.ToImmutableDictionary(x => x.Item1, x => x.Item2.EntityCount));
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(sender);
        }

        private Task<(ShardId, T)[]> AskAllShardsAsync<T>(object message)
        {
            var timeout = TimeSpan.FromSeconds(3);
            var tasks = Shards.Select(entity => entity.Value.Ask<T>(message, timeout).ContinueWith(t => (entity.Key, t.Result), TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion));
            return Task.WhenAll(tasks);
        }

        private void TryCompleteGracefulShutdown()
        {
            if (GracefulShutdownInProgress && Shards.Count == 0 && ShardBuffers.Count == 0)
                Context.Stop(Self);     // all shards have been rebalanced, complete graceful shutdown
        }

        private void SendGracefulShutdownToCoordinator()
        {
            if (GracefulShutdownInProgress)
            {
                Log.Debug("Sending graceful shutdown to {0}", CoordinatorSelection);
                CoordinatorSelection.ForEach(c => c.Tell(new PersistentShardCoordinator.GracefulShutdownRequest(Self)));
            } 
        }

        private void HandleCoordinatorMessage(PersistentShardCoordinator.ICoordinatorMessage message)
        {
            switch (message)
            {
                case PersistentShardCoordinator.HostShard hs:
                    {
                        var shard = hs.Shard;
                        Log.Debug("{0}: Host shard [{1}]", TypeName, shard);
                        RegionByShard = RegionByShard.SetItem(shard, Self);
                        UpdateRegionShards(Self, shard);

                        // Start the shard, if already started this does nothing
                        GetShard(shard);

                        Sender.Tell(new PersistentShardCoordinator.ShardStarted(shard));
                    }
                    break;
                case PersistentShardCoordinator.ShardHome home:
                    Log.Debug("{0}: Shard [{1}] located at [{2}]", TypeName, home.Shard, home.Ref);

                    if (RegionByShard.TryGetValue(home.Shard, out var region))
                    {
                        if (region.Equals(Self) && !home.Ref.Equals(Self))
                        {
                            // should not happen, inconsistency between ShardRegion and PersistentShardCoordinator
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
                            DeliverBufferedMessage(home.Shard, shardRef);
                    }
                    else
                        DeliverBufferedMessage(home.Shard, home.Ref);
                    break;
                case PersistentShardCoordinator.RegisterAck ra:
                    _coordinator = ra.Coordinator;
                    Context.Watch(_coordinator);
                    RequestShardBufferHomes();
                    break;
                case PersistentShardCoordinator.BeginHandOff bho:
                    {
                        var shard = bho.Shard;
                        Log.Debug("{0}: BeginHandOff shard [{1}]", TypeName, shard);
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

                        Sender.Tell(new PersistentShardCoordinator.BeginHandOffAck(shard));
                    }
                    break;
                case PersistentShardCoordinator.HandOff ho:
                    {
                        var shard = ho.Shard;
                        Log.Debug("{0}: HandOff shard [{1}]", TypeName, shard);

                        // must drop requests that came in between the BeginHandOff and now,
                        // because they might be forwarded from other regions and there
                        // is a risk or message re-ordering otherwise
                        if (ShardBuffers.ContainsKey(shard))
                        {
                            ShardBuffers = ShardBuffers.Remove(shard);
                            _loggedFullBufferWarning = false;
                        }

                        if (Shards.TryGetValue(shard, out var actorRef))
                        {
                            HandingOff = HandingOff.Add(actorRef);
                            actorRef.Forward(message);
                        }
                        else
                            Sender.Tell(new PersistentShardCoordinator.ShardStopped(shard));
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

        private void RequestShardBufferHomes()
        {
            foreach (var buffer in ShardBuffers)
            {
                const string logMsg = "{0}: Retry request for shard [{1}] homes from coordinator at [{2}]. [{3}] buffered messages.";
                if (_retryCount >= RetryCountThreshold)
                    Log.Warning(logMsg, TypeName, buffer.Key, _coordinator, buffer.Value.Count);
                else
                    Log.Debug(logMsg, TypeName, buffer.Key, _coordinator, buffer.Value.Count);

                _coordinator.Tell(new PersistentShardCoordinator.GetShardHome(buffer.Key));
            }
        }

        private void DeliverBufferedMessage(ShardId shardId, IActorRef receiver)
        {
            if (ShardBuffers.TryGetValue(shardId, out var buffer))
            {
                Log.Debug("{0}: Deliver [{1}] buffered messages for shard [{2}]", TypeName, buffer.Count, shardId);

                foreach (var m in buffer)
                    receiver.Tell(m.Key, m.Value);

                ShardBuffers = ShardBuffers.Remove(shardId);
            }

            _loggedFullBufferWarning = false;
            _retryCount = 0;
        }

        private IActorRef GetShard(ShardId id)
        {
            if (StartingShards.Contains(id))
                return ActorRefs.Nobody;

            //TODO: change on ConcurrentDictionary.GetOrAdd?
            if (!Shards.TryGetValue(id, out var region))
            {
                if (EntityProps == null)
                    throw new IllegalStateException("Shard must not be allocated to a proxy only ShardRegion");

                if (ShardsByRef.Values.All(shardId => shardId != id))
                {
                    Log.Debug("{0}: Starting shard [{1}] in region", TypeName, id);

                    var name = Uri.EscapeDataString(id);
                    var shardRef = Context.Watch(Context.ActorOf(Sharding.Shards.Props(
                        TypeName,
                        id,
                        EntityProps,
                        Settings,
                        ExtractEntityId,
                        ExtractShardId,
                        HandOffStopMessage,
                        _replicator,
                        _majorityMinCap).WithDispatcher(Context.Props.Dispatcher), name));

                    ShardsByRef = ShardsByRef.SetItem(shardRef, id);
                    Shards = Shards.SetItem(id, shardRef);
                    StartingShards = StartingShards.Add(id);
                    return shardRef;
                }
            }

            return region ?? ActorRefs.Nobody;
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
                        Log.Info("{0}: Self downed, stopping ShardRegion [{1}]", TypeName, Self.Path);
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
                _coordinator = null;
            else if (Regions.TryGetValue(terminated.ActorRef, out var shards))
            {
                RegionByShard = RegionByShard.RemoveRange(shards);
                Regions = Regions.Remove(terminated.ActorRef);

                if (Log.IsDebugEnabled)
                    Log.Debug("{0}: Region [{1}] with shards [{2}] terminated", TypeName, terminated.ActorRef, string.Join(", ", shards));
            }
            else if (ShardsByRef.TryGetValue(terminated.ActorRef, out var shard))
            {
                ShardsByRef = ShardsByRef.Remove(terminated.ActorRef);
                Shards = Shards.Remove(shard);
                StartingShards = StartingShards.Remove(shard);
                if (HandingOff.Contains(terminated.ActorRef))
                {
                    HandingOff = HandingOff.Remove(terminated.ActorRef);
                    Log.Debug("{0}: Shard [{1}] handoff complete", TypeName, shard);
                }
                else
                {
                    // if persist fails it will stop
                    Log.Debug("{0}: Shard [{1}] terminated while not being handed off", TypeName, shard);
                    if (Settings.RememberEntities)
                        Context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.ShardFailureBackoff, Self, new RestartShard(shard), Self);
                }

                TryCompleteGracefulShutdown();
            }
        }
    }
}
