using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Remote;
using Akka.Util.Internal;

namespace Akka.Cluster
{

    /// <summary>
    /// INTERNAL API
    /// 
    /// Receives <see cref="ClusterHeartbeatSender.Heartbeat"/> messages and replies.
    /// </summary>
    internal sealed class ClusterHeartbeatReceiver : ReceiveActor
    {
        private readonly ClusterHeartbeatSender.HeartbeatRsp _selfHeartbeatRsp;

        public ClusterHeartbeatReceiver()
        {
            _selfHeartbeatRsp = new ClusterHeartbeatSender.HeartbeatRsp(Cluster.Get(Context.System).SelfUniqueAddress);
            Receive<ClusterHeartbeatSender.Heartbeat>(heartbeat => Sender.Tell(_selfHeartbeatRsp));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ClusterHeartbeatSender : ReceiveActor
    {
        private readonly CancellationTokenSource _heartbeatTask;

        public IFailureDetectorRegistry<Address> FailureDetector
        {
            get { return _cluster.FailureDetector; }
        }

        private ClusterHeartbeatSenderState _state;

        private Heartbeat _selfHeartbeat;

        private readonly Cluster _cluster;

        private readonly LoggingAdapter _log = Context.GetLogger();

        public ClusterHeartbeatSender()
        {
            _cluster = Cluster.Get(Context.System);
            _selfHeartbeat = new Heartbeat(_cluster.SelfAddress);
            _state = new ClusterHeartbeatSenderState(
                new HeartbeatNodeRing(_cluster.SelfUniqueAddress, new[] { _cluster.SelfUniqueAddress },
                    _cluster.Settings.MonitoredByNrOfMembers),
                ImmutableHashSet.Create<UniqueAddress>(),
                FailureDetector);

            //stat perioidic heartbeat to other nodes in cluster
            _heartbeatTask = new CancellationTokenSource();
            Context.System.Scheduler.Schedule(
                _cluster.Settings.PeriodicTasksInitialDelay.Max(_cluster.Settings.HeartbeatInterval),
                _cluster.Settings.HeartbeatInterval, Self, new HeartbeatTick());
            Initializing();
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            foreach (var receiver in _state.ActiveReceivers)
            {
                FailureDetector.Remove(receiver.Address);
            }
            _heartbeatTask.Cancel();
            _cluster.Unsubscribe(Self);
        }

        private void Initializing()
        {
            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                Init(state);
                Become(Active);
            });
            Receive<HeartbeatTick>(tick => { }); //do nothing
        }

        private void Active()
        {
            Receive<HeartbeatTick>(tick => DoHeartbeat());
            Receive<HeartbeatRsp>(rsp => DoHeartbeatRsp(rsp.From));
            Receive<ClusterEvent.MemberUp>(up => AddMember(up.Member));
            Receive<ClusterEvent.MemberRemoved>(removed => RemoveMember(removed.Member));
            Receive<ClusterEvent.IMemberEvent>(@event => { }); //we don't care about other member evets
            Receive<ExpectedFirstHeartbeat>(heartbeat => TriggerFirstHeart(heartbeat.From));
        }

        /// <summary>
        /// Looks up and returns the remote cluster heartbeat connection for the specific address.
        /// </summary>
        private ActorSelection HeartbeatReceiver(Address address)
        {
            return Context.ActorSelection(new RootActorPath(address)/"system"/"cluster"/"heartbeatReceiver");
        }

        private void Init(ClusterEvent.CurrentClusterState snapshot)
        {
            var nodes = snapshot.Members.Where(x => x.Status == MemberStatus.Up).Select(x => x.UniqueAddress).ToImmutableHashSet();
            _state = _state.Init(nodes);
        }

        private void AddMember(Member m)
        {
            if (m.UniqueAddress != _cluster.SelfUniqueAddress)
                _state = _state.AddMember(m.UniqueAddress);
        }

        private void RemoveMember(Member m)
        {
            if (m.UniqueAddress == _cluster.SelfUniqueAddress)
            {
                // This cluster node will be shutdown, but stop this actor immediately
                // to avoid further updates
                Context.Stop(Self);
            }
            else
            {
                _state = _state.RemoveMember(m.UniqueAddress);
            }
        }

