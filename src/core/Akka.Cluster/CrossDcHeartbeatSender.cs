#region copyright
//-----------------------------------------------------------------------
// <copyright file="CrossDcHeartbeatSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// This actor is will be started on all nodes participating in a cluster,
    /// however unlike the within-dc heartbeat sender (<see cref="ClusterHeartbeatSender"/>),
    /// it will only actively work on `n` "oldest" nodes of a given data center.
    /// 
    /// It will monitor it's oldest counterparts in other data centers.
    /// For example, a DC configured to have (up to) 4 monitoring actors,
    /// will have 4 such active at any point in time, and those will monitor
    /// the (at most) 4 oldest nodes of each data center.
    /// 
    /// This monitoring mode is both simple and predictable, and also uses the assumption that
    /// "nodes which stay around for a long time, become old", and those rarely change. In a way,
    /// they are the "core" of a cluster, while other nodes may be very dynamically changing worked
    /// nodes which aggresively come and go as the traffic in the service changes.
    /// </summary>
    internal sealed class CrossDcHeartbeatSender : ActorBase
    {
        #region messages intended only for local messaging during testing

        internal interface IInspectionCommand : INoSerializationVerificationNeeded { }
        internal sealed class ReportStatus { }

        internal interface IStatusReport : INoSerializationVerificationNeeded { }
        internal interface IMonitoringStateReport : IStatusReport { }
        internal sealed class MonitoringActive : IMonitoringStateReport
        {
            public MonitoringActive(CrossDcHeartbeatingState state)
            {
                State = state;
            }

            public CrossDcHeartbeatingState State { get; }
        }

        internal sealed class MonitoringDormant : IMonitoringStateReport { }

        #endregion

        private readonly Func<Member, bool> IsExternalClusterMember;
        private readonly Receive _activeOrIntrospecting;

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly bool _isVerboseHeartbeat;

        // For inspecting if in active state; allows avoiding "becoming active" when already active
        private bool _activelyMonitoring;

        private readonly CrossDcFailureDetectorSettings _crossDcSettings;
        private readonly object _selfHeartbeat;
        private readonly IFailureDetectorRegistry<Address> _crossDcFailureDetector;

        private CrossDcHeartbeatingState _dataCentersState;
        private readonly ICancelable _heartbeatTask;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public CrossDcHeartbeatSender()
        {
            _activeOrIntrospecting = message => Active(message) || Introspecting(message);
            _isVerboseHeartbeat = _cluster.Settings.VerboseHeartbeatLogging;
            IsExternalClusterMember = member => member.DataCenter != SelfDataCenter;
            _crossDcFailureDetector = _cluster.FailureDetector;
            _selfHeartbeat = new ClusterHeartbeatSender.Heartbeat(_cluster.SelfAddress);
            _dataCentersState = CrossDcHeartbeatingState.Init(
                SelfDataCenter,
                _crossDcFailureDetector,
                _crossDcSettings.NrOfMonitoringActors,
                ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering));

            // start periodic heartbeat to other nodes in cluster
            var periodicTasksInitialDelay = _cluster.Settings.PeriodicTasksInitialDelay;
            var heartbeatInterval = _cluster.Settings.HeartbeatInterval;
            var interval = new TimeSpan(Math.Max(periodicTasksInitialDelay.Ticks, heartbeatInterval.Ticks));
            _heartbeatTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, heartbeatInterval, Self, new ClusterHeartbeatSender.HeartbeatTick(), ActorRefs.NoSender);
        }

        public string SelfDataCenter => _cluster.SelfDataCenter;

        public bool SelfIsResponsibleForCrossDcHeartbeat
        {
            get
            {
                var activeDCs = _dataCentersState.DataCenters.Count;
                return activeDCs > 1 && _dataCentersState.ShouldActivelyMonitorNodes(SelfDataCenter, _cluster.SelfUniqueAddress);
            }
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            if (_isVerboseHeartbeat)
                _log.Debug("Initialized cross-dc heartbeat sender as DORMANT in DC: [{0}]", SelfDataCenter);
        }

        protected override void PostStop()
        {
            base.PostStop();
            foreach (var a in _dataCentersState.ActiveReceivers)
                _crossDcFailureDetector.Remove(a.Address);

            _heartbeatTask.Cancel();
            _cluster.Unsubscribe(Self);
        }

        protected override bool Receive(object message) => Dormant(message) || Introspecting(message);

        private bool Dormant(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state: Init(state); return true;
                case ClusterEvent.MemberRemoved removed: RemoveMember(removed.Member); return true;
                case ClusterHeartbeatSender.HeartbeatTick _: return true; // ignore
                default: return false;
            }
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case ClusterHeartbeatSender.HeartbeatTick _: Heartbeat(); return true;
                case ClusterHeartbeatSender.HeartbeatRsp rsp: HeartbeatRsp(rsp.From); return true;
                case ClusterEvent.MemberRemoved removed: RemoveMember(removed.Member); return true;
                case ClusterEvent.IMemberEvent e: AddMember(e.Member); return true;
                case ClusterHeartbeatSender.ExpectedFirstHeartbeat f: TriggerFirstHeartbeat(f.From); return true;
                default: return false;
            }
        }

        private bool Introspecting(object message)
        {
            switch (message)
            {
                case ReportStatus _:
                    var msg = _activelyMonitoring ? (object)new MonitoringActive(_dataCentersState) : new MonitoringDormant(); 
                    Sender.Tell(msg);
                    return true;
                default: return false;
            }
        }
        private void Init(ClusterEvent.CurrentClusterState snapshot)
        {
            // val unreachable = snapshot.unreachable.collect({ case m if isExternalClusterMember(m) => m.uniqueAddress })
            // nr of monitored nodes is the same as the number of monitoring nodes (`n` oldest in one DC watch `n` oldest in other)
            var nodes = snapshot.Members;
            var nrOfMonitoredNodes = _crossDcSettings.NrOfMonitoringActors;
            _dataCentersState = CrossDcHeartbeatingState.Init(SelfDataCenter, _crossDcFailureDetector, nrOfMonitoredNodes, nodes);
            BecomeActiveIfResponsibleForHeartbeat();
        }

        private void AddMember(Member member)
        {
            if (CrossDcHeartbeatingState.AtLeastInUpState(member))
            {
                // since we only monitor nodes in Up or later states, due to the n-th oldest requirement
                _dataCentersState = _dataCentersState.AddMember(member);
                if (_isVerboseHeartbeat && member.DataCenter != SelfDataCenter)
                    _log.Debug("Register member {0} for cross DC heartbeat (will only heartbeat if oldest)", member);

                BecomeActiveIfResponsibleForHeartbeat();
            }
        }

        private void RemoveMember(Member member)
        {
            if (member.UniqueAddress == _cluster.SelfUniqueAddress)
            {
                // This cluster node will be shutdown, but stop this actor immediately to avoid further updates
                Context.Stop(Self);
            }
            else
            {
                _dataCentersState = _dataCentersState.RemoveMember(member);
                BecomeActiveIfResponsibleForHeartbeat();
            }
        }

        private void Heartbeat()
        {
            foreach (var to in _dataCentersState.ActiveReceivers)
            {
                if (_crossDcFailureDetector.IsMonitoring(to.Address))
                {
                    if (_isVerboseHeartbeat) _log.Debug("Cluster Node [{0}][{1}] - (Cross) Heartbeat to [{2}]", SelfDataCenter, _cluster.SelfAddress, to.Address);
                }
                else
                {
                    if (_isVerboseHeartbeat) _log.Debug("Cluster Node [{0}][{1}] - First (Cross) Heartbeat to [{2}]", SelfDataCenter, _cluster.SelfAddress, to.Address);
                    // schedule the expected first heartbeat for later, which will give the
                    // other side a chance to reply, and also trigger some resends if needed
                    Context.System.Scheduler.ScheduleTellOnce(_crossDcSettings.HeartbeatExpectedResponseAfter, Self, new ClusterHeartbeatSender.ExpectedFirstHeartbeat(to), ActorRefs.NoSender);
                }

                HeartbeatReceiver(to.Address).Tell(_selfHeartbeat);
            }
        }

        private void HeartbeatRsp(UniqueAddress from)
        {
            if (_isVerboseHeartbeat) _log.Debug("Cluster Node [{0}][{1}] - (Cross) Heartbeat response from [{2}]", SelfDataCenter, _cluster.SelfAddress, from.Address);
            _dataCentersState = _dataCentersState.HeartbeatRsp(from);
        }

        private void TriggerFirstHeartbeat(UniqueAddress from)
        {
            if (_dataCentersState.ActiveReceivers.Contains(from) && !_crossDcFailureDetector.IsMonitoring(from.Address))
            {
                if (_isVerboseHeartbeat) _log.Debug("Cluster Node [{0}][{1}] - Trigger extra expected (cross) heartbeat from [{2}]", SelfDataCenter, _cluster.SelfAddress, from.Address);
                _crossDcFailureDetector.Heartbeat(from.Address);
            }

        }

        private void BecomeActiveIfResponsibleForHeartbeat()
        {
            if (!_activelyMonitoring && SelfIsResponsibleForCrossDcHeartbeat)
            {
                _log.Info("Cross DC heartbeat becoming ACTIVE on this node (for DC: {0}), monitoring other DCs oldest nodes", SelfDataCenter);
                _activelyMonitoring = true;

                Context.Become(_activeOrIntrospecting);
            }
            else if (!_activelyMonitoring)
            {
                if (_isVerboseHeartbeat) _log.Info("Remaining DORMANT; others in {0} handle heartbeating other DCs", SelfDataCenter);
            }
        }

        /// <summary>
        /// Looks up and returns the remote cluster heartbeat connection for the specific address.
        /// </summary>
        private ActorSelection HeartbeatReceiver(Address address) => Context.ActorSelection(ClusterHeartbeatReceiver.Path(address));
    }

    internal sealed class CrossDcHeartbeatingState
    {
        private static readonly ImmutableSortedSet<Member> EmptyMembersSortedSet = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering);
        internal static Func<Member, bool> AtLeastInUpState { get; } = m => m.Status != MemberStatus.WeaklyUp && m.Status != MemberStatus.Joining;
        public static CrossDcHeartbeatingState Init(string selfDataCenter,
            IFailureDetectorRegistry<Address> crossDcFailureDetector,
            int nrOfMonitoredNodesPerDc,
            ImmutableSortedSet<Member> members)
        {
            // TODO unduplicate this with the logic in MembershipState.ageSortedTopOldestMembersPerDc
            var groupedByDc = members
                .Where(AtLeastInUpState)
                .GroupBy(m => m.DataCenter)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableSortedSet(Member.AgeOrdering));

            return new CrossDcHeartbeatingState(
                selfDataCenter,
                crossDcFailureDetector,
                nrOfMonitoredNodesPerDc,
                groupedByDc);
        }

        public string SelfDataCenter { get; }
        public IFailureDetectorRegistry<Address> FailureDetector { get; }
        public int NrOfMonitoredNodesPerDc { get; }
        public ImmutableDictionary<string, ImmutableSortedSet<Member>> State { get; }

        public CrossDcHeartbeatingState(string selfDataCenter, IFailureDetectorRegistry<Address> failureDetector, int nrOfMonitoredNodesPerDc, ImmutableDictionary<string, ImmutableSortedSet<Member>> state)
        {
            this.SelfDataCenter = selfDataCenter;
            this.FailureDetector = failureDetector;
            this.NrOfMonitoredNodesPerDc = nrOfMonitoredNodesPerDc;
            this.State = state;
        }

        public ImmutableHashSet<UniqueAddress> ActiveReceivers
        {
            get
            {
                var otherDc = State.Where(pair => pair.Key != SelfDataCenter);
                var allOtherNodes = otherDc.Select(pair => pair.Value);

                return allOtherNodes
                    .SelectMany(set =>
                        set.Take(NrOfMonitoredNodesPerDc).Select(m => m.UniqueAddress))
                    .ToImmutableHashSet();
            }
        }

        public IEnumerable<Member> AllMembers => State.Values.SelectMany(x => x);

        public ImmutableHashSet<string> DataCenters => State.Keys.ToImmutableHashSet();

        public bool ShouldActivelyMonitorNodes(string selfDc, UniqueAddress selfAddress)
        {
            // Since we need ordering of oldests guaranteed, we must only look at Up (or Leaving, Exiting...) nodes
            var selfDcNeighbours = State.GetValueOrDefault(selfDc, EmptyMembersSortedSet);
            var selfDcOldOnes = selfDcNeighbours.Where(AtLeastInUpState).Take(NrOfMonitoredNodesPerDc);

            // if this node is part of the "n oldest nodes" it should indeed monitor other nodes:
            foreach (var member in selfDcOldOnes) // use for instead of LINQ - less allocations
                if (member.UniqueAddress == selfAddress) return true;
            return false;
        }

        public CrossDcHeartbeatingState HeartbeatRsp(UniqueAddress from)
        {
            if (ActiveReceivers.Contains(from))
            {
                FailureDetector.Heartbeat(from.Address);
            }
            return this;
        }

        public CrossDcHeartbeatingState AddMember(Member member)
        {
            var dc = member.DataCenter;

            // we need to remove the member first, to avoid having "duplicates"
            // this is because the removal and uniqueness we need is only by uniqueAddress
            // which is not used by the `ageOrdering`
            var oldMembersWithoutM = State.GetValueOrDefault(dc, EmptyMembersSortedSet)
                .Where(m => m.UniqueAddress != member.UniqueAddress)
                .ToImmutableSortedSet(Member.AgeOrdering);

            var updatedMembers = oldMembersWithoutM.Add(member);
            var updatedState = this.Copy(state: State.SetItem(dc, updatedMembers));

            // guarding against the case of two members having the same upNumber, in which case the activeReceivers
            // which are based on the ageOrdering could actually have changed by adding a node. In practice this
            // should happen rarely, since upNumbers are assigned sequentially, and we only ever compare nodes
            // in the same DC. If it happens though, we need to remove the previously monitored node from the failure
            // detector, to prevent both a resource leak and that node actually appearing as unreachable in the gossip (!)
            var stoppedMonitoringReceivers = updatedState.ActiveReceiversIn(dc).Except(this.ActiveReceiversIn(dc));
            foreach (var a in stoppedMonitoringReceivers)
                FailureDetector.Remove(a.Address);

            return updatedState;
        }

        public CrossDcHeartbeatingState RemoveMember(Member member)
        {
            var dc = member.DataCenter;
            if (State.TryGetValue(dc, out var dcMembers))
            {
                var updatedMembers = dcMembers.Where(m => m.UniqueAddress != member.UniqueAddress)
                    .ToImmutableSortedSet(Member.AgeOrdering);

                FailureDetector.Remove(member.Address);
                return this.Copy(state: State.SetItem(dc, updatedMembers));
            }
            else return this;
        }

        private ImmutableHashSet<UniqueAddress> ActiveReceiversIn(string dc)
        {
            // CrossDcHeartbeatSender is not supposed to send within its own Dc
            if (dc == SelfDataCenter) return ImmutableHashSet<UniqueAddress>.Empty;
            else
            {
                var otherNodes = State.GetValueOrDefault(dc, EmptyMembersSortedSet);
                return otherNodes
                    .Take(NrOfMonitoredNodesPerDc)
                    .Select(m => m.UniqueAddress)
                    .ToImmutableHashSet();
            }
        }

        private CrossDcHeartbeatingState Copy(string selfDataCenter = null,
            IFailureDetectorRegistry<Address> failureDetector = null,
            int? nrOfMonitoredNodesPerDc = null,
            ImmutableDictionary<string, ImmutableSortedSet<Member>> state = null)
        {
            return new CrossDcHeartbeatingState(
                selfDataCenter: selfDataCenter ?? SelfDataCenter,
                failureDetector: failureDetector ?? FailureDetector,
                nrOfMonitoredNodesPerDc: nrOfMonitoredNodesPerDc ?? NrOfMonitoredNodesPerDc,
                state: state ?? State);
        }
    }
}