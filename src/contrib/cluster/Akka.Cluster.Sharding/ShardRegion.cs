using System;
using System.Collections.Concurrent;
using Akka.Actor;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    public interface IShardRegionCommand { }

    /**
    * If the state of the entries are persistent you may stop entries that are not used to
    * reduce memory consumption. This is done by the application specific implementation of
    * the entry actors for example by defining receive timeout (`context.setReceiveTimeout`).
    * If a message is already enqueued to the entry when it stops itself the enqueued message
    * in the mailbox will be dropped. To support graceful passivation without loosing such
    * messages the entry actor can send this `Passivate` message to its parent `ShardRegion`.
    * The specified wrapped `stopMessage` will be sent back to the entry, which is
    * then supposed to stop itself. Incoming messages will be buffered by the `ShardRegion`
    * between reception of `Passivate` and termination of the entry. Such buffered messages
    * are thereafter delivered to a new incarnation of the entry.
    *
    * [[akka.actor.PoisonPill]] is a perfectly fine `stopMessage`.
    */
    public sealed class Passivate : IShardRegionCommand
    {
        public Passivate(object stopMessage)
        {
            StopMessage = stopMessage;
        }

        public object StopMessage { get; private set; }
    }

    public sealed class Retry : IShardRegionCommand
    {
        public static readonly Retry Instance = new Retry();
        private Retry() { }
    }

    /**
     * This actor creates children entry actors on demand for the shards that it is told to be
     * responsible for. It delegates messages targeted to other shards to the responsible
     * `ShardRegion` actor on other nodes.
     *
     * @see [[ClusterSharding$ ClusterSharding extension]]
     */
    public class ShardRegion : ActorBase
    {
        private class MemberAgeComparer : IComparer<Member>
        {
            public static readonly IComparer<Member> Instance = new MemberAgeComparer();

            private MemberAgeComparer() { }

            public int Compare(Member x, Member y)
            {
                return x.IsOlderThan(y) ? 1 : -1;
            }
        }

        private readonly string _typeName;
        private readonly Props _entryProps;
        private readonly string _role;
        private readonly string _coordinatorPath;
        private readonly TimeSpan _retryInterval;
        private readonly TimeSpan _shardFailureBackoff;
        private readonly TimeSpan _entryRestartBackoff;
        private readonly TimeSpan _snapshotInterval;
        private readonly int _bufferSize;
        private readonly bool _rememberEntries;
        private readonly IdExtractor _idExtractor;
        private readonly ShardResolver _shardResolver;

        private readonly Cluster _cluster;
        private readonly IComparer<Member> _ageOrdering;
        private ISet<Member> _membersByAge;
        private IActorRef _coordinator = null;

        private readonly IDictionary<IActorRef, ICollection<ShardId>> _regions;
        private readonly IDictionary<ShardId, IActorRef> _regionByShard;
        private readonly IDictionary<ShardId, ICollection<BufferedMessage>> _shardBuffers;
        private readonly IDictionary<ShardId, IActorRef> _shards;
        private readonly IDictionary<IActorRef, ShardId> _shardsByRef;
        private readonly ISet<IActorRef> _handingOff;

        private readonly ICancelable _retryTask;
        private readonly ILoggingAdapter _log;

        public ShardRegion(string typeName, Props entryProps, string role, string coordinatorPath,
            TimeSpan retryInterval, TimeSpan shardFailureBackoff, TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval, int bufferSize, bool rememberEntries, IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            _typeName = typeName;
            _entryProps = entryProps;
            _role = role;
            _coordinatorPath = coordinatorPath;
            _retryInterval = retryInterval;
            _shardFailureBackoff = shardFailureBackoff;
            _entryRestartBackoff = entryRestartBackoff;
            _snapshotInterval = snapshotInterval;
            _bufferSize = bufferSize;
            _rememberEntries = rememberEntries;
            _idExtractor = idExtractor;
            _shardResolver = shardResolver;

            _cluster = Cluster.Get(Context.System);

            // sort by age, oldest first
            _ageOrdering = MemberAgeComparer.Instance;
            _membersByAge = new SortedSet<Member>(_ageOrdering);

            _regions = new ConcurrentDictionary<IActorRef, ICollection<ShardId>>();
            _regionByShard = new ConcurrentDictionary<string, IActorRef>();
            _shardBuffers = new ConcurrentDictionary<string, ICollection<BufferedMessage>>();
            _shards = new ConcurrentDictionary<string, IActorRef>();
            _shardsByRef = new ConcurrentDictionary<IActorRef, string>();
            _handingOff = new HashSet<IActorRef>();

            _log = Context.GetLogger();
            _retryTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(retryInterval, retryInterval, Self, Retry.Instance, Self);
        }

        public int TotalBufferSize
        {
            get { return _shardBuffers.Aggregate(0, (acc, entry) => acc + entry.Value.Count); }
        }

        protected ActorSelection CoordinatorSelection
        {
            get
            {
                var firstMember = _membersByAge.FirstOrDefault();
                return firstMember == null ? null : Context.ActorSelection(new RootActorPath(firstMember.Address) + _coordinatorPath);
            }
        }

        protected object RegistrationMessage
        {
            get
            {
                if (_entryProps != null && !_entryProps.Equals(Actor.Props.None))
                    return new Register(Self);
                else return new RegisterProxy(Self);
            }
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
            _retryTask.Cancel();
        }

        protected bool MatchingRole(Member member)
        {
            return _role == null || member.HasRole(_role);
        }

        private void ChangeMembers(ISet<Member> newMembers)
        {
            var before = _membersByAge.FirstOrDefault();
            var after = newMembers.FirstOrDefault();
            _membersByAge = newMembers;
            if (before != null && after != null && !before.Equals(after))
            {
                if (_log.IsDebugEnabled)
                    _log.Debug("Coordinator moved from [{0}] to [{1}]", before.Address, after.Address);
                _coordinator = null;
                Register();
            }
        }

        private void Register()
        {
            var coordinator = CoordinatorSelection;
            if (coordinator != null)
                coordinator.Tell(RegistrationMessage);
        }

        /**
        * Scala API: Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor.
        */
        public static Props Props(
            string typeName,
            Props entryProps,
            string role,
            string coordinatorPath,
            TimeSpan retryInterval,
            TimeSpan shardFailureBackoff,
            TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval,
            int bufferSize,
            bool rememberEntries,
            IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, entryProps, role, coordinatorPath, retryInterval, shardFailureBackoff,
                            entryRestartBackoff, snapshotInterval, bufferSize, rememberEntries, idExtractor,
                            shardResolver));
        }

        public static Props Props(
            string typeName,
            Props entryProps,
            string role,
            string coordinatorPath,
            TimeSpan retryInterval,
            TimeSpan shardFailureBackoff,
            TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval,
            int bufferSize,
            bool rememberEntries,
            IMessageExtractor messageExtractor)
        {
            IdExtractor idExtractor = messageExtractor.ToIdExtractor();
            ShardResolver shardResolver = ShardResolvers.Default;

            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, entryProps, role, coordinatorPath, retryInterval, shardFailureBackoff,
                            entryRestartBackoff, snapshotInterval, bufferSize, rememberEntries, idExtractor,
                            shardResolver));
        }


        /**
        * Scala API: Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor
        * when using it in proxy only mode.
        */
        public static Props ProxyProps(string typeName, string role, string coordinatorPath, TimeSpan retryInterval,
            int bufferSize, IdExtractor idExtractor, ShardResolver shardResolver)
        {
            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, null, role, coordinatorPath, retryInterval, TimeSpan.Zero,
                            TimeSpan.Zero, TimeSpan.Zero, bufferSize, false, idExtractor, shardResolver));
        }

        /**
        * Java API: : Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor
        * when using it in proxy only mode.
        */
        public static Props ProxyProps(string typeName, string role, string coordinatorPath, TimeSpan retryInterval,
            int bufferSize, IMessageExtractor messageExtractor)
        {
            IdExtractor idExtractor = messageExtractor.ToIdExtractor();
            ShardResolver shardResolver = ShardResolvers.Default;

            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, null, role, coordinatorPath, retryInterval, TimeSpan.Zero,
                            TimeSpan.Zero, TimeSpan.Zero, bufferSize, false, idExtractor, shardResolver));
        }

        protected override bool Receive(object message)
        {
            Tuple<string, object> extracted;
            if (message is Terminated) HandleTerminated(message as Terminated);
            else if (message is ClusterEvent.IClusterDomainEvent) HandleClusterEvent(message as ClusterEvent.IClusterDomainEvent);
            else if (message is ClusterEvent.CurrentClusterState) HandleClusterState(message as ClusterEvent.CurrentClusterState);
            else if (message is ICoordinatorMessage) HandleCoordinatorMessage(message as ICoordinatorMessage);
            else if (message is IShardRegionCommand) HandleShardRegionCommand(message as IShardRegionCommand);
            else if ((extracted = _idExtractor(message)) != null) DeliverMessage(message, Sender);
            else return false;
            return true;
        }

        private void DeliverMessage(object message, IActorRef sender)
        {
            var shard = _shardResolver(message);
            IActorRef region;
            if (_regionByShard.TryGetValue(shard, out region))
            {
                if (region.Equals(Self))
                {
                    GetShard(shard).Tell(message, sender);
                }
                else
                {
                    _log.Debug("Forwarding request for shard [{0}] to [{1}]", shard, region);
                    region.Tell(message, sender);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(shard))
                {
                    _log.Warning("Shard must not be empty, dropping message [{0}]", message.GetType());
                    Context.System.DeadLetters.Tell(message);
                }
                else
                {
                    if (!_shardBuffers.ContainsKey(shard))
                    {
                        _log.Debug("Request shard [{0}] home", shard);
                        if (_coordinator != null) _coordinator.Tell(new GetShardHome(shard));
                    }

                    if (TotalBufferSize >= _bufferSize)
                    {
                        _log.Debug("Buffer is full, dropping message for shard [{0}]", shard);
                        Context.System.DeadLetters.Tell(message);
                    }
                    else
                    {
                        ICollection<BufferedMessage> buffer;
                        if (_shardBuffers.TryGetValue(shard, out buffer))
                        {
                            buffer.Add(new BufferedMessage(message, sender));
                        }
                        else
                        {
                            _shardBuffers.Add(shard, new List<BufferedMessage> { new BufferedMessage(message, sender) });
                        }
                    }
                }
            }
        }

        private void HandleShardRegionCommand(IShardRegionCommand command)
        {
            if (command is Retry)
            {
                if (_coordinator == null) Register();
                else RequestShardBufferHomes();
            }
            else Unhandled(command);
        }

        private void HandleCoordinatorMessage(ICoordinatorMessage message)
        {
            if (message is HostShard)
            {
                var shard = ((HostShard)message).Shard;
                _log.Debug("Host shard [{0}]", shard);
                _regionByShard.Add(shard, Self);
                UpdateRegions(shard, Self);

                // Start the shard, if already started this does nothing
                GetShard(shard);
                DeliverBufferedMessage(shard);

                Sender.Tell(new ShardStarted(shard));
            }
            else if (message is ShardHome)
            {
                var msg = (ShardHome)message;
                _log.Debug("Shard [{0}] located at [{1}]", msg.Shard, msg.Ref);
                IActorRef region;

                if (_regionByShard.TryGetValue(msg.Shard, out region))
                {
                    if (region.Equals(Self) && !msg.Ref.Equals(Self))
                    {
                        // should not happen, inconsistency between ShardRegion and ShardCoordinator
                        throw new IllegalStateException(string.Format("Unexpected change of shard [{0}] from self to [{1}]", msg.Shard, msg.Ref));
                    }
                }

                _regionByShard.Add(msg.Shard, msg.Ref);
                UpdateRegions(msg.Shard, msg.Ref);

                if (!msg.Ref.Equals(Self)) Context.Watch(msg.Ref);

                DeliverBufferedMessage(msg.Shard);
            }
            else if (message is RegisterAck)
            {
                _coordinator = ((RegisterAck)message).Coordinator;
                Context.Watch(_coordinator);
                RequestShardBufferHomes();
            }
            else if (message is SnapshotTick)
            {
                foreach (var child in Context.GetChildren())
                {
                    child.Tell(message);
                }
            }
            else if (message is BeginHandOff)
            {
                var shard = ((BeginHandOff)message).Shard;
                _log.Debug("Begin hand off shard [{0}]", shard);
                IActorRef region;
                if (_regionByShard.TryGetValue(shard, out region))
                {
                    var updatedShards = _regions[region];
                    updatedShards.Remove(shard);
                    if (updatedShards.Count == 0) _regions.Remove(region);

                    _regionByShard.Remove(shard);
                }

                Sender.Tell(new BeginHandOffAck(shard));
            }
            else if (message is HandOff)
            {
                var shard = ((HandOff)message).Shard;
                _log.Debug("Hand off shard [{0}]", shard);

                // must drop requests that came in between the BeginHandOff and now,
                // because they might be forwarded from other regions and there
                // is a risk or message re-ordering otherwise
                _shardBuffers.Remove(shard);

                IActorRef actorRef;
                if (_shards.TryGetValue(shard, out actorRef))
                {
                    _handingOff.Add(actorRef);
                    actorRef.Forward(message);
                }
                else
                {
                    Sender.Tell(new ShardStopped(shard));
                }
            }
            else Unhandled(message);
        }

        private void RequestShardBufferHomes()
        {
            if (_coordinator != null)
            {
                foreach (var buffer in _shardBuffers)
                {
                    _log.Debug("Retry request for shard [{0}] homes", buffer.Key);
                    _coordinator.Tell(new GetShardHome(buffer.Key));
                }
            }
        }

        private void UpdateRegions(string shard, IActorRef actorRef)
        {
            ICollection<ShardId> vec;
            if (_regions.TryGetValue(actorRef, out vec))
            {
                vec.Add(shard);
            }
            else
            {
                _regions.Add(Self, new HashSet<ShardId> { shard });
            }
        }

        private void DeliverBufferedMessage(ShardId shard)
        {
            ICollection<BufferedMessage> buffer;
            if (_shardBuffers.TryGetValue(shard, out buffer))
            {
                foreach (var bufferedMessage in buffer)
                {
                    DeliverMessage(bufferedMessage.Message, bufferedMessage.ActorRef);
                }
                _shardBuffers.Remove(shard);
            }
        }

        private IActorRef GetShard(ShardId shard)
        {
            //TODO: change on ConcurrentDictionary.GetOrAdd?
            IActorRef region;
            if (!_shards.TryGetValue(shard, out region))
            {
                if (_entryProps == null || _entryProps.Equals(Actor.Props.Empty))
                {
                    throw new IllegalStateException("Shard must not be allocated to a proxy only ShardRegion");
                }
                else
                {
                    _log.Debug("Starting shard [{0}] in region", shard);

                    //val name = URLEncoder.encode(id, "utf-8")
                    var name = shard;
                    var shardRef = Context.Watch(Context.ActorOf(Actor.Props.Create(() =>
                        new Shard(_typeName,
                        shard,
                        _entryProps,
                        _shardFailureBackoff,
                        _entryRestartBackoff,
                        _snapshotInterval,
                        _bufferSize,
                        _rememberEntries,
                        _idExtractor,
                        _shardResolver)), name));

                    _shards.Add(shard, shardRef);
                    _shardsByRef.Add(shardRef, shard);
                    return shardRef;
                }
            }
            else return region;
        }

        private void HandleClusterState(ClusterEvent.CurrentClusterState state)
        {
            var newMembers = new SortedSet<Member>(_ageOrdering);
            foreach (var member in state.Members.Where(m => m.Status == MemberStatus.Up && MatchingRole(m)))
            {
                newMembers.Add(member);
            }

            ChangeMembers(newMembers);
        }

        private void HandleClusterEvent(ClusterEvent.IClusterDomainEvent e)
        {
            if (e is ClusterEvent.MemberUp)
            {
                var m = ((ClusterEvent.MemberUp)e).Member;
                if (MatchingRole(m))
                {
                    _membersByAge.Add(m);
                    ChangeMembers(_membersByAge);
                }
            }
            else if (e is ClusterEvent.MemberRemoved)
            {
                var m = ((ClusterEvent.MemberRemoved)e).Member;
                if (m.UniqueAddress == _cluster.SelfUniqueAddress)
                    Context.Stop(Self);
                else if (MatchingRole(m))
                {
                    _membersByAge.Remove(m);
                    ChangeMembers(_membersByAge);
                }
            }
            else Unhandled(e);
        }

        private void HandleTerminated(Terminated terminated)
        {
            ICollection<ShardId> shards;
            ShardId shard;
            if (_coordinator != null && _coordinator.Equals(terminated.ActorRef))
            {
                _coordinator = null;
            }
            else if (_regions.TryGetValue(terminated.ActorRef, out shards))
            {
                foreach (var s in shards)
                {
                    _regionByShard.Remove(s);
                }
                _regions.Remove(terminated.ActorRef);
                if (_log.IsDebugEnabled)
                    _log.Debug("Region [{0}] with shards [{1}] terminated", terminated.ActorRef, string.Join(", ", shards));
            }
            else if (_shardsByRef.TryGetValue(terminated.ActorRef, out shard))
            {
                //Are we meant to be handing off, or is this a unknown stop?
                if (_handingOff.Contains(terminated.ActorRef))
                {
                    _shardsByRef.Remove(terminated.ActorRef);
                    _shards.Remove(shard);
                    _handingOff.Remove(terminated.ActorRef);

                    _log.Debug("Shard [{0}] handoff complete", shard);
                }
                else
                {
                    throw new IllegalStateException(string.Format("Shard [{0}] terminated while not being handed off", shard));
                }
            }
        }
    }

}