        private void DoHeartbeat()
        {
            foreach (var to in _state.ActiveReceivers)
            {
                if (FailureDetector.IsMonitoring(to.Address))
                    _log.Debug("Cluster Node [{0}] - Heartbeat to [{1}]", _cluster.SelfAddress, to.Address);
                else
                {
                    _log.Debug("Cluster Node [{0}] - First Heartbeat to [{1}]", _cluster.SelfAddress, to.Address);
                    // schedule the expected first heartbeat for later, which will give the
                    // other side a chance to reply, and also trigger some resends if needed
                    Context.System.Scheduler.ScheduleOnce(_cluster.Settings.HeartbeatExpectedResponseAfter, Self,
                        new ExpectedFirstHeartbeat(to));
                }
                HeartbeatReceiver(to.Address).Tell(_selfHeartbeat);
            }
        }

        private void DoHeartbeatRsp(UniqueAddress from)
        {
            _log.Debug("Cluster Node [{0}] - Heartbeat response from [{1}]", _cluster.SelfAddress, from.Address);
            _state = _state.HeartbeatRsp(from);
        }

        private void TriggerFirstHeart(UniqueAddress from)
        {
            if (_state.ActiveReceivers.Contains(from) && !FailureDetector.IsMonitoring(from.Address))
            {
                _log.Debug("Cluster Node [{0}] - Trigger extra expected heartbeat from [{1}]", _cluster.SelfAddress, from.Address);
                FailureDetector.Heartbeat(from.Address);
            }
        }

        #region Messaging classes

        /// <summary>
        /// Sent at regular intervals for failure detection
        /// </summary>
        internal sealed class Heartbeat : IClusterMessage
        {
            public Heartbeat(Address @from)
            {
                From = @from;
            }

            public Address From { get; private set; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            public override bool Equals(object obj)
#pragma warning restore 659
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Heartbeat && Equals((Heartbeat)obj);
            }

            private bool Equals(Heartbeat other)
            {
                return Equals(From, other.From);
            }
        }

        /// <summary>
        /// Sends replies to <see cref="Heartbeat"/> messages
        /// </summary>
        internal sealed class HeartbeatRsp : IClusterMessage
        {
            public HeartbeatRsp(UniqueAddress @from)
            {
                From = @from;
            }

            public UniqueAddress From { get; private set; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            public override bool Equals(object obj)
#pragma warning restore 659
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is HeartbeatRsp && Equals((HeartbeatRsp)obj);
            }

            private bool Equals(HeartbeatRsp other)
            {
                return Equals(From, other.From);
            }
        }

        /// <summary>
        /// Sent to self only
        /// </summary>
        private class HeartbeatTick { }

        internal sealed class ExpectedFirstHeartbeat
        {
            public ExpectedFirstHeartbeat(UniqueAddress @from)
            {
                From = @from;
            }

            public UniqueAddress From { get; private set; }
        }

        #endregion


    }

    /// <summary>
    /// INTERNAL API
    /// State of <see cref="ClusterHeartbeatSender"/>. Encapsulated to facilitate unit testing.
    /// It is immutable, but it updates the failure detector.
    /// </summary>
    internal sealed class ClusterHeartbeatSenderState
    {
        public ClusterHeartbeatSenderState(HeartbeatNodeRing ring, ImmutableHashSet<UniqueAddress> unreachable, IFailureDetectorRegistry<Address> failureDetector)
        {
            FailureDetector = failureDetector;
            Unreachable = unreachable;
            Ring = ring;
            ActiveReceivers = Ring.MyReceivers.Value.Union(Unreachable);
        }

        public HeartbeatNodeRing Ring { get; private set; }

        public ImmutableHashSet<UniqueAddress> Unreachable { get; private set; }

        public IFailureDetectorRegistry<Address> FailureDetector { get; private set; }

        public readonly ImmutableHashSet<UniqueAddress> ActiveReceivers;

        public UniqueAddress SelfAddress { get { return Ring.SelfAddress; } }

        public ClusterHeartbeatSenderState Copy(HeartbeatNodeRing ring = null, ImmutableHashSet<UniqueAddress> unreachable = null, IFailureDetectorRegistry<Address> failureDetector = null)
        {
            return new ClusterHeartbeatSenderState(ring ?? Ring, unreachable ?? Unreachable, failureDetector ?? FailureDetector);
        }

        public ClusterHeartbeatSenderState Init(ImmutableHashSet<UniqueAddress> nodes)
        {
            return Copy(ring: Ring.Copy(nodes: nodes.Add(SelfAddress)));
        }

        public ClusterHeartbeatSenderState AddMember(UniqueAddress node)
        {
            return MembershipChange(Ring + node);
        }

