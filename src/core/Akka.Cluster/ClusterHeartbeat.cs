//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Receives <see cref="ClusterHeartbeatSender.Heartbeat"/> messages and replies.
    /// </summary>
    internal sealed class ClusterHeartbeatReceiver : UntypedActor
    {
        // Important - don't use Cluster.Get(Context.System) in constructor because that would
        // cause deadlock. See startup sequence in ClusterDaemon.
        private readonly Cluster _cluster;

        public bool VerboseHeartbeat => _cluster.Settings.VerboseHeartbeatLogging;
        
        public ClusterHeartbeatReceiver(Cluster cluster)
        {
            _cluster = cluster;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterHeartbeatSender.Heartbeat hb:
                    if (VerboseHeartbeat) _cluster.CurrentInfoLogger.LogDebug($"Heartbeat from [{hb.From}] - Sequence number [{hb.SequenceNr.ToString(CultureInfo.InvariantCulture)}]");
                    Sender.Tell(new ClusterHeartbeatSender.HeartbeatRsp(_cluster.SelfUniqueAddress,
                        hb.SequenceNr, hb.CreationTimeNanos));
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        public static Props Props(Cluster getCluster)
        {
            return Akka.Actor.Props.Create(() => new ClusterHeartbeatReceiver(getCluster));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ClusterHeartbeatSender : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Cluster _cluster;
        private readonly IFailureDetectorRegistry<Address> _failureDetector;
        private ClusterHeartbeatSenderState _state;
        private readonly ICancelable _heartbeatTask;

        // used for logging warning if actual tick interval is unexpected (e.g. due to starvation)
        private DateTime _tickTimestamp;

        /// <summary>
        /// Create a new instance of the <see cref="ClusterHeartbeatSender"/> and pass in a reference to the <see cref="Cluster"/>
        /// to which it belongs.
        /// </summary>
        public ClusterHeartbeatSender(Cluster cluster)
        {
            _cluster = cluster;
            var tickInitialDelay = _cluster.Settings.PeriodicTasksInitialDelay.Max(_cluster.Settings.HeartbeatInterval);
            _tickTimestamp = DateTime.UtcNow + tickInitialDelay;

            // the failureDetector is only updated by this actor, but read from other places
            _failureDetector = _cluster.FailureDetector;

            _state = new ClusterHeartbeatSenderState(
                ring: new HeartbeatNodeRing(
                    _cluster.SelfUniqueAddress,
                    ImmutableHashSet.Create(_cluster.SelfUniqueAddress),
                    ImmutableHashSet<UniqueAddress>.Empty,
                    _cluster.Settings.MonitoredByNrOfMembers),
                oldReceiversNowUnreachable: ImmutableHashSet<UniqueAddress>.Empty,
                failureDetector: _failureDetector);

            // start periodic heartbeat to other nodes in cluster
            _heartbeatTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                tickInitialDelay,
                _cluster.Settings.HeartbeatInterval,
                Self,
                new HeartbeatTick(),
                Self);

            Initializing();
        }

        private long _seqNo;
        private Heartbeat SelfHeartbeat()
        {
            _seqNo += 1;
            return new Heartbeat(_cluster.SelfAddress, _seqNo, MonotonicClock.GetNanos());
        }
        
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent));
        }
        
        protected override void PostStop()
        {
            foreach (var receiver in _state.ActiveReceivers)
            {
                _failureDetector.Remove(receiver.Address);
            }
            _heartbeatTask.Cancel();
            _cluster.Unsubscribe(Self);
        }

        /// <summary>
        /// Looks up and returns the remote cluster heartbeat connection for the specific address.
        /// </summary>
        protected virtual ActorSelection HeartbeatReceiver(Address address)
        {
            return Context.ActorSelection(new RootActorPath(address) / "system" / "cluster" / "heartbeatReceiver");
        }

        private void Initializing()
        {
            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                Init(state);
                Become(Active);
            });
            Receive<HeartbeatTick>(_ =>
            {
                _tickTimestamp = DateTime.UtcNow; // start checks when active
            }); //do nothing
        }

        private void Active()
        {
            Receive<HeartbeatTick>(_ => DoHeartbeat());
            Receive<HeartbeatRsp>(rsp => DoHeartbeatRsp(rsp));
            Receive<ClusterEvent.MemberRemoved>(removed => RemoveMember(removed.Member));
            Receive<ClusterEvent.IMemberEvent>(evt => AddMember(evt.Member));
            Receive<ClusterEvent.UnreachableMember>(m => UnreachableMember(m.Member));
            Receive<ClusterEvent.ReachableMember>(m => ReachableMember(m.Member));
            Receive<ExpectedFirstHeartbeat>(heartbeat => TriggerFirstHeart(heartbeat.From));
        }

        private void Init(ClusterEvent.CurrentClusterState snapshot)
        {
            var nodes = snapshot.Members.Select(x => x.UniqueAddress).ToImmutableHashSet();
            var unreachable = snapshot.Unreachable.Select(c => c.UniqueAddress).ToImmutableHashSet();
            _state = _state.Init(nodes, unreachable);
        }

        private void AddMember(Member m)
        {
            if (!m.UniqueAddress.Equals(_cluster.SelfUniqueAddress) && !_state.Contains(m.UniqueAddress))
                _state = _state.AddMember(m.UniqueAddress);
        }

        private void RemoveMember(Member m)
        {
            if (m.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
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

        private void UnreachableMember(Member m)
        {
            _state = _state.UnreachableMember(m.UniqueAddress);
        }

        private void ReachableMember(Member m)
        {
            _state = _state.ReachableMember(m.UniqueAddress);
        }

        private void DoHeartbeat()
        {
            foreach (var to in _state.ActiveReceivers)
            {
                if (_failureDetector.IsMonitoring(to.Address))
                {
                    if (_cluster.Settings.VerboseHeartbeatLogging)
                    {
                        _log.Debug("Cluster Node [{0}] - Heartbeat to [{1}]", _cluster.SelfAddress, to.Address);
                    }
                }
                else
                {
                    if (_cluster.Settings.VerboseHeartbeatLogging)
                    {
                        _log.Debug("Cluster Node [{0}] - First Heartbeat to [{1}]", _cluster.SelfAddress, to.Address);
                    }

                    // schedule the expected first heartbeat for later, which will give the
                    // other side a chance to reply, and also trigger some resends if needed
#pragma warning disable AK1004
                    Context.System.Scheduler.ScheduleTellOnce(
                        _cluster.Settings.HeartbeatExpectedResponseAfter,
                        Self,
                        new ExpectedFirstHeartbeat(to),
                        Self);
#pragma warning restore AK1004
                }
                HeartbeatReceiver(to.Address).Tell(SelfHeartbeat());
            }

            CheckTickInterval();
        }

        private void CheckTickInterval()
        {
            var now = DateTime.UtcNow;
            var doubleHeartbeatInterval = _cluster.Settings.HeartbeatInterval + _cluster.Settings.HeartbeatInterval;
            if (now - _tickTimestamp >= doubleHeartbeatInterval)
            {
                _log.Warning(
                    "Cluster Node [{0}] - Scheduled sending of heartbeat was delayed. " +
                    "Previous heartbeat was sent [{1}] ms ago, expected interval is [{2}] ms. This may cause failure detection " +
                    "to mark members as unreachable. The reason can be thread starvation, e.g. by running blocking tasks on the " +
                    "default dispatcher, CPU overload, or GC.",
                    _cluster.SelfAddress, (now - _tickTimestamp).TotalMilliseconds.ToString(CultureInfo.InvariantCulture), 
                    _cluster.Settings.HeartbeatInterval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            }
            
            _tickTimestamp = DateTime.UtcNow;
        }

        private void DoHeartbeatRsp(HeartbeatRsp rsp)
        {
            if (_cluster.Settings.VerboseHeartbeatLogging)
            {
                _log.Debug("Cluster Node [{0}] - Heartbeat response from [{1}] - Sequence number [{2}] - Creation time [{3}]", _cluster.SelfAddress, rsp.From.Address,
                    rsp.SequenceNr.ToString(CultureInfo.InvariantCulture), new TimeSpan(rsp.CreationTimeNanos.ToTicks()).ToString());
            }
            _state = _state.HeartbeatRsp(rsp.From);
        }

        private void TriggerFirstHeart(UniqueAddress from)
        {
            if (_state.ActiveReceivers.Contains(from) && !_failureDetector.IsMonitoring(from.Address))
            {
                if (_cluster.Settings.VerboseHeartbeatLogging)
                {
                    _log.Debug("Cluster Node [{0}] - Trigger extra expected heartbeat from [{1}]", _cluster.SelfAddress, from.Address);
                }
                _failureDetector.Heartbeat(from.Address);
            }
        }

        #region Messaging classes

        // <Heartbeat>
        /// <summary>
        /// Sent at regular intervals for failure detection
        /// </summary>
        internal sealed class Heartbeat : IClusterMessage, IPriorityMessage, IDeadLetterSuppression, IEquatable<Heartbeat>
        {
            public Heartbeat(Address from, long sequenceNr, long creationTimeNanos)
            {
                From = from;
                SequenceNr = sequenceNr;
                CreationTimeNanos = creationTimeNanos;
            }

            public Address From { get; }

            public long SequenceNr { get; }

            public long CreationTimeNanos { get; }

            public bool Equals(Heartbeat other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return From.Equals(other.From) && SequenceNr == other.SequenceNr && CreationTimeNanos == other.CreationTimeNanos;
            }

            public override bool Equals(object obj)
            {
                return ReferenceEquals(this, obj) || obj is Heartbeat other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = From.GetHashCode();
                    hashCode = (hashCode * 397) ^ SequenceNr.GetHashCode();
                    hashCode = (hashCode * 397) ^ CreationTimeNanos.GetHashCode();
                    return hashCode;
                }
            }
        }
        // </Heartbeat>

        /// <summary>
        /// Sends replies to <see cref="Heartbeat"/> messages
        /// </summary>
        internal sealed class HeartbeatRsp : IClusterMessage, IPriorityMessage, IDeadLetterSuppression, IEquatable<HeartbeatRsp>
        {
            public HeartbeatRsp(UniqueAddress from, long sequenceNr, long creationTimeNanos)
            {
                From = from;
                SequenceNr = sequenceNr;
                CreationTimeNanos = creationTimeNanos;
            }

            public UniqueAddress From { get; }

            public long SequenceNr { get; }

            public long CreationTimeNanos { get; }

            public bool Equals(HeartbeatRsp other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return From.Equals(other.From) && SequenceNr == other.SequenceNr 
                                               && CreationTimeNanos == other.CreationTimeNanos;
            }

            public override bool Equals(object obj)
            {
                return ReferenceEquals(this, obj) || obj is HeartbeatRsp other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = From.GetHashCode();
                    hashCode = (hashCode * 397) ^ SequenceNr.GetHashCode();
                    hashCode = (hashCode * 397) ^ CreationTimeNanos.GetHashCode();
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// Sent to self only
        /// </summary>
        private class HeartbeatTick { }

        /// <summary>
        /// Message used to verify initial contact with a heartbeat receiver.
        /// </summary>
        internal sealed class ExpectedFirstHeartbeat
        {
            /// <summary>
            /// Creates a message requesting an initial heartbeat check for a receiver.
            /// </summary>
            /// <param name="from">The unique address of the receiver expected to respond.</param>
            public ExpectedFirstHeartbeat(UniqueAddress from)
            {
                From = from;
            }

            /// <summary>
            /// The receiver whose first heartbeat response is expected.
            /// </summary>
            public UniqueAddress From { get; }
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
        /// <summary>
        /// Creates sender state from a heartbeat ring, previously unreachable receivers, and a failure detector.
        /// </summary>
        /// <param name="ring">The ring used to select this node's heartbeat receivers.</param>
        /// <param name="oldReceiversNowUnreachable">Receivers no longer selected by the ring but still tracked as unreachable.</param>
        /// <param name="failureDetector">The failure detector registry updated by heartbeat responses and membership changes.</param>
        public ClusterHeartbeatSenderState(HeartbeatNodeRing ring, ImmutableHashSet<UniqueAddress> oldReceiversNowUnreachable, IFailureDetectorRegistry<Address> failureDetector)
        {
            Ring = ring;
            OldReceiversNowUnreachable = oldReceiversNowUnreachable;
            FailureDetector = failureDetector;
            ActiveReceivers = Ring.MyReceivers.Value.Union(OldReceiversNowUnreachable);
        }

        /// <summary>
        /// The current ring used to select heartbeat receivers.
        /// </summary>
        public HeartbeatNodeRing Ring { get; }

        /// <summary>
        /// Previously selected receivers that are now unreachable and remain tracked until they respond or are removed.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> OldReceiversNowUnreachable { get; }

        /// <summary>
        /// The failure detector registry updated by this state.
        /// </summary>
        public IFailureDetectorRegistry<Address> FailureDetector { get; }

        /// <summary>
        /// The union of current ring receivers and previously selected unreachable receivers.
        /// </summary>
        public readonly IImmutableSet<UniqueAddress> ActiveReceivers;

        /// <summary>
        /// The unique address of the local node in the ring.
        /// </summary>
        public UniqueAddress SelfAddress { get { return Ring.SelfAddress; } }

        /// <summary>
        /// Initializes membership and reachability from the current cluster snapshot.
        /// </summary>
        /// <param name="nodes">The current cluster member identities.</param>
        /// <param name="unreachable">The members currently marked unreachable.</param>
        /// <returns>A state whose ring includes this node and the supplied membership snapshot.</returns>
        public ClusterHeartbeatSenderState Init(ImmutableHashSet<UniqueAddress> nodes, ImmutableHashSet<UniqueAddress> unreachable)
        {
            return Copy(ring: Ring.Copy(nodes: nodes.Add(SelfAddress), unreachable: unreachable));
        }

        /// <summary>
        /// Check to see if a node with the given address exists inside the heartbeat sender state.
        /// </summary>
        /// <param name="node">The node to check</param>
        /// <returns><c>true</c> if the heartbeat sender is already aware of this node. <c>false</c> otherwise.</returns>
        public bool Contains(UniqueAddress node)
        {
            return Ring.Nodes.Contains(node);
        }

        /// <summary>
        /// Adds a new <see cref="UniqueAddress"/> to the heartbeat sender's state.
        /// </summary>
        /// <param name="node">The node to add.</param>
        /// <returns>An updated copy of the state now including this member.</returns>
        public ClusterHeartbeatSenderState AddMember(UniqueAddress node)
        {
            return MembershipChange(Ring + node);
        }

        /// <summary>
        /// Removes a member from the ring and clears its failure-detector state.
        /// </summary>
        /// <param name="node">The member identity to remove.</param>
        /// <returns>The updated heartbeat state.</returns>
        public ClusterHeartbeatSenderState RemoveMember(UniqueAddress node)
        {
            var newState = MembershipChange(Ring - node);

            FailureDetector.Remove(node.Address);
            if (newState.OldReceiversNowUnreachable.Contains(node))
                return newState.Copy(oldReceiversNowUnreachable: newState.OldReceiversNowUnreachable.Remove(node));
            return newState;
        }

        /// <summary>
        /// Marks a member unreachable in the ring used to select heartbeat receivers.
        /// </summary>
        /// <param name="node">The member identity marked unreachable.</param>
        /// <returns>The updated heartbeat state.</returns>
        public ClusterHeartbeatSenderState UnreachableMember(UniqueAddress node)
        {
            return MembershipChange(Ring.Copy(unreachable: Ring.Unreachable.Add(node)));
        }

        /// <summary>
        /// Removes a member from the ring's unreachable set.
        /// </summary>
        /// <param name="node">The member identity marked reachable.</param>
        /// <returns>The updated heartbeat state.</returns>
        public ClusterHeartbeatSenderState ReachableMember(UniqueAddress node)
        {
            return MembershipChange(Ring.Copy(unreachable: Ring.Unreachable.Remove(node)));
        }

        private ClusterHeartbeatSenderState MembershipChange(HeartbeatNodeRing newRing)
        {
            var oldReceivers = Ring.MyReceivers.Value;
            var removedReceivers = oldReceivers.Except(newRing.MyReceivers.Value);
            var adjustedOldReceiversNowUnreachable = OldReceiversNowUnreachable;
            foreach (var r in removedReceivers)
            {
                if (FailureDetector.IsAvailable(r.Address))
                {
                    FailureDetector.Remove(r.Address);
                }
                else
                {
                    adjustedOldReceiversNowUnreachable = adjustedOldReceiversNowUnreachable.Add(r);
                }
            }
            return Copy(newRing, adjustedOldReceiversNowUnreachable);
        }

        /// <summary>
        /// Records an accepted heartbeat response and clears stale unreachable-receiver tracking when applicable.
        /// </summary>
        /// <param name="from">The unique address that sent the response.</param>
        /// <returns>The updated state, or this state when the sender is not an active receiver.</returns>
        public ClusterHeartbeatSenderState HeartbeatRsp(UniqueAddress from)
        {
            if (ActiveReceivers.Contains(from))
            {
                FailureDetector.Heartbeat(from.Address);
                if (OldReceiversNowUnreachable.Contains(from))
                {
                    //back from unreachable, ok to stop heartbeating to it
                    if (!Ring.MyReceivers.Value.Contains(from))
                    {
                        FailureDetector.Remove(from.Address);
                    }
                    return Copy(oldReceiversNowUnreachable: OldReceiversNowUnreachable.Remove(from));
                }
                return this;
            }
            return this;
        }

        /// <summary>
        /// Creates a copy with any supplied state components replaced.
        /// </summary>
        /// <param name="ring">The replacement ring, or <c>null</c> to retain the current ring.</param>
        /// <param name="oldReceiversNowUnreachable">The replacement set, or <c>null</c> to retain the current set.</param>
        /// <param name="failureDetector">The replacement registry, or <c>null</c> to retain the current registry.</param>
        /// <returns>A state containing the replacements and retained values.</returns>
        public ClusterHeartbeatSenderState Copy(HeartbeatNodeRing? ring = null, ImmutableHashSet<UniqueAddress> oldReceiversNowUnreachable = null, IFailureDetectorRegistry<Address> failureDetector = null)
        {
            return new ClusterHeartbeatSenderState(ring ?? Ring, oldReceiversNowUnreachable ?? OldReceiversNowUnreachable, failureDetector ?? FailureDetector);
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
    internal struct HeartbeatNodeRing
    {
        private readonly bool _useAllAsReceivers;
        private Option<IImmutableSet<UniqueAddress>> _myReceivers;

        /// <summary>
        /// Creates a ring that selects heartbeat receivers from the supplied cluster membership.
        /// </summary>
        /// <param name="selfAddress">The local node's unique address.</param>
        /// <param name="nodes">The cluster member identities included in the ring; this set must contain <paramref name="selfAddress"/>.</param>
        /// <param name="unreachable">The members currently marked unreachable.</param>
        /// <param name="monitoredByNumberOfNodes">The target number of reachable peers to select for monitoring.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="nodes"/> doesn't contain the specified <paramref name="selfAddress"/>.
        /// </exception>
        public HeartbeatNodeRing(
            UniqueAddress selfAddress,
            ImmutableHashSet<UniqueAddress> nodes,
            ImmutableHashSet<UniqueAddress> unreachable,
            int monitoredByNumberOfNodes)
        {
            SelfAddress = selfAddress;
            Nodes = nodes;
            NodeRing = nodes.ToImmutableSortedSet(RingComparer.Instance);
            Unreachable = unreachable;
            MonitoredByNumberOfNodes = monitoredByNumberOfNodes;

            if (!nodes.Contains(selfAddress))
                throw new ArgumentException($"Nodes [{string.Join(", ", nodes)}] must contain selfAddress [{selfAddress}]");

            _useAllAsReceivers = MonitoredByNumberOfNodes >= (NodeRing.Count - 1);
            _myReceivers = Option<IImmutableSet<UniqueAddress>>.None;
        }

        /// <summary>
        /// The local node's unique address.
        /// </summary>
        public UniqueAddress SelfAddress { get; }

        /// <summary>
        /// The members included in the ring.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Nodes { get; }

        /// <summary>
        /// The members currently marked unreachable.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Unreachable { get; }

        /// <summary>
        /// The target number of reachable peers each node monitors.
        /// </summary>
        public int MonitoredByNumberOfNodes { get; }

        public ImmutableSortedSet<UniqueAddress> NodeRing { get; }

        /// <summary>
        /// Receivers for <see cref="SelfAddress"/>. Cached for subsequent access.
        /// </summary>
        public Option<IImmutableSet<UniqueAddress>> MyReceivers
        {
            get
            {
                if (_myReceivers.IsEmpty)
                {
                    _myReceivers = Option<IImmutableSet<UniqueAddress>>.Create(Receivers(SelfAddress));
                }

                return _myReceivers;
            }
        }

        /// <summary>
        /// The set of Akka.Cluster nodes designated for receiving heartbeats from this node.
        /// </summary>
        /// <param name="sender">The node sending heartbeats.</param>
        /// <returns>An organized ring of unique nodes.</returns>
        public IImmutableSet<UniqueAddress> Receivers(UniqueAddress sender)
        {
            if (_useAllAsReceivers)
            {
                return NodeRing.Remove(sender);
            }
            else
            {
                // Pick nodes from the iterator until n nodes that are not unreachable have been selected.
                // Intermediate unreachable nodes up to `monitoredByNrOfMembers` are also included in the result.
                // The reason for not limiting it to strictly monitoredByNrOfMembers is that the leader must
                // be able to continue its duties (e.g. removal of downed nodes) when many nodes are shutdown
                // at the same time and nobody in the remaining cluster is monitoring some of the shutdown nodes.
                (int, ImmutableSortedSet<UniqueAddress>) Take(int n, IEnumerator<UniqueAddress> iter, ImmutableSortedSet<UniqueAddress> acc, ImmutableHashSet<UniqueAddress> unreachable, int monitoredByNumberOfNodes)
                {
                    while (true)
                    {
                        if (iter.MoveNext() == false || n == 0)
                        {
                            iter.Dispose(); // dispose enumerator
                            return (n, acc);
                        }
                        else
                        {
                            var next = iter.Current;
                            var isUnreachable = unreachable.Contains(next);
                            if (isUnreachable && acc.Count >= monitoredByNumberOfNodes)
                            {
                            }
                            else if (isUnreachable)
                            {
                                acc = acc.Add(next);
                            }
                            else
                            {
                                n = n - 1;
                                acc = acc.Add(next);
                            }
                        }
                    }
                }

                var (remaining, slice1) = Take(MonitoredByNumberOfNodes, NodeRing.From(sender).Skip(1).GetEnumerator(), ImmutableSortedSet<UniqueAddress>.Empty, Unreachable, MonitoredByNumberOfNodes);

                IImmutableSet<UniqueAddress> slice = remaining == 0 
                    ? slice1 // or, wrap-around
                    : Take(remaining, NodeRing.TakeWhile(x => x != sender).GetEnumerator(), slice1, Unreachable, MonitoredByNumberOfNodes).Item2;

                return slice;
            }
        }

        /// <summary>
        /// Creates a copy of the ring, replacing supplied components and retaining the rest.
        /// </summary>
        /// <param name="selfAddress">The replacement local address, or <c>null</c> to retain the current address.</param>
        /// <param name="nodes">The replacement membership set, or <c>null</c> to retain the current set.</param>
        /// <param name="unreachable">The replacement unreachable set, or <c>null</c> to retain the current set.</param>
        /// <param name="monitoredByNumberOfNodes">The replacement receiver target, or <c>null</c> to retain the current value.</param>
        /// <returns>A ring using the supplied replacements and retained values.</returns>
        public HeartbeatNodeRing Copy(UniqueAddress selfAddress = null, ImmutableHashSet<UniqueAddress> nodes = null, ImmutableHashSet<UniqueAddress> unreachable = null, int? monitoredByNumberOfNodes = null)
        {
            return new HeartbeatNodeRing(
                selfAddress ?? SelfAddress,
                nodes ?? Nodes,
                unreachable ?? Unreachable,
                monitoredByNumberOfNodes ?? MonitoredByNumberOfNodes);
        }

        #region Operators

        /// <summary>
        /// Adds a member to the ring if it is not already present.
        /// </summary>
        /// <param name="ring">The ring to update.</param>
        /// <param name="node">The member identity to add.</param>
        /// <returns>The ring containing <paramref name="node"/>, or the original ring if it was already present.</returns>
        public static HeartbeatNodeRing operator +(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.Nodes.Contains(node) ? ring : ring.Copy(nodes: ring.Nodes.Add(node));
        }

        /// <summary>
        /// Removes a member from the ring and its unreachable set.
        /// </summary>
        /// <param name="ring">The ring to update.</param>
        /// <param name="node">The member identity to remove.</param>
        /// <returns>The ring without <paramref name="node"/> in membership or unreachable state, or the original ring if it was absent from both.</returns>
        public static HeartbeatNodeRing operator -(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.Nodes.Contains(node) || ring.Unreachable.Contains(node)
                ? ring.Copy(nodes: ring.Nodes.Remove(node), unreachable: ring.Unreachable.Remove(node)) 
                : ring;
        }

        #endregion

        #region Comparer
        /// <summary>
        /// Data structure for picking heartbeat receivers. The node ring is
        /// shuffled by deterministic hashing to avoid picking physically co-located
        /// neighbors.
        /// </summary>
        internal class RingComparer : IComparer<UniqueAddress>
        {
            /// <summary>
            /// The singleton instance of this comparer
            /// </summary>
            public static readonly RingComparer Instance = new();
            private RingComparer() { }

            /// <inheritdoc/>
            public int Compare(UniqueAddress x, UniqueAddress y)
            {
                var ha = x.Uid;
                var hb = y.Uid;
                var c = ha.CompareTo(hb);
                return c == 0 ? Member.AddressOrdering.Compare(x.Address, y.Address) : c;
            }
        }
        #endregion
    }
}
