//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        private readonly Lazy<ClusterHeartbeatSender.HeartbeatRsp> _selfHeartbeatRsp;

        /// <summary>
        /// TBD
        /// </summary>
        public ClusterHeartbeatReceiver()
        {
            // Important - don't use Cluster.Get(Context.System) in constructor because that would
            // cause deadlock. See startup sequence in ClusterDaemon.
            _selfHeartbeatRsp = new Lazy<ClusterHeartbeatSender.HeartbeatRsp>(() =>
                new ClusterHeartbeatSender.HeartbeatRsp(Cluster.Get(Context.System).SelfUniqueAddress));

            Receive<ClusterHeartbeatSender.Heartbeat>(heartbeat => Sender.Tell(_selfHeartbeatRsp.Value));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ClusterHeartbeatSender : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Cluster _cluster;
        private readonly IFailureDetectorRegistry<Address> _failureDetector;
        private readonly Heartbeat _selfHeartbeat;
        private ClusterHeartbeatSenderState _state;
        private readonly ICancelable _heartbeatTask;

        // used for logging warning if actual tick interval is unexpected (e.g. due to starvation)
        private DateTime _tickTimestamp;

        /// <summary>
        /// TBD
        /// </summary>
        public ClusterHeartbeatSender()
        {
            _cluster = Cluster.Get(Context.System);
            var tickInitialDelay = _cluster.Settings.PeriodicTasksInitialDelay.Max(_cluster.Settings.HeartbeatInterval);
            _tickTimestamp = DateTime.UtcNow + tickInitialDelay;

            // the failureDetector is only updated by this actor, but read from other places
            _failureDetector = _cluster.FailureDetector;

            _selfHeartbeat = new Heartbeat(_cluster.SelfAddress);

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

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent) });
        }

        /// <summary>
        /// TBD
        /// </summary>
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
        private ActorSelection HeartbeatReceiver(Address address)
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
            Receive<HeartbeatTick>(tick =>
            {
                _tickTimestamp = DateTime.UtcNow; // start checks when active
            }); //do nothing
        }

        private void Active()
        {
            Receive<HeartbeatTick>(tick => DoHeartbeat());
            Receive<HeartbeatRsp>(rsp => DoHeartbeatRsp(rsp.From));
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
                    Context.System.Scheduler.ScheduleTellOnce(
                        _cluster.Settings.HeartbeatExpectedResponseAfter,
                        Self,
                        new ExpectedFirstHeartbeat(to),
                        Self);
                }
                HeartbeatReceiver(to.Address).Tell(_selfHeartbeat);
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
                    _cluster.SelfAddress, (now - _tickTimestamp).TotalMilliseconds, _cluster.Settings.HeartbeatInterval.TotalMilliseconds);
            }
            
            _tickTimestamp = DateTime.UtcNow;
        }

        private void DoHeartbeatRsp(UniqueAddress from)
        {
            if (_cluster.Settings.VerboseHeartbeatLogging)
            {
                _log.Debug("Cluster Node [{0}] - Heartbeat response from [{1}]", _cluster.SelfAddress, from.Address);
            }
            _state = _state.HeartbeatRsp(from);
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

        /// <summary>
        /// Sent at regular intervals for failure detection
        /// </summary>
        internal sealed class Heartbeat : IClusterMessage, IPriorityMessage, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="from">TBD</param>
            public Heartbeat(Address from)
            {
                From = from;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address From { get; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            /// <inheritdoc/>
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
        internal sealed class HeartbeatRsp : IClusterMessage, IPriorityMessage, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="from">TBD</param>
            public HeartbeatRsp(UniqueAddress from)
            {
                From = from;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress From { get; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            /// <inheritdoc/>
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

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class ExpectedFirstHeartbeat
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="from">TBD</param>
            public ExpectedFirstHeartbeat(UniqueAddress from)
            {
                From = from;
            }

            /// <summary>
            /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="ring">TBD</param>
        /// <param name="oldReceiversNowUnreachable">TBD</param>
        /// <param name="failureDetector">TBD</param>
        public ClusterHeartbeatSenderState(HeartbeatNodeRing ring, ImmutableHashSet<UniqueAddress> oldReceiversNowUnreachable, IFailureDetectorRegistry<Address> failureDetector)
        {
            Ring = ring;
            OldReceiversNowUnreachable = oldReceiversNowUnreachable;
            FailureDetector = failureDetector;
            ActiveReceivers = Ring.MyReceivers.Value.Union(OldReceiversNowUnreachable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public HeartbeatNodeRing Ring { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> OldReceiversNowUnreachable { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IFailureDetectorRegistry<Address> FailureDetector { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly ImmutableHashSet<UniqueAddress> ActiveReceivers;

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress SelfAddress { get { return Ring.SelfAddress; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nodes">TBD</param>
        /// <param name="unreachable">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public ClusterHeartbeatSenderState RemoveMember(UniqueAddress node)
        {
            var newState = MembershipChange(Ring - node);

            FailureDetector.Remove(node.Address);
            if (newState.OldReceiversNowUnreachable.Contains(node))
                return newState.Copy(oldReceiversNowUnreachable: newState.OldReceiversNowUnreachable.Remove(node));
            return newState;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public ClusterHeartbeatSenderState UnreachableMember(UniqueAddress node)
        {
            return MembershipChange(Ring.Copy(unreachable: Ring.Unreachable.Add(node)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
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
                    FailureDetector.Remove(r.Address);
                else
                {
                    adjustedOldReceiversNowUnreachable = adjustedOldReceiversNowUnreachable.Add(r);
                }
            }
            return Copy(newRing, adjustedOldReceiversNowUnreachable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="ring">TBD</param>
        /// <param name="oldReceiversNowUnreachable">TBD</param>
        /// <param name="failureDetector">TBD</param>
        /// <returns>TBD</returns>
        public ClusterHeartbeatSenderState Copy(HeartbeatNodeRing ring = null, ImmutableHashSet<UniqueAddress> oldReceiversNowUnreachable = null, IFailureDetectorRegistry<Address> failureDetector = null)
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
    internal sealed class HeartbeatNodeRing
    {
        private readonly bool _useAllAsReceivers;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="selfAddress">TBD</param>
        /// <param name="nodes">TBD</param>
        /// <param name="unreachable">TBD</param>
        /// <param name="monitoredByNumberOfNodes">TBD</param>
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
            Unreachable = unreachable;
            MonitoredByNumberOfNodes = monitoredByNumberOfNodes;

            if (!nodes.Contains(selfAddress))
                throw new ArgumentException($"Nodes [${string.Join(", ", nodes)}] must contain selfAddress [{selfAddress}]");

            _useAllAsReceivers = MonitoredByNumberOfNodes >= (NodeRing().Count - 1);
            MyReceivers = new Lazy<ImmutableHashSet<UniqueAddress>>(() => Receivers(SelfAddress));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress SelfAddress { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Nodes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Unreachable { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MonitoredByNumberOfNodes { get; }

        private ImmutableSortedSet<UniqueAddress> NodeRing()
        {
            return Nodes.ToImmutableSortedSet(RingComparer.Instance);
        }

        /// <summary>
        /// Receivers for <see cref="SelfAddress"/>. Cached for subsequent access.
        /// </summary>
        public readonly Lazy<ImmutableHashSet<UniqueAddress>> MyReceivers;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sender">TBD</param>
        /// <returns>TBD</returns>
        public ImmutableHashSet<UniqueAddress> Receivers(UniqueAddress sender)
        {
            if (_useAllAsReceivers)
            {
                return NodeRing().Remove(sender).ToImmutableHashSet();
            }
            else
            {
                // Pick nodes from the iterator until n nodes that are not unreachable have been selected.
                // Intermediate unreachable nodes up to `monitoredByNrOfMembers` are also included in the result.
                // The reason for not limiting it to strictly monitoredByNrOfMembers is that the leader must
                // be able to continue its duties (e.g. removal of downed nodes) when many nodes are shutdown
                // at the same time and nobody in the remaining cluster is monitoring some of the shutdown nodes.
                Func<int, IEnumerator<UniqueAddress>, ImmutableSortedSet<UniqueAddress>, (int, ImmutableSortedSet<UniqueAddress>)> take = null;
                take = (n, iter, acc) =>
                {
                    if (iter.MoveNext() == false || n == 0)
                    {
                        return (n, acc);
                    }
                    else
                    {
                        UniqueAddress next = iter.Current;
                        var isUnreachable = Unreachable.Contains(next);
                        if (isUnreachable && acc.Count >= MonitoredByNumberOfNodes)
                        {
                            return take(n, iter, acc); // skip the unreachable, since we have already picked `MonitoredByNumberOfNodes`
                        }
                        else if (isUnreachable)
                        {
                            return take(n, iter, acc.Add(next)); // include the unreachable, but don't count it
                        }
                        else
                        {
                            return take(n - 1, iter, acc.Add(next)); // include the reachable
                        }
                    }
                };

                var tuple = take(MonitoredByNumberOfNodes, NodeRing().From(sender).Skip(1).GetEnumerator(), ImmutableSortedSet<UniqueAddress>.Empty);
                var remaining = tuple.Item1;
                var slice1 = tuple.Item2;

                IImmutableSet<UniqueAddress> slice = remaining == 0 
                    ? slice1 
                    : take(remaining, NodeRing().Until(sender).Where(c => !c.Equals(sender)).GetEnumerator(), slice1).Item2;

                return slice.ToImmutableHashSet();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="selfAddress">TBD</param>
        /// <param name="nodes">TBD</param>
        /// <param name="unreachable">TBD</param>
        /// <param name="monitoredByNumberOfNodes">TBD</param>
        /// <returns>TBD</returns>
        public HeartbeatNodeRing Copy(UniqueAddress selfAddress = null, ImmutableHashSet<UniqueAddress> nodes = null, ImmutableHashSet<UniqueAddress> unreachable = null, int? monitoredByNumberOfNodes = null)
        {
            return new HeartbeatNodeRing(
                selfAddress ?? SelfAddress,
                nodes ?? Nodes,
                unreachable ?? Unreachable,
                monitoredByNumberOfNodes.HasValue ? monitoredByNumberOfNodes.Value : MonitoredByNumberOfNodes);
        }

        #region Operators

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ring">TBD</param>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public static HeartbeatNodeRing operator +(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.Nodes.Contains(node) ? ring : ring.Copy(nodes: ring.Nodes.Add(node));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ring">TBD</param>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public static HeartbeatNodeRing operator -(HeartbeatNodeRing ring, UniqueAddress node)
        {
            return ring.Nodes.Contains(node) || ring.Unreachable.Contains(node)
                ? ring.Copy(nodes: ring.Nodes.Remove(node), unreachable: ring.Unreachable.Remove(node)) 
                : ring;
        }

        #endregion

        #region Comparer
        /// <summary>
        /// TBD
        /// </summary>
        internal class RingComparer : IComparer<UniqueAddress>
        {
            /// <summary>
            /// The singleton instance of this comparer
            /// </summary>
            public static readonly RingComparer Instance = new RingComparer();
            private RingComparer() { }

            /// <inheritdoc/>
            public int Compare(UniqueAddress x, UniqueAddress y)
            {
                var result = Member.AddressOrdering.Compare(x.Address, y.Address);
                if (result == 0)
                    if (x.Uid < y.Uid) return -1;
                    else if (x.Uid == y.Uid) return 0;
                    else return 1;
                return result;
            }
        }
        #endregion
    }
}