        public ClusterHeartbeatSenderState RemoveMember(UniqueAddress node)
        {
            var newState = MembershipChange(Ring - node);
            FailureDetector.Remove(node.Address);
            if (newState.Unreachable.Contains(node))
                return newState.Copy(unreachable: newState.Unreachable.Remove(node));
            return newState;
        }

        private ClusterHeartbeatSenderState MembershipChange(HeartbeatNodeRing newRing)
        {
            var oldReceivers = Ring.MyReceivers.Value;
            var removedReceivers = oldReceivers.Except(newRing.MyReceivers.Value);
            var newUnreachable = Unreachable;
            foreach (var r in removedReceivers)
            {
                if (FailureDetector.IsAvailable(r.Address))
                    FailureDetector.Remove(r.Address);
                else
                {
                    newUnreachable = newUnreachable.Add(r);
                }
            }
            return Copy(newRing, newUnreachable);
        }

        public ClusterHeartbeatSenderState HeartbeatRsp(UniqueAddress from)
        {
            if (ActiveReceivers.Contains(from))
            {
                FailureDetector.Heartbeat(from.Address);
                if (Unreachable.Contains(from))
                {
                    //back from unreachable, ok to stop heartbeating to it
                    if (!Ring.MyReceivers.Value.Contains(from))
                    {
                        FailureDetector.Remove(from.Address);
                    }
                    return Copy(unreachable: Unreachable.Remove(from));
                }
                return this;
            }
            return this;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Data structure for picking heartbeat receivers. The node ring is shuffled
    /// by deterministic hashing to avoid picking physically co-located neighbors.
    /// 
    /// It is immutable, i.e. the methods all return new instances.
    /// </summary>
    internal sealed class HeartbeatNodeRing
    {
        public HeartbeatNodeRing(UniqueAddress selfAddress, IEnumerable<UniqueAddress> nodes,
            int monitoredByNumberOfNodes)
            : this(selfAddress, ImmutableSortedSet.Create(nodes.ToArray()), monitoredByNumberOfNodes)
        {

        }

        public HeartbeatNodeRing(UniqueAddress selfAddress, ImmutableSortedSet<UniqueAddress> nodes, int monitoredByNumberOfNodes)
        {
            MonitoredByNumberOfNodes = monitoredByNumberOfNodes;
            NodeRing = nodes;
            SelfAddress = selfAddress;
            _useAllAsReceivers = MonitoredByNumberOfNodes >= (NodeRing.Count - 1);
            MyReceivers = new Lazy<ImmutableHashSet<UniqueAddress>>(() => Receivers(SelfAddress));
        }

        public UniqueAddress SelfAddress { get; private set; }

        public ImmutableSortedSet<UniqueAddress> NodeRing { get; private set; }

        public int MonitoredByNumberOfNodes { get; private set; }

        /// <summary>
        /// Receivers for <see cref="SelfAddress"/>. Cached for subsequent access.
        /// </summary>
        public readonly Lazy<ImmutableHashSet<UniqueAddress>> MyReceivers;

        private readonly bool _useAllAsReceivers;

        public ImmutableHashSet<UniqueAddress> Receivers(UniqueAddress sender)
        {
            if (_useAllAsReceivers)
                return NodeRing.Remove(sender).ToImmutableHashSet();
            var slice = NodeRing.From(sender).Skip(1).Take(MonitoredByNumberOfNodes).ToList(); //grab members furthest from this peer
            if (slice.Count < MonitoredByNumberOfNodes)
            {
                slice = slice.Concat(NodeRing.Take(MonitoredByNumberOfNodes - slice.Count)).ToList();
            }
            return slice.ToImmutableHashSet();
        }

        public HeartbeatNodeRing Copy(UniqueAddress selfAddress = null, IEnumerable<UniqueAddress> nodes = null,
            int? monitoredByNumberOfNodes = null)
        {
            return new HeartbeatNodeRing(selfAddress ?? SelfAddress,
                nodes ?? NodeRing,
                monitoredByNumberOfNodes.HasValue ? monitoredByNumberOfNodes.Value : MonitoredByNumberOfNodes);
        }

        #region Operators

        public static HeartbeatNodeRing operator +(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.NodeRing.Contains(node) ? ring : ring.Copy(nodes: ring.NodeRing.Add(node));
        }

        public static HeartbeatNodeRing operator -(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.NodeRing.Contains(node) ? ring.Copy(nodes: ring.NodeRing.Remove(node)) : ring;
        }

        #endregion
    }
}
