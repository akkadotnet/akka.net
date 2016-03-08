//-----------------------------------------------------------------------
// <copyright file="ShardRegion.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;
    using EntityId = String;
    using Msg = Object;

    /// <summary>
    /// This actor creates children entity actors on demand for the shards that it is told to be
    /// responsible for. It delegates messages targeted to other shards to the responsible
    /// <see cref="ShardRegion"/> actor on other nodes.
    /// </summary>
    public class ShardRegion : ActorBase
    {
        #region messages
        
        [Serializable]
        internal sealed class Retry : IShardRegionCommand
        {
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
            public readonly ShardId ShardId;
            public RestartShard(string shardId)
            {
                ShardId = shardId;
            }
        }

        #endregion

        /// <summary>
        /// INTERNAL API. Sends stopMessage (e.g. <see cref="PoisonPill"/>) to the entities and when all of them have terminated it replies with `ShardStopped`.
        /// </summary>
        internal class HandOffStopper : ReceiveActor
        {
            public static Actor.Props Props(ShardId shard, IActorRef replyTo, IEnumerable<IActorRef> entities, object stopMessage)
            {
                return Actor.Props.Create(() => new HandOffStopper(shard, replyTo, entities, stopMessage)).WithDeploy(Deploy.Local);
            }

            public HandOffStopper(ShardId shard, IActorRef replyTo, IEnumerable<IActorRef> entities, object stopMessage)
            {
                var remaining = new HashSet<IActorRef>(entities);

                Receive<Terminated>(t =>
                {
                    remaining.Remove(t.ActorRef);
                    if (remaining.Count == 0)
                    {
                        replyTo.Tell(new PersistentShardCoordinator.ShardStopped(shard));
                        Context.Stop(Self);
                    }
                });

                foreach (var aref in remaining)
                {
                    Context.Watch(aref);
                    aref.Tell(stopMessage);
                }
            }
        }

        private class MemberAgeComparer : IComparer<Member>
        {
            public static readonly IComparer<Member> Instance = new MemberAgeComparer();

            private MemberAgeComparer() { }

            public int Compare(Member x, Member y)
            {
                if (y.IsOlderThan(x)) return -1;
                return x.IsOlderThan(y) ? 1 : 0;
            }
        }

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="ShardRegion"/> actor.
        /// </summary>
        internal static Props Props(string typeName, Props entityProps, ClusterShardingSettings settings, string coordinatorPath, IdExtractor extractEntityId, ShardResolver extractShardId, object handOffStopMessage)
        {
            return Actor.Props.Create(() => new ShardRegion(typeName, entityProps, settings, coordinatorPath, extractEntityId, extractShardId, handOffStopMessage)).WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="ShardRegion"/> actor when used in proxy only mode.
        /// </summary>
        internal static Props ProxyProps(string typeName, ClusterShardingSettings settings, string coordinatorPath, IdExtractor extractEntityId, ShardResolver extractShardId)
        {
            return Actor.Props.Create(() => new ShardRegion(typeName, null, settings, coordinatorPath, extractEntityId, extractShardId, PoisonPill.Instance)).WithDeploy(Deploy.Local);
        }

        public readonly string TypeName;
        public readonly Props EntityProps;
        public readonly ClusterShardingSettings Settings;
        public readonly string CoordinatorPath;
        public readonly IdExtractor IdExtractor;
        public readonly ShardResolver ShardResolver;
        public readonly object HandOffStopMessage;

        public readonly Cluster Cluster = Cluster.Get(Context.System);

        // sort by age, oldest first
        private static readonly IComparer<Member> AgeOrdering = MemberAgeComparer.Instance;

        protected IImmutableDictionary<IActorRef, IImmutableSet<ShardId>> Regions = ImmutableDictionary<IActorRef, IImmutableSet<ShardId>>.Empty;
        protected IImmutableDictionary<ShardId, IActorRef> RegionByShard = ImmutableDictionary<ShardId, IActorRef>.Empty;
        protected IImmutableDictionary<ShardId, IImmutableList<KeyValuePair<Msg, IActorRef>>> ShardBuffers = ImmutableDictionary<ShardId, IImmutableList<KeyValuePair<Msg, IActorRef>>>.Empty;
        protected IImmutableDictionary<ShardId, IActorRef> Shards = ImmutableDictionary<ShardId, IActorRef>.Empty;
        protected IImmutableDictionary<IActorRef, ShardId> ShardsByRef = ImmutableDictionary<IActorRef, ShardId>.Empty;
        protected IImmutableSet<Member> MembersByAge;
        protected IImmutableSet<IActorRef> HandingOff = ImmutableHashSet<IActorRef>.Empty;

        private readonly ICancelable _retryTask;
        private IActorRef _coordinator = null;
        private int _retryCount = 0;
        private bool _loggedFullBufferWarning = false;
        private const int RetryCountThreshold = 5;

        public ShardRegion(string typeName, Props entityProps, ClusterShardingSettings settings, string coordinatorPath, IdExtractor extractEntityId, ShardResolver extractShardId, object handOffStopMessage)
        {
            TypeName = typeName;
            EntityProps = entityProps;
            Settings = settings;
            CoordinatorPath = coordinatorPath;
            IdExtractor = extractEntityId;
            ShardResolver = extractShardId;
            HandOffStopMessage = handOffStopMessage;

            //TODO: how to apply custom comparer different way?
            var membersByAgeBuilder = ImmutableSortedSet<Member>.Empty.ToBuilder();
            membersByAgeBuilder.KeyComparer = AgeOrdering;
            MembersByAge = membersByAgeBuilder.ToImmutable();

            _retryTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.TunningParameters.RetryInterval, Settings.TunningParameters.RetryInterval, Self, Retry.Instance, Self);
        }

        private ILoggingAdapter _log;
        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }
        public bool GracefulShutdownInProgres { get; private set; }
        public int TotalBufferSize { get { return ShardBuffers.Aggregate(0, (acc, entity) => acc + entity.Value.Count); } }
        
        protected ActorSelection CoordinatorSelection
        {
            get
            {
                var firstMember = MembersByAge.FirstOrDefault();
                return firstMember == null ? null : Context.ActorSelection(firstMember.Address.ToString() + CoordinatorPath);
            }
        }

        protected object RegistrationMessage
        {
            get
            {
                if (EntityProps != null && !EntityProps.Equals(Actor.Props.None))
                    return new PersistentShardCoordinator.Register(Self);
                else return new PersistentShardCoordinator.RegisterProxy(Self);
            }
        }

        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            base.PostStop();
            Cluster.Unsubscribe(Self);
            _retryTask.Cancel();
        }

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
                    Log.Debug("Coordinator moved from [{0}] to [{1}]",
                        before == null ? string.Empty : before.Address.ToString(),
                        after == null ? string.Empty : after.Address.ToString());

                _coordinator = null;
                Register();
            }
        }

        protected override bool Receive(object message)
        {
            if (message is Terminated) HandleTerminated(message as Terminated);
            else if (message is ShardInitialized) InitializeShard(((ShardInitialized)message).ShardId, Sender);
            else if (message is ClusterEvent.IClusterDomainEvent) HandleClusterEvent(message as ClusterEvent.IClusterDomainEvent);
            else if (message is ClusterEvent.CurrentClusterState) HandleClusterState(message as ClusterEvent.CurrentClusterState);
            else if (message is PersistentShardCoordinator.ICoordinatorMessage) HandleCoordinatorMessage(message as PersistentShardCoordinator.ICoordinatorMessage);
            else if (message is IShardRegionCommand) HandleShardRegionCommand(message as IShardRegionCommand);
            else if (message is IShardRegionQuery) HandleShardRegionQuery(message as IShardRegionQuery);
            else if (IdExtractor(message) != null) DeliverMessage(message, Sender);
            else if (message is RestartShard) DeliverMessage(message, Sender);
            else return false;
            return true;
        }

        private void InitializeShard(ShardId id, IActorRef shardRef)
        {
            Log.Debug("Shard was initialized [{0}]", id);
            Shards = Shards.SetItem(id, shardRef);
            DeliverBufferedMessage(id, shardRef);
        }

        private void Register()
        {
            var coordinator = CoordinatorSelection;
            if (coordinator != null)
                coordinator.Tell(RegistrationMessage);

            if (ShardBuffers.Count != 0 && _retryCount >= RetryCountThreshold)
                Log.Warning("Trying to register to coordinator at [{0}], but no acknowledgement. Total [{1}] buffered messages.",
                    coordinator != null ? coordinator.PathString : string.Empty, TotalBufferSize);
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            var restart = message as RestartShard;
            if (restart != null)
            {
                var shardId = restart.ShardId;
                IActorRef regionRef;
                if (RegionByShard.TryGetValue(shardId, out regionRef))
                {
                    if (Self.Equals(regionRef)) GetShard(shardId);
                }
                else
                {
                    IImmutableList<KeyValuePair<Msg, IActorRef>> buffer;
                    if (!ShardBuffers.TryGetValue(shardId, out buffer))
                    {
                        buffer = ImmutableList<KeyValuePair<object, IActorRef>>.Empty;
                        Log.Debug("Request shard [{0}] home", shardId);
                        if (_coordinator != null)
                            _coordinator.Tell(new PersistentShardCoordinator.GetShardHome(shardId));
                    }

                    Log.Debug("Buffer message for shard [{0}]. Total [{1}] buffered messages.", shardId, buffer.Count + 1);
                    ShardBuffers = ShardBuffers.SetItem(shardId, buffer.Add(new KeyValuePair<object, IActorRef>(message, sender)));
                }
            }
            else
            {
                IActorRef region;
                var shardId = ShardResolver(message);
                if (RegionByShard.TryGetValue(shardId, out region))
                {
                    if (region.Equals(Self))
                    {
                        var sref = GetShard(shardId);
                        if (Equals(sref, ActorRefs.Nobody))
                            BufferMessage(shardId, message, sender);
                        else
                        {
                            IImmutableList<KeyValuePair<Msg, IActorRef>> buffer;
                            if (ShardBuffers.TryGetValue(shardId, out buffer))
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
                        Log.Debug("Forwarding request for shard [{0}] to [{1}]", shardId, region);
                        region.Tell(message, sender);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(shardId))
                    {
                        Log.Warning("Shard must not be empty, dropping message [{0}]", message.GetType());
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        if (!ShardBuffers.ContainsKey(shardId))
                        {
                            Log.Debug("Request shard [{0}] home", shardId);
                            if (_coordinator != null)
                                _coordinator.Tell(new PersistentShardCoordinator.GetShardHome(shardId));
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
                    Log.Debug("Buffer is full, dropping message for shard [{0}]", shardId);
                else
                {
                    Log.Warning("Buffer is full, dropping message for shard [{0}]", shardId);
                    _loggedFullBufferWarning = true;
                }

                Context.System.DeadLetters.Tell(message);
            }
            else
            {
                IImmutableList<KeyValuePair<Msg, IActorRef>> buffer;
                if (!ShardBuffers.TryGetValue(shardId, out buffer)) buffer = ImmutableList<KeyValuePair<Msg, IActorRef>>.Empty;
                ShardBuffers = ShardBuffers.SetItem(shardId, buffer.Add(new KeyValuePair<object, IActorRef>(message, sender)));

                // log some insight to how buffers are filled up every 10% of the buffer capacity
                var total = totalBufferSize + 1;
                var bufferSize = Settings.TunningParameters.BufferSize;
                if (total % (bufferSize / 10) == 0)
                {
                    var logMsg = "ShardRegion for [{0}] is using [{1}] of it's buffer capacity";
                    if ((total > bufferSize / 2))
                        Log.Warning(logMsg + " The coordinator might not be available. You might want to check cluster membership status.", TypeName, 100 * total / bufferSize);
                    else
                        Log.Warning(logMsg, TypeName, 100 * total / bufferSize);
                }
            }
        }

        private void HandleShardRegionCommand(IShardRegionCommand command)
        {
            if (command is Retry)
            {
                if (ShardBuffers.Count != 0) _retryCount++;

                if (_coordinator == null) Register();
                else
                {
                    SendGracefulShutdownToCoordinator();
                    RequestShardBufferHomes();
                    TryCompleteGracefulShutdown();
                }
            }
            else if (command is GracefulShutdown)
            {
                Log.Debug("Starting graceful shutdown of region and all its shards");
                GracefulShutdownInProgres = true;
                SendGracefulShutdownToCoordinator();
                TryCompleteGracefulShutdown();
            }
            else Unhandled(command);
        }

        private void HandleShardRegionQuery(IShardRegionQuery query)
        {
            if (query is GetCurrentRegions)
            {
                if (_coordinator != null) _coordinator.Forward(query);
                else Sender.Tell(new CurrentRegions(new Address[0]));
            }
            else if (query is GetShardRegionState) ReplyToRegionStateQuery(Sender);
            else if(query is GetShardRegionStats) ReplyToRegionStatsQuery(Sender);
            else if (query is GetClusterShardingStats)
            {
                if(_coordinator != null) _coordinator.Tell(new ClusterShardingStats(new Dictionary<Address, ShardRegionStats>(0)));
            } 
            else Unhandled(query);
        }

        private void ReplyToRegionStateQuery(IActorRef sender)
        {
            AskAllShardsAsync<Shard.CurrentShardState>(Shard.GetCurrentShardState.Instance)
                .PipeTo(sender,
                    success: shardStates => new CurrentShardRegionState(new HashSet<ShardState>(shardStates.Select(x => new ShardState(x.Item1, x.Item2.EntityIds)))),
                    failure: err => new CurrentShardRegionState(new HashSet<ShardState>()));
        }

        private void ReplyToRegionStatsQuery(IActorRef sender)
        {
            AskAllShardsAsync<Shard.ShardStats>(Shard.GetShardStats.Instance)
                .PipeTo(sender,
                    success: shardStats => new ShardRegionStats(shardStats.ToDictionary(x => x.Item1, x => x.Item2.EntityCount)),
                    failure: err => new ShardRegionStats(new Dictionary<string, int>(0)));
        }

        private Task<Tuple<ShardId, T>[]> AskAllShardsAsync<T>(object message)
        {
            var timeout = TimeSpan.FromSeconds(3);
            var tasks = Shards.Select(entity => entity.Value.Ask<T>(message, timeout).ContinueWith(t => Tuple.Create(entity.Key, t.Result)));
            return Task.WhenAll(tasks);
        }

        private void TryCompleteGracefulShutdown()
        {
            if(GracefulShutdownInProgres && Shards.Count == 0 && ShardBuffers.Count == 0)
                Context.Stop(Self);     // all shards have been rebalanced, complete graceful shutdown
        }

        private void SendGracefulShutdownToCoordinator()
        {
            if (GracefulShutdownInProgres && _coordinator != null)
                _coordinator.Tell(new PersistentShardCoordinator.GracefulShutdownRequest(Self));
        }

        private void HandleCoordinatorMessage(PersistentShardCoordinator.ICoordinatorMessage message)
        {
            if (message is PersistentShardCoordinator.HostShard)
            {
                var shard = ((PersistentShardCoordinator.HostShard)message).Shard;
                Log.Debug("Host shard [{0}]", shard);
                RegionByShard = RegionByShard.SetItem(shard, Self);
                UpdateRegionShards(Self, shard);

                // Start the shard, if already started this does nothing
                GetShard(shard);

                Sender.Tell(new PersistentShardCoordinator.ShardStarted(shard));
            }
            else if (message is PersistentShardCoordinator.ShardHome)
            {
                var home = (PersistentShardCoordinator.ShardHome)message;
                Log.Debug("Shard [{0}] located at [{1}]", home.Shard, home.Ref);
                IActorRef region;

                if (RegionByShard.TryGetValue(home.Shard, out region))
                {
                    if (region.Equals(Self) && !home.Ref.Equals(Self))
                    {
                        // should not happen, inconsistency between ShardRegion and PersistentShardCoordinator
                        throw new IllegalStateException(string.Format("Unexpected change of shard [{0}] from self to [{1}]", home.Shard, home.Ref));
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
            }
            else if (message is PersistentShardCoordinator.RegisterAck)
            {
                _coordinator = ((PersistentShardCoordinator.RegisterAck)message).Coordinator;
                Context.Watch(_coordinator);
                RequestShardBufferHomes();
            }
            else if (message is PersistentShardCoordinator.BeginHandOff)
            {
                var shard = ((PersistentShardCoordinator.BeginHandOff)message).Shard;
                Log.Debug("Begin hand off shard [{0}]", shard);
                IActorRef regionRef;
                if (RegionByShard.TryGetValue(shard, out regionRef))
                {
                    IImmutableSet<ShardId> updatedShards;
                    if (!Regions.TryGetValue(regionRef, out updatedShards))
                        updatedShards = ImmutableHashSet<ShardId>.Empty;

                    updatedShards = updatedShards.Remove(shard);
                    if (updatedShards.Count == 0)
                        Regions = Regions.Remove(regionRef);
                    else
                        Regions = Regions.SetItem(regionRef, updatedShards);

                    RegionByShard = RegionByShard.Remove(shard);
                }

                Sender.Tell(new PersistentShardCoordinator.BeginHandOffAck(shard));
            }
            else if (message is PersistentShardCoordinator.HandOff)
            {
                var shard = ((PersistentShardCoordinator.HandOff)message).Shard;
                Log.Debug("Hand off shard [{0}]", shard);

                // must drop requests that came in between the BeginHandOff and now,
                // because they might be forwarded from other regions and there
                // is a risk or message re-ordering otherwise
                if (ShardBuffers.ContainsKey(shard))
                {
                    ShardBuffers = ShardBuffers.Remove(shard);
                    _loggedFullBufferWarning = false;
                }

                IActorRef actorRef;
                if (Shards.TryGetValue(shard, out actorRef))
                {
                    HandingOff = HandingOff.Add(actorRef);
                    actorRef.Forward(message);
                }
                else
                    Sender.Tell(new PersistentShardCoordinator.ShardStopped(shard));
            }
            else Unhandled(message);
        }

        private void UpdateRegionShards(IActorRef regionRef, string shard)
        {
            IImmutableSet<ShardId> shards;
            if (!Regions.TryGetValue(regionRef, out shards)) shards = ImmutableSortedSet<ShardId>.Empty;
            Regions = Regions.SetItem(regionRef, shards.Add(shard));
        }

        private void RequestShardBufferHomes()
        {
            if (_coordinator != null)
            {
                foreach (var buffer in ShardBuffers)
                {
                    var logMsg = "Retry request for shard [{0}] homes from coordinator at [{1}]. [{2}] buffered messages.";
                    if (_retryCount >= RetryCountThreshold)
                        Log.Warning(logMsg, buffer.Key, _coordinator, buffer.Value.Count);
                    else
                        Log.Debug(logMsg, buffer.Key, _coordinator, buffer.Value.Count);

                    _coordinator.Tell(new PersistentShardCoordinator.GetShardHome(buffer.Key));
                }
            }
        }

        private void DeliverBufferedMessage(ShardId shardId, IActorRef receiver)
        {
            IImmutableList<KeyValuePair<Msg, IActorRef>> buffer;
            if (ShardBuffers.TryGetValue(shardId, out buffer))
            {
                Log.Debug("Deliver [{0}] buffered messages for shard [{1}]", buffer.Count, shardId);

                foreach (var m in buffer)
                {
                    receiver.Tell(m.Key, m.Value);
                }
                ShardBuffers = ShardBuffers.Remove(shardId);
            }

            _loggedFullBufferWarning = false;
            _retryCount = 0;
        }

        private IActorRef GetShard(ShardId id)
        {
            //TODO: change on ConcurrentDictionary.GetOrAdd?
            IActorRef region = null;
            if (!Shards.TryGetValue(id, out region))
            {
                if (EntityProps == null || EntityProps.Equals(Actor.Props.Empty))
                {
                    throw new IllegalStateException("Shard must not be allocated to a proxy only ShardRegion");
                }
                else if (ShardsByRef.Values.All(shardId => shardId != id))
                {
                    Log.Debug("Starting shard [{0}] in region", id);

                    //val name = URLEncoder.encode(id, "utf-8")
                    var name = Uri.EscapeDataString(id);
                    var shardRef = Context.Watch(Context.ActorOf(PersistentShard.Props(
                        TypeName,
                        id,
                        EntityProps,
                        Settings,
                        IdExtractor,
                        ShardResolver,
                        HandOffStopMessage).WithDispatcher(Context.Props.Dispatcher), name));

                    ShardsByRef = ShardsByRef.SetItem(shardRef, id);
                    return shardRef;
                }
            }

            return region ?? ActorRefs.Nobody;
        }

        private void HandleClusterState(ClusterEvent.CurrentClusterState state)
        {
            var builder = ImmutableSortedSet<Member>.Empty.ToBuilder();
            builder.KeyComparer = AgeOrdering;
            var members = builder.ToImmutable()
                .Union(state.Members.Where(m => m.Status == MemberStatus.Up && MatchingRole(m)));

            ChangeMembers(members);
        }

        private void HandleClusterEvent(ClusterEvent.IClusterDomainEvent e)
        {
            if (e is ClusterEvent.MemberUp)
            {
                var m = ((ClusterEvent.MemberUp)e).Member;
                if (MatchingRole(m))
                    ChangeMembers(MembersByAge.Add(m));
            }
            else if (e is ClusterEvent.MemberRemoved)
            {
                var m = ((ClusterEvent.MemberRemoved)e).Member;
                if (m.UniqueAddress == Cluster.SelfUniqueAddress)
                    Context.Stop(Self);
                else if (MatchingRole(m))
                    ChangeMembers(MembersByAge.Remove(m));
            }
            else Unhandled(e);
        }

        private void HandleTerminated(Terminated terminated)
        {
            IImmutableSet<ShardId> shards;
            ShardId shard;
            if (_coordinator != null && _coordinator.Equals(terminated.ActorRef))
            {
                _coordinator = null;
            }
            else if (Regions.TryGetValue(terminated.ActorRef, out shards))
            {
                RegionByShard = RegionByShard.RemoveRange(shards);
                Regions = Regions.Remove(terminated.ActorRef);

                if (Log.IsDebugEnabled)
                    Log.Debug("Region [{0}] with shards [{1}] terminated", terminated.ActorRef, string.Join(", ", shards));
            }
            else if (ShardsByRef.TryGetValue(terminated.ActorRef, out shard))
            {
                ShardsByRef = ShardsByRef.Remove(terminated.ActorRef);
                Shards = Shards.Remove(shard);
                //Are we meant to be handing off, or is this a unknown stop?
                if (HandingOff.Contains(terminated.ActorRef))
                {
                    HandingOff = HandingOff.Remove(terminated.ActorRef);
                    Log.Debug("Shard [{0}] handoff complete", shard);
                }
                else
                {
                    // if persist fails it will stop
                    Log.Debug("Shard [{0}] terminated while not being handed off", shard);
                    if (Settings.RememberEntities)
                    {
                        Context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.ShardFailureBackoff, Self, new RestartShard(shard), Self);
                    }
                }

                TryCompleteGracefulShutdown();
            }
        }
    }

}