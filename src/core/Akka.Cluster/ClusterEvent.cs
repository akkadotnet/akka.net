//-----------------------------------------------------------------------
// <copyright file="ClusterEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// Domain events published to the event bus.
    /// Subscribe with:
    /// {{{
    /// Cluster(system).subscribe(actorRef, classOf[ClusterDomainEvent])
    /// }}}
    /// </summary>
    public class ClusterEvent
    {
        public enum SubscriptionInitialStateMode
        {
            //TODO: Sort out xml doc references
            /// <summary>
            /// When using this subscription mode a snapshot of
            /// <see cref="CurrentClusterState"/> will be sent to the
            /// subscriber as the first message.
            /// </summary>
            InitialStateAsSnapshot,
            /// <summary>
            /// When using this subscription mode the events corresponding
            /// to the current state will be sent to the subscriber to mimic what you would
            /// have seen if you were listening to the events when they occurred in the past.
            /// </summary>
            InitialStateAsEvents
        }

        public static readonly SubscriptionInitialStateMode InitialStateAsSnapshot =
            SubscriptionInitialStateMode.InitialStateAsSnapshot;

        public static readonly SubscriptionInitialStateMode InitialStateAsEvents =
            SubscriptionInitialStateMode.InitialStateAsEvents;

        /// <summary>
        /// Marker interface for cluster domain events
        /// </summary>
        public interface IClusterDomainEvent { }

        public sealed class CurrentClusterState
        {
            private readonly ImmutableSortedSet<Member> _members;
            private readonly ImmutableHashSet<Member> _unreachable;
            private readonly ImmutableHashSet<Address> _seenBy;
            private readonly Address _leader;
            private readonly ImmutableDictionary<string, Address> _roleLeaderMap;

            public CurrentClusterState() : this(
                ImmutableSortedSet.Create<Member>(),
                ImmutableHashSet.Create<Member>(),
                ImmutableHashSet.Create<Address>(),
                null,
                ImmutableDictionary.Create<string, Address>()
                )
            {}

            public CurrentClusterState(
                ImmutableSortedSet<Member> members,
                ImmutableHashSet<Member> unreachable,
                ImmutableHashSet<Address> seenBy,
                Address leader,
                ImmutableDictionary<string, Address> roleLeaderMap)
            {
                _members = members;
                _unreachable = unreachable;
                _seenBy = seenBy;
                _leader = leader;
                _roleLeaderMap = roleLeaderMap;
            }

            /// <summary>
            /// Get current member list
            /// </summary>
            public ImmutableSortedSet<Member> Members
            {
                get { return _members; }
            }

            /// <summary>
            /// Get current unreachable set
            /// </summary>
            public ImmutableHashSet<Member> Unreachable
            {
                get { return _unreachable; }
            }

            /// <summary>
            /// Get current "seen-by" set
            /// </summary>
            public ImmutableHashSet<Address> SeenBy
            {
                get { return _seenBy; }
            }

            /// <summary>
            /// Get address of current leader, or null if noe
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            //TODO: Cache ToImmutableHashSet?
            /// <summary>
            /// All node roles in the cluster
            /// </summary>
            public ImmutableHashSet<string> AllRoles
            {
                get { return _roleLeaderMap.Keys.ToImmutableHashSet(); }
            }

            /// <summary>
            /// Needed internally inside the <see cref="ClusterReadView"/>
            /// </summary>
            internal ImmutableDictionary<string, Address> RoleLeaderMap
            {
                get { return _roleLeaderMap; }
            }

            /// <summary>
            /// Get address of current leader, if any, within the role set
            /// </summary>
            public Address RoleLeader(string role)
            {
                return _roleLeaderMap.GetOrElse(role, null);
            }

            /// <summary>
            /// Creates a deep copy of the <see cref="CurrentClusterState"/> and optionally allows you
            /// to specify different values for the outgoing objects
            /// </summary>
            public CurrentClusterState Copy(ImmutableSortedSet<Member> members = null,
                ImmutableHashSet<Member> unreachable = null,
                ImmutableHashSet<Address> seenBy = null,
                Address leader = null,
                ImmutableDictionary<string, Address> roleLeaderMap = null)
            {
                return new CurrentClusterState(members ?? _members,
                    unreachable ?? _unreachable,
                    seenBy ?? _seenBy,
                    leader ?? (_leader != null ? (Address)_leader.Clone() : null),
                    roleLeaderMap ?? _roleLeaderMap);
            }
        }

        /// <summary>
        /// Marker interface for membership events.
        /// Published when the state change is first seen on a node.
        /// The state change was performed by the leader when there was
        /// convergence on the leader node, i.e. all members had seen previous
        /// state.
        /// </summary>
        public interface IMemberEvent : IClusterDomainEvent
        {
            Member Member { get; }
        }

        public abstract class MemberStatusChange : IMemberEvent
        {
            protected readonly Member _member;

            protected MemberStatusChange(Member member, MemberStatus validStatus)
            {
                if (member.Status != validStatus)
                    throw new ArgumentException(String.Format("Expected {0} state, got: {1}", validStatus, member));
                _member = member;
            }

            public Member Member
            {
                get { return _member; }
            }

            public override bool Equals(object obj)
            {
                var other = obj as MemberStatusChange;
                if (other == null) return false;
                return _member.Equals(other._member);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash*23 + _member.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Member status changed to Joining.
        /// </summary>
        public sealed class MemberJoined : MemberStatusChange
        {
            public MemberJoined(Member member)
                : base(member, MemberStatus.Joining) { }
        }

        /// <summary>
        /// Member status changed to Up.
        /// </summary>
        public sealed class MemberUp : MemberStatusChange
        {
            public MemberUp(Member member)
                : base(member, MemberStatus.Up) { }
        }

        /// <summary>
        ///  Member status changed to Leaving.
        /// </summary>
        public sealed class MemberLeft : MemberStatusChange
        {
            public MemberLeft(Member member)
                : base(member, MemberStatus.Leaving) { }
        }

        //TODO: Sort out xml doc references
        /// <summary>
        /// Member status changed to <see cref="Akka.Cluster.MemberStatus.Exiting"/> and will be removed
        /// when all members have seen the `Exiting` status.
        /// </summary>
        public sealed class MemberExited : MemberStatusChange
        {
            public MemberExited(Member member)
                : base(member, MemberStatus.Exiting)
            {
            }
        }

        /// <summary>
        /// Member completely removed from the cluster.
        /// When `previousStatus` is `MemberStatus.Down` the node was removed
        /// after being detected as unreachable and downed.
        /// When `previousStatus` is `MemberStatus.Exiting` the node was removed
        /// after graceful leaving and exiting.
        /// </summary>
        public sealed class MemberRemoved : MemberStatusChange
        {
            readonly MemberStatus _previousStatus;

            public MemberStatus PreviousStatus 
            { 
                get { return _previousStatus; }
            }

            public MemberRemoved(Member member, MemberStatus previousStatus)
                : base(member, MemberStatus.Removed)
            {
                if(member.Status != MemberStatus.Removed) throw new ArgumentException(String.Format("Expected Removed status, got {0}", member));
                _previousStatus = previousStatus;
            }

            public override bool Equals(object obj)
            {
                var other = obj as MemberRemoved;
                if (other == null) return false;
                return _member.Equals(other._member) && _previousStatus == other._previousStatus;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash*23 + base.GetHashCode();
                    hash = hash*23 + _previousStatus.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Leader of the cluster members changed. Published when the state change
        /// is first seen on a node.
        /// </summary>
        public sealed class LeaderChanged : IClusterDomainEvent
        {
            private readonly Address _leader;

            public LeaderChanged(Address leader)
            {
                _leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            public override bool Equals(object obj)
            {
                var other = obj as LeaderChanged;
                if (other == null) return false;
                return (_leader == null && other._leader == null) || (_leader != null && _leader.Equals(other._leader));
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + (_leader == null ? 0 : _leader.GetHashCode());
                    return hash;
                }
            }
        }

        /// <summary>
        /// First member (leader) of the members within a role set changed.
        /// Published when the state change is first seen on a node.
        /// </summary>
        public sealed class RoleLeaderChanged : IClusterDomainEvent
        {
            //TODO: While I'm here Address needs to be immutable
            private readonly Address _leader;
            private readonly string _role;

            public RoleLeaderChanged(string role, Address leader)
            {
                _role = role;
                _leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            public string Role
            {
                get { return _role; }
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _role.GetHashCode();
                    hash = hash * 23 + (_leader == null ? 0 : _leader.GetHashCode());
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                //TODO: Messy
                var other = obj as RoleLeaderChanged;
                if (other == null) return false;
                return _role.Equals(other._role) && 
                    ((_leader == null && other._leader == null) || (_leader != null && _leader.Equals(other._leader)));
            }
        }

        public sealed class ClusterShuttingDown : IClusterDomainEvent
        {
            public static readonly IClusterDomainEvent Instance = new ClusterShuttingDown();
        }

        /// <summary>
        /// A marker interface to facilitate the subscription of
        /// both <see cref="Akka.Cluster.ClusterEvent.UnreachableMember"/> and <see cref="Akka.Cluster.ClusterEvent.ReachableMember"/>.
        /// </summary>
        public interface IReachabilityEvent : IClusterDomainEvent
        {
        }

        public abstract class ReachabilityEvent : IReachabilityEvent
        {
            private readonly Member _member;

            protected ReachabilityEvent(Member member)
            {
                _member = member;
            }

            public Member Member
            {
                get { return _member; }
            }

            public override bool Equals(object obj)
            {
                var other = obj as ReachabilityEvent;
                if (other == null) return false;
                return _member.Equals(other._member);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _member.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// A member is considered as unreachable by the failure detector.
        /// </summary>
        public sealed class UnreachableMember : ReachabilityEvent
        {
            public UnreachableMember(Member member)
                : base(member)
            {
            }
        }

        //TODO: xml doc
        /// <summary>
        /// A member is considered as reachable by the failure detector
        /// after having been unreachable.
        /// <see cref="Akka.Cluster.ClusterEvent.UnreachableMember"/>
        /// </summary>
        public sealed class ReachableMember : ReachabilityEvent
        {
            public ReachableMember(Member member)
                : base(member)
            {
            }
        }

        /// <summary>
        /// Current snapshot of cluster node metrics. Published to subscribers.
        /// </summary>
        public class ClusterMetricsChanged : IClusterDomainEvent
        {
            private readonly ImmutableHashSet<NodeMetrics> _nodeMetrics;

            public ClusterMetricsChanged(ImmutableHashSet<NodeMetrics> nodeMetrics)
            {
                _nodeMetrics = nodeMetrics;
            }

            public ImmutableHashSet<NodeMetrics> NodeMetrics
            {
                get { return _nodeMetrics; }
            }
        }

        /// <summary>
        /// The nodes that have seen current version of the Gossip.
        /// </summary>
        internal sealed class SeenChanged : IClusterDomainEvent
        {
            private readonly bool _convergence;
            private readonly ImmutableHashSet<Address> _seenBy;

            public SeenChanged(bool convergence, ImmutableHashSet<Address> seenBy)
            {
                _convergence = convergence;
                _seenBy = seenBy;
            }

            public bool Convergence
            {
                get { return _convergence; }
            }

            public ImmutableHashSet<Address> SeenBy
            {
                get { return _seenBy; }
            }

            public override bool Equals(object obj)
            {
                var other = obj as SeenChanged;
                if (other == null) return false;
                return _convergence.Equals(other._convergence) && _seenBy.SequenceEqual(other._seenBy);
            }

            //TODO: Override GetHashCode? What to do about collection?
        }

        internal sealed class ReachabilityChanged : IClusterDomainEvent
        {
            private readonly Reachability _reachability;

            public ReachabilityChanged(Reachability reachability)
            {
                _reachability = reachability;
            }

            public Reachability Reachability
            {
                get { return _reachability; }
            }

            public override bool Equals(object obj)
            {
                var other = obj as ReachabilityChanged;
                if (other == null) return false;
                return _reachability.Equals(other._reachability);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash*23 + _reachability.GetHashCode();
                    return hash;
                }
            }
        }

        internal sealed class CurrentInternalStats : IClusterDomainEvent
        {
            private readonly GossipStats _gossipStats;
            private readonly VectorClockStats _vclockStats;

            public CurrentInternalStats(GossipStats gossipStats, VectorClockStats vclockStats)
            {
                _gossipStats = gossipStats;
                _vclockStats = vclockStats;
            }

            public GossipStats GossipStats
            {
                get { return _gossipStats; }
            }

            public VectorClockStats SeenBy
            {
                get { return _vclockStats; }
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash*23 + _gossipStats.GetHashCode();
                    hash = hash*23 + _vclockStats.GetHashCode();
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                var other = obj as CurrentInternalStats;
                if (other == null) return false;
                return _gossipStats.Equals(other._gossipStats) && _vclockStats.Equals(other._vclockStats);
            }
        }

        internal static ImmutableList<UnreachableMember> DiffUnreachable(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList.Create<UnreachableMember>();
            }

            var oldUnreachableNodes = oldGossip.Overview.Reachability.AllUnreachableOrTerminated;
            return newGossip.Overview.Reachability.AllUnreachableOrTerminated
                    .Where(node => !oldUnreachableNodes.Contains(node) && node != selfUniqueAddress)
                    .Select(node => new UnreachableMember(newGossip.GetMember(node)))
                    .ToImmutableList();
        }

        internal static ImmutableList<ReachableMember> DiffReachable(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList.Create<ReachableMember>();
            }

            return oldGossip.Overview.Reachability.AllUnreachable
                    .Where(node => newGossip.HasMember(node) && newGossip.Overview.Reachability.IsReachable(node) && node != selfUniqueAddress)
                    .Select(a => new ReachableMember(newGossip.GetMember(a)))
                    .ToImmutableList();
        }

        internal static ImmutableList<IMemberEvent> DiffMemberEvents(Gossip oldGossip, Gossip newGossip)
        {
            if (newGossip.Equals(oldGossip)) return ImmutableList.Create<IMemberEvent>();
            var newMembers = newGossip.Members.Except(oldGossip.Members);

            var membersGroupedByAddress = newGossip.Members
                .Concat(oldGossip.Members)
                .GroupBy(m => m.UniqueAddress);
            var changedMembers = membersGroupedByAddress
                .Where(g => g.Count() == 2 && g.First().Status != g.Skip(1).First().Status)
                .Select(g => g.First());

            var memberEvents = CollectMemberEvents(newMembers.Union(changedMembers));

            var removedMembers = oldGossip.Members.Except(newGossip.Members);
            var removedEvents = removedMembers.Select(m => new MemberRemoved(m.Copy(status: MemberStatus.Removed), m.Status));

            return memberEvents.Concat(removedEvents).ToImmutableList();
        }

        private static IEnumerable<IMemberEvent> CollectMemberEvents(IEnumerable<Member> members)
        {
            foreach (var member in members)
            {
                if (member.Status == MemberStatus.Joining) yield return new MemberJoined(member);
                if (member.Status == MemberStatus.Up) yield return new MemberUp(member);
                if (member.Status == MemberStatus.Leaving) yield return new MemberLeft(member);
                if (member.Status == MemberStatus.Exiting) yield return new MemberExited(member);
            }
        }

        internal static ImmutableList<LeaderChanged> DiffLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            var newLeader = newGossip.Leader(selfUniqueAddress);
            if ((newLeader == null && oldGossip.Leader(selfUniqueAddress) == null) 
                || newLeader != null && newLeader.Equals(oldGossip.Leader(selfUniqueAddress))) 
                return ImmutableList.Create<LeaderChanged>();

            return ImmutableList.Create(newLeader == null 
                ? new LeaderChanged(null) 
                : new LeaderChanged(newLeader.Address));
        }

        internal static ImmutableHashSet<RoleLeaderChanged> DiffRolesLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            return InternalDiffRolesLeader(oldGossip, newGossip, selfUniqueAddress).ToImmutableHashSet();
        }

        private static IEnumerable<RoleLeaderChanged> InternalDiffRolesLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            foreach (var role in oldGossip.AllRoles.Union(newGossip.AllRoles))
            {
                var newLeader = newGossip.RoleLeader(role, selfUniqueAddress);
                if(newLeader == null && oldGossip.RoleLeader(role, selfUniqueAddress) != null)
                    yield return new RoleLeaderChanged(role, null);
                if(newLeader != null && !newLeader.Equals(oldGossip.RoleLeader(role, selfUniqueAddress))) 
                    yield return new RoleLeaderChanged(role, newLeader.Address);
            }
        }

        internal static ImmutableList<SeenChanged> DiffSeen(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList.Create<SeenChanged>();
            }

            var newConvergence = newGossip.Convergence(selfUniqueAddress);
            var newSeenBy = newGossip.SeenBy;
            if (newConvergence != oldGossip.Convergence(selfUniqueAddress) || newSeenBy != oldGossip.SeenBy)
            {
                return ImmutableList.Create(new SeenChanged(newConvergence, newSeenBy.Select(s => s.Address).ToImmutableHashSet()));
            }

            return ImmutableList.Create<SeenChanged>();
        }

        internal static ImmutableList<ReachabilityChanged> DiffReachability(Gossip oldGossip, Gossip newGossip)
        {
            if (newGossip.Overview.Reachability.Equals(oldGossip.Overview.Reachability))
                return ImmutableList.Create<ReachabilityChanged>();

            return ImmutableList.Create(new ReachabilityChanged(newGossip.Overview.Reachability));
        }
    }

    //TODO: IRequiresMessageQueue? 
    sealed class ClusterDomainEventPublisher : UntypedActor
    {
        Gossip _latestGossip;
        private readonly UniqueAddress _selfUniqueAddress = Cluster.Get(Context.System).SelfUniqueAddress;

        public ClusterDomainEventPublisher()
        {
            _latestGossip = Gossip.Empty;
            _eventStream = Context.System.EventStream;
        }

        protected override void PreRestart(Exception reason, object message)
        {
            // don't postStop when restarted, no children to stop
        }

        protected override void PostStop()
        {
            // publish the final removed state before shutting down
            Publish(ClusterEvent.ClusterShuttingDown.Instance);
            PublishChanges(Gossip.Empty);
        }

        protected override void OnReceive(object message)
        {
            if (message is InternalClusterAction.PublishChanges)
            {
                var p = message as InternalClusterAction.PublishChanges;
                PublishChanges(p.NewGossip);
            }
            else if (message is ClusterEvent.CurrentInternalStats)
            {
                var i = message as ClusterEvent.CurrentInternalStats;
                PublishInternalStats(i);
            }
            else if (message is InternalClusterAction.SendCurrentClusterState)
            {
                var sc = message as InternalClusterAction.SendCurrentClusterState;
                SendCurrentClusterState(sc.Receiver);
            }
            else if (message is InternalClusterAction.Subscribe)
            {
                var sub = message as InternalClusterAction.Subscribe;
                Subscribe(sub.Subscriber, sub.InitialStateMode, sub.To);
            }
            else if (message is InternalClusterAction.PublishEvent)
            {
                var pub = message as InternalClusterAction.PublishEvent;
                Publish(pub);
            }
            else if (message is InternalClusterAction.Unsubscribe)
            {
                var unsub = message as InternalClusterAction.Unsubscribe;
                Unsubscribe(unsub.Subscriber, unsub.To);
            }
            else
            {
                Unhandled(message);
            }
        }

        readonly EventStream _eventStream;

        /// <summary>
        /// The current snapshot state corresponding to latest gossip 
        /// to mimic what you would have seen if you were listening to the events.
        /// </summary>
        private void SendCurrentClusterState(IActorRef receiver)
        {
            var unreachable = _latestGossip.Overview.Reachability.AllUnreachableOrTerminated
                .Where(node => node != _selfUniqueAddress)
                .Select(_latestGossip.GetMember)
                .ToImmutableHashSet();

            var state = new ClusterEvent.CurrentClusterState(
                members: _latestGossip.Members,
                unreachable: unreachable,
                seenBy: _latestGossip.SeenBy.Select(s => s.Address).ToImmutableHashSet(),
                leader: _latestGossip.Leader(_selfUniqueAddress) == null ? null : _latestGossip.Leader(_selfUniqueAddress).Address,
                roleLeaderMap: _latestGossip.AllRoles.ToImmutableDictionary(r => r, r =>
                {
                    var leader = _latestGossip.RoleLeader(r, _selfUniqueAddress);
                    return leader == null ? null : leader.Address;
                }));
            receiver.Tell(state);
        }

        private void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initMode,
            IEnumerable<Type> to)
        {
            if (initMode == ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents)
            {
                Action<object> pub = @event =>
                {
                    var eventType = @event.GetType();
                    //TODO: IsAssignableFrom same as in scala?
                    if (to.Any(o => o.IsAssignableFrom(eventType)))
                        subscriber.Tell(@event);
                };
                PublishDiff(Gossip.Empty, _latestGossip, pub);
            }
            else if(initMode == ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot)
            {
                SendCurrentClusterState(subscriber);
            }

            foreach (var t in to) _eventStream.Subscribe(subscriber, t);
        }

        private void Unsubscribe(IActorRef subscriber, Type to)
        {
            if (to == null) _eventStream.Unsubscribe(subscriber);
            else _eventStream.Unsubscribe(subscriber, to);
        }

        private void PublishChanges(Gossip newGossip)
        {
            var oldGossip = _latestGossip;
            _latestGossip = newGossip;
            PublishDiff(oldGossip, newGossip, Publish);
        }

        private void PublishDiff(Gossip oldGossip, Gossip newGossip, Action<object> pub)
        {
            foreach (var @event in ClusterEvent.DiffMemberEvents(oldGossip, newGossip)) pub(@event);
            foreach (var @event in ClusterEvent.DiffUnreachable(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffReachable(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffLeader(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffRolesLeader(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            // publish internal SeenState for testing purposes
            foreach (var @event in ClusterEvent.DiffSeen(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffReachability(oldGossip, newGossip)) pub(@event);
        }

        private void PublishInternalStats(ClusterEvent.CurrentInternalStats currentStats)
        {
            Publish(currentStats);
        }

        private void Publish(object @event)
        {
            _eventStream.Publish(@event);
        }

        private void ClearState()
        {
            _latestGossip = Gossip.Empty;
        }
    }
}


