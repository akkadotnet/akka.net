//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Akka.Actor;
using Akka.Coordination;
using Akka.Event;
using Akka.Remote;
using static Akka.Cluster.ClusterEvent;

namespace Akka.Cluster.SBR
{
    /// <summary>
    ///     Unreachable members will be downed by this actor according to the given strategy.
    ///     It is active on the leader node in the cluster.
    ///     The implementation is split into two classes SplitBrainResolver and SplitBrainResolverBase to be
    ///     able to unit test the logic without running cluster.
    /// </summary>
    internal class SplitBrainResolver : SplitBrainResolverBase
    {
        private readonly Cluster cluster;

        public SplitBrainResolver(TimeSpan stableAfter, DowningStrategy strategy)
            : base(stableAfter, strategy)
        {
            cluster = Cluster.Get(Context.System);
            Log.Info(
                "SBR started. Config: strategy [{0}], stable-after [{1}], down-all-when-unstable [{2}], selfUniqueAddress [{3}].",
                Logging.SimpleName(strategy.GetType()),
                stableAfter,
                // ReSharper disable VirtualMemberCallInConstructor
                DownAllWhenUnstable == TimeSpan.Zero ? "off" : DownAllWhenUnstable.ToString(),
                SelfUniqueAddress.Address);
            // ReSharper restore VirtualMemberCallInConstructor
        }

        public override UniqueAddress SelfUniqueAddress => cluster.SelfUniqueAddress;

        public static Props Props2(TimeSpan stableAfter, DowningStrategy strategy)
        {
            return Props.Create(() => new SplitBrainResolver(stableAfter, strategy));
        }

        // re-subscribe when restart
        protected override void PreStart()
        {
            cluster.Subscribe(Self, InitialStateAsEvents, typeof(IClusterDomainEvent));
            base.PreStart();
        }

        protected override void PostStop()
        {
            cluster.Unsubscribe(Self);
            base.PostStop();
        }

        public override void Down(UniqueAddress node, IDecision decision)
        {
            Log.Info("SBR is downing [{0}]", node);
            cluster.Down(node.Address);
        }
    }

    /// <summary>
    ///     The implementation is split into two classes SplitBrainResolver and SplitBrainResolverBase to be
    ///     able to unit test the logic without running cluster.
    /// </summary>
    internal abstract class SplitBrainResolverBase : ActorBase, IWithUnboundedStash, IWithTimers
    {
        private readonly TimeSpan releaseLeaseAfter;

        // would be better as constructor parameter, but don't want to break Cinnamon instrumentation
        private readonly SplitBrainResolverSettings settings;
        private ILoggingAdapter _log;


        private ReachabilityChangedStats reachabilityChangedStats =
            new ReachabilityChangedStats(DateTime.UtcNow, DateTime.UtcNow, 0);

        private IReleaseLeaseCondition releaseLeaseCondition = ReleaseLeaseCondition.NoLease.Instance;
        private bool selfMemberAdded;

        private Deadline stableDeadline;

        protected SplitBrainResolverBase(TimeSpan stableAfter, DowningStrategy strategy)
        {
            StableAfter = stableAfter;
            Strategy = strategy;

            settings = new SplitBrainResolverSettings(Context.System.Settings.Config);
            releaseLeaseAfter = stableAfter + stableAfter;

            // ReSharper disable once VirtualMemberCallInConstructor
            Timers.StartPeriodicTimer(Tick.Instance, Tick.Instance, TickInterval);

            ResetStableDeadline();
        }

        public TimeSpan StableAfter { get; }

        public DowningStrategy Strategy { get; }

        public ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        public abstract UniqueAddress SelfUniqueAddress { get; }

        public virtual TimeSpan DownAllWhenUnstable => settings.DownAllWhenUnstable;

        public virtual TimeSpan TickInterval => TimeSpan.FromSeconds(1);

        protected bool Leader { get; private set; }

        public bool IsResponsible => Leader && selfMemberAdded;

        public ITimerScheduler Timers { get; set; }

        public IStash Stash { get; set; }

        public abstract void Down(UniqueAddress node, IDecision decision);

        //  private def internalDispatcher: ExecutionContext =
        //    context.system.asInstanceOf[ExtendedActorSystem].dispatchers.internalDispatcher

        // overridden in tests
        protected virtual Deadline NewStableDeadline()
        {
            return Deadline.Now + StableAfter;
        }

        public void ResetStableDeadline()
        {
            stableDeadline = NewStableDeadline();
        }

        private void ResetReachabilityChangedStats()
        {
            var now = DateTime.UtcNow;
            reachabilityChangedStats = new ReachabilityChangedStats(now, now, 0);
        }

        private void ResetReachabilityChangedStatsIfAllUnreachableDowned()
        {
            if (!reachabilityChangedStats.IsEmpty && Strategy.IsAllUnreachableDownOrExiting)
            {
                Log.Debug("SBR resetting reachability stats, after all unreachable healed, downed or removed");
                ResetReachabilityChangedStats();
            }
        }

        /// <summary>
        ///     Helper to wrap updates to strategy info with, so that stable-after timer is reset and information is logged about
        ///     state change */
        /// </summary>
        /// <param name="resetStable"></param>
        /// <param name="f"></param>
        public void MutateMemberInfo(bool resetStable, Action f)
        {
            var unreachableBefore = Strategy.Unreachable.Count;
            f();
            var unreachableAfter = Strategy.Unreachable.Count;

            string EarliestTimeOfDecision()
            {
                return (DateTime.UtcNow + StableAfter).ToString(CultureInfo.InvariantCulture);
            }

            if (resetStable)
            {
                if (IsResponsible)
                {
                    if (unreachableBefore == 0 && unreachableAfter > 0)
                        Log.Info(
                            "SBR found unreachable members, waiting for stable-after = {0} ms before taking downing decision. " +
                            "Now {1} unreachable members found. Downing decision will not be made before {2}.",
                            StableAfter.TotalMilliseconds,
                            unreachableAfter,
                            EarliestTimeOfDecision());
                    else if (unreachableBefore > 0 && unreachableAfter == 0)
                        Log.Info(
                            "SBR found all unreachable members healed during stable-after period, no downing decision necessary for now.");
                    else if (unreachableAfter > 0)
                        Log.Info(
                            "SBR found unreachable members changed during stable-after period. Resetting timer. " +
                            "Now {0} unreachable members found. Downing decision will not be made before {1}.",
                            unreachableAfter,
                            EarliestTimeOfDecision());
                    // else no unreachable members found but set of members changed
                }

                Log.Debug("SBR reset stable deadline when members/unreachable changed");
                ResetStableDeadline();
            }
        }

        /// <summary>
        ///     Helper to wrap updates to `leader` and `selfMemberAdded` to log changes in responsibility status */
        /// </summary>
        /// <param name="f"></param>
        public void MutateResponsibilityInfo(Action f)
        {
            var responsibleBefore = IsResponsible;
            f();
            var responsibleAfter = IsResponsible;

            if (!responsibleBefore && responsibleAfter)
                Log.Info(
                    "This node is now the leader responsible for taking SBR decisions among the reachable nodes " +
                    "(more leaders may exist).");
            else if (responsibleBefore && !responsibleAfter)
                Log.Info("This node is not the leader any more and not responsible for taking SBR decisions.");

            if (Leader && !selfMemberAdded)
                Log.Debug("This node is leader but !selfMemberAdded.");
        }

        protected override void PostStop()
        {
            if (!(releaseLeaseCondition is ReleaseLeaseCondition.NoLease))
                Log.Info(
                    "SBR is stopped and owns the lease. The lease will not be released until after the " +
                    "lease heartbeat-timeout.");
            base.PostStop();
        }


        protected override bool Receive(object message)
        {
            switch (message)
            {
                case SeenChanged ch:
                    SeenChanged(ch.SeenBy);
                    return true;
                case MemberJoined ch:
                    AddJoining(ch.Member);
                    return true;
                case MemberWeaklyUp ch:
                    AddWeaklyUp(ch.Member);
                    return true;
                case MemberUp ch:
                    AddUp(ch.Member);
                    return true;
                case MemberLeft ch:
                    Leaving(ch.Member);
                    return true;
                case MemberExited ch:
                    Exited(ch.Member);
                    return true;
                case UnreachableMember ch:
                    UnreachableMember(ch.Member);
                    return true;
                case MemberDowned ch:
                    UnreachableMember(ch.Member);
                    return true;
                case ReachableMember ch:
                    ReachableMember(ch.Member);
                    return true;
                case ReachabilityChanged ch:
                    ReachabilityChanged(ch.Reachability);
                    return true;
                case MemberRemoved ch:
                    Remove(ch.Member);
                    return true;
                case LeaderChanged ch:
                    LeaderChanged(ch.Leader);
                    return true;
                case ReleaseLeaseResult lr:
                    OnReleaseLeaseResult(lr.Released);
                    break;
                case Tick _:
                    OnTick();
                    return true;
                case IClusterDomainEvent _:
                    // not interested in other events
                    return true;
            }

            return false;
        }

        private void LeaderChanged(Address leaderOption)
        {
            MutateResponsibilityInfo(() => { Leader = leaderOption?.Equals(SelfUniqueAddress.Address) == true; });
        }

        private void OnTick()
        {
            // note the DownAll due to instability is running on all nodes to make that decision as quickly and
            // aggressively as possible if time is out
            if (reachabilityChangedStats.ChangeCount > 0)
            {
                var now = DateTime.UtcNow;
                var durationSinceLatestChange = now - reachabilityChangedStats.LatestChangeTimestamp;
                var durationSinceFirstChange = now - reachabilityChangedStats.FirstChangeTimestamp;

                var downAllWhenUnstableEnabled = DownAllWhenUnstable > TimeSpan.Zero;
                if (downAllWhenUnstableEnabled && durationSinceFirstChange > StableAfter + DownAllWhenUnstable)
                {
                    Log.Warning(
                        //ClusterLogMarker.sbrInstability,
                        "SBR detected instability and will down all nodes: {0}",
                        reachabilityChangedStats);
                    ActOnDecision(DownAll.Instance);
                }
                else if (!downAllWhenUnstableEnabled && durationSinceLatestChange > StableAfter + StableAfter)
                {
                    // downAllWhenUnstable is disabled but reset for meaningful logging
                    Log.Debug("SBR no reachability changes within {0} ms, resetting stats",
                        (StableAfter + StableAfter).TotalMilliseconds);
                    ResetReachabilityChangedStats();
                }
            }

            if (IsResponsible && !Strategy.Unreachable.IsEmpty && stableDeadline.IsOverdue)
                switch (Strategy.Decide())
                {
                    case IAcquireLeaseDecision decision:

                        switch (Strategy.Lease)
                        {
                            case Lease lease:
                                if (lease.CheckLease())
                                {
                                    Log.Info(
                                        "SBR has acquired lease for decision [{0}]",
                                        decision);
                                    ActOnDecision(decision);
                                }
                                else
                                {
                                    if (decision.AcquireDelay == TimeSpan.Zero)
                                    {
                                        OnAcquireLease(); // reply message is AcquireLeaseResult
                                    }
                                    else
                                    {
                                        Log.Debug("SBR delayed attempt to acquire lease for [{0} ms]",
                                            decision.AcquireDelay.TotalMilliseconds);
                                        Timers.StartSingleTimer(AcquireLease.Instance, AcquireLease.Instance,
                                            decision.AcquireDelay);
                                    }

                                    Context.Become(WaitingForLease(decision));
                                }

                                break;
                            default:
                                throw new InvalidOperationException(
                                    "Unexpected lease decision although lease is not configured");
                        }

                        break;
                    case IDecision decision:
                        ActOnDecision(decision);
                        break;
                }

            switch (releaseLeaseCondition)
            {
                case ReleaseLeaseCondition.WhenTimeElapsed rlc:
                    if (rlc.Deadline.IsOverdue)
                        ReleaseLease(); // reply message is ReleaseLeaseResult, which will update the releaseLeaseCondition
                    break;
            }
        }

        private void OnAcquireLease()
        {
            Log.Debug("SBR trying to acquire lease");
            //implicit val ec: ExecutionContext = internalDispatcher
            Strategy.Lease?.Acquire().ContinueWith(r =>
                {
                    if (r.IsFaulted)
                        Log.Error(r.Exception, "SBR acquire of lease failed");
                    return new AcquireLeaseResult(!r.IsFaulted ? r.Result : false);
                })
                .PipeTo(Self);
        }

        public Receive WaitingForLease(IDecision decision)
        {
            bool Receive(object message)
            {
                switch (message)
                {
                    case AcquireLease _:
                        OnAcquireLease(); // reply message is LeaseResult
                        return true;

                    case AcquireLeaseResult lr:
                        if (lr.HoldingLease)
                        {
                            Log.Info("SBR acquired lease for decision [{0}]", decision);
                            var downedNodes = ActOnDecision(decision);
                            switch (releaseLeaseCondition)
                            {
                                case ReleaseLeaseCondition.WhenMembersRemoved rlc:
                                    releaseLeaseCondition =
                                        new ReleaseLeaseCondition.WhenMembersRemoved(rlc.Nodes.Union(downedNodes));
                                    break;
                                default:
                                    if (downedNodes.IsEmpty)
                                        releaseLeaseCondition =
                                            new ReleaseLeaseCondition.WhenTimeElapsed(Deadline.Now + releaseLeaseAfter);
                                    else
                                        releaseLeaseCondition =
                                            new ReleaseLeaseCondition.WhenMembersRemoved(downedNodes);
                                    break;
                            }
                        }
                        else
                        {
                            var reverseDecision = Strategy.ReverseDecision(decision);
                            Log.Info(
                                "SBR couldn't acquire lease, reverse decision [{0}] to [{1}]",
                                decision,
                                reverseDecision);
                            ActOnDecision(reverseDecision);
                            releaseLeaseCondition = ReleaseLeaseCondition.NoLease.Instance;
                        }

                        Stash.UnstashAll();
                        Context.Become(Receive);
                        return true;

                    case ReleaseLeaseResult lr:
                        // superseded by new acquire release request
                        return true;
                    case Tick _:
                        // ignore ticks while waiting
                        return true;
                    default:
                        Stash.Stash();
                        return true;
                }
            }

            return Receive;
        }

        private void OnReleaseLeaseResult(bool released)
        {
            switch (releaseLeaseCondition)
            {
                case ReleaseLeaseCondition.WhenTimeElapsed rlc:
                    if (released && rlc.Deadline.IsOverdue)
                    {
                        Log.Info("SBR released lease.");
                        releaseLeaseCondition = ReleaseLeaseCondition.NoLease.Instance; // released successfully
                    }

                    break;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="decision"></param>
        /// <returns>the nodes that were downed</returns>
        public ImmutableHashSet<UniqueAddress> ActOnDecision(IDecision decision)
        {
            ImmutableHashSet<UniqueAddress> nodesToDown;
            try
            {
                nodesToDown = Strategy.NodesToDown(decision);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning(ex, ex.Message);
                nodesToDown = Strategy.NodesToDown(DownAll.Instance);
            }

            ObserveDecision(decision, nodesToDown);

            if (!nodesToDown.IsEmpty)
            {
                var downMyself = nodesToDown.Contains(SelfUniqueAddress);
                // downing is idempotent, and we also avoid calling down on nodes with status Down
                // down selfAddress last, since it may shutdown itself if down alone
                foreach (var uniqueAddress in nodesToDown)
                    if (!uniqueAddress.Equals(SelfUniqueAddress))
                        Down(uniqueAddress, decision);
                if (downMyself)
                    Down(SelfUniqueAddress, decision);

                ResetReachabilityChangedStats();
                ResetStableDeadline();
            }

            return nodesToDown;
        }

        public void ObserveDecision(
            IDecision decision,
            ImmutableHashSet<UniqueAddress> nodesToDown
        )
        {
            var downMyself = nodesToDown.Contains(SelfUniqueAddress);

            var indirectlyConnectedLogMessage = decision.IsIndirectlyConnected
                ? $", indirectly connected [{string.Join(", ", Strategy.IndirectlyConnected)}]"
                : "";

            Log.Warning(
              $"SBR took decision {decision} and is downing [{string.Join(", ", nodesToDown.Select(i => i.Address))}]{(downMyself ? " including myself, " : "")}, " +
                  $"[{Strategy.Unreachable.Count}] unreachable of [{Strategy.Members.Count}] members" +
                  indirectlyConnectedLogMessage +
                  $", full reachability status: [{Strategy.Reachability}]");
        }

        public void UnreachableMember(Member m)
        {
            if (!m.UniqueAddress.Equals(SelfUniqueAddress))
            {
                Log.Debug("SBR unreachableMember [{0}]", m);
                MutateMemberInfo(true, () =>
                {
                    Strategy.AddUnreachable(m);
                    UpdateReachabilityChangedStats();
                    ResetReachabilityChangedStatsIfAllUnreachableDowned();
                    if (!reachabilityChangedStats.IsEmpty)
                        Log.Debug("SBR noticed {0}", reachabilityChangedStats);
                });
            }
        }

        public void ReachableMember(Member m)
        {
            if (!m.UniqueAddress.Equals(SelfUniqueAddress))
            {
                Log.Debug("SBR reachableMember [{0}]", m);
                MutateMemberInfo(true, () =>
                {
                    Strategy.AddReachable(m);
                    UpdateReachabilityChangedStats();
                    ResetReachabilityChangedStatsIfAllUnreachableDowned();
                    if (!reachabilityChangedStats.IsEmpty)
                        Log.Debug("SBR noticed {0}", reachabilityChangedStats);
                });
            }
        }

        private void ReachabilityChanged(Reachability r)
        {
            Strategy.SetReachability(r);
        }

        private void UpdateReachabilityChangedStats()
        {
            var now = DateTime.UtcNow;
            if (reachabilityChangedStats.ChangeCount == 0)
                reachabilityChangedStats = new ReachabilityChangedStats(now, now, 1);
            else
                reachabilityChangedStats = new ReachabilityChangedStats(
                    reachabilityChangedStats.FirstChangeTimestamp,
                    now,
                    reachabilityChangedStats.ChangeCount + 1
                );
        }

        public void SeenChanged(ImmutableHashSet<Address> seenBy)
        {
            Strategy.SetSeenBy(seenBy);
        }

        public void AddUp(Member m)
        {
            Log.Debug("SBR add Up [{0}]", m);
            MutateMemberInfo(true, () =>
            {
                Strategy.Add(m);
                if (m.UniqueAddress.Equals(SelfUniqueAddress))
                    MutateResponsibilityInfo(() => { selfMemberAdded = true; });
            });
            switch (Strategy)
            {
                case StaticQuorum s:
                    if (s.IsTooManyMembers)
                        Log.Warning(
                            "The cluster size is [{0}] and static-quorum.quorum-size is [{1}]. You should not add " +
                            "more than [{2}] (static-quorum.size * 2 - 1) members to the cluster. If the exceeded cluster size " +
                            "remains when a SBR decision is needed it will down all nodes.",
                            s.MembersWithRole.Count,
                            s.QuorumSize,
                            s.QuorumSize * 2 - 1);
                    break;
            }
        }

        public void Leaving(Member m)
        {
            Log.Debug("SBR leaving [{0}]", m);
            MutateMemberInfo(false, () => { Strategy.Add(m); });
        }

        public void Exited(Member m)
        {
            Log.Debug("SBR exited [{0}]", m);
            MutateMemberInfo(resetStable: true, () =>
            {
                Strategy.Add(m);
            });
        }

        public void AddJoining(Member m)
        {
            Log.Debug("SBR add Joining/WeaklyUp [{0}]", m);
            Strategy.Add(m);
        }

        public void AddWeaklyUp(Member m)
        {
            if (m.UniqueAddress.Equals(SelfUniqueAddress))
                MutateResponsibilityInfo(() => { selfMemberAdded = true; });
            // treat WeaklyUp in same way as joining
            AddJoining(m);
        }

        public void Remove(Member m)
        {
            if (m.UniqueAddress.Equals(SelfUniqueAddress))
                Context.Stop(Self);
            else
                MutateMemberInfo(false, () =>
                {
                    Log.Debug("SBR remove [{0}]", m);
                    Strategy.Remove(m);

                    ResetReachabilityChangedStatsIfAllUnreachableDowned();

                    switch (releaseLeaseCondition)
                    {
                        case ReleaseLeaseCondition.WhenMembersRemoved rlc:
                            var remainingDownedNodes = rlc.Nodes.Remove(m.UniqueAddress);

                            if (remainingDownedNodes.IsEmpty)
                                releaseLeaseCondition =
                                    new ReleaseLeaseCondition.WhenTimeElapsed(Deadline.Now + releaseLeaseAfter);
                            else
                                releaseLeaseCondition =
                                    new ReleaseLeaseCondition.WhenMembersRemoved(remainingDownedNodes);
                            break;
                    }
                });
        }

        private void ReleaseLease()
        {
            //    implicit val ec: ExecutionContext = internalDispatcher
            if (Strategy.Lease != null)
                if (!(releaseLeaseCondition is ReleaseLeaseCondition.NoLease))
                {
                    Log.Debug("SBR releasing lease");
                    Strategy.Lease.Release().ContinueWith(r => new ReleaseLeaseResult(!r.IsFaulted ? r.Result : false))
                        .PipeTo(Self);
                }
        }

        internal class Tick
        {
            public static readonly Tick Instance = new Tick();

            private Tick()
            {
            }
        }

        /// <summary>
        ///     Response (result) of the acquire lease request.
        /// </summary>
        protected class AcquireLeaseResult
        {
            public AcquireLeaseResult(bool holdingLease)
            {
                HoldingLease = holdingLease;
            }

            public bool HoldingLease { get; }
        }

        /// <summary>
        ///     Response (result) of the release lease request.
        /// </summary>
        protected class ReleaseLeaseResult
        {
            public ReleaseLeaseResult(bool released)
            {
                Released = released;
            }

            public bool Released { get; }
        }

        /// <summary>
        ///     For delayed acquire of the lease.
        /// </summary>
        protected class AcquireLease
        {
            public static readonly AcquireLease Instance = new AcquireLease();

            private AcquireLease()
            {
            }
        }

        protected class ReachabilityChangedStats
        {
            public ReachabilityChangedStats(DateTime firstChangeTimestamp, DateTime latestChangeTimestamp,
                long changeCount)
            {
                FirstChangeTimestamp = firstChangeTimestamp;
                LatestChangeTimestamp = latestChangeTimestamp;
                ChangeCount = changeCount;
            }

            public DateTime FirstChangeTimestamp { get; }
            public DateTime LatestChangeTimestamp { get; }
            public long ChangeCount { get; }

            public bool IsEmpty => ChangeCount == 0;

            public override string ToString()
            {
                if (IsEmpty) return "reachability unchanged";

                var now = DateTime.UtcNow;
                return
                    $"reachability changed {ChangeCount} times since {(now - FirstChangeTimestamp).TotalMilliseconds} ms ago, " +
                    $"latest change was {(now - LatestChangeTimestamp).TotalMilliseconds} ms ago";
            }
        }

        protected interface IReleaseLeaseCondition
        {
        }

        protected static class ReleaseLeaseCondition
        {
            public class NoLease : IReleaseLeaseCondition
            {
                public static readonly NoLease Instance = new NoLease();

                private NoLease()
                {
                }
            }

            public class WhenMembersRemoved : IReleaseLeaseCondition
            {
                public WhenMembersRemoved(ImmutableHashSet<UniqueAddress> nodes)
                {
                    Nodes = nodes;
                }

                public ImmutableHashSet<UniqueAddress> Nodes { get; }
            }

            public class WhenTimeElapsed : IReleaseLeaseCondition
            {
                public WhenTimeElapsed(Deadline deadline)
                {
                    Deadline = deadline;
                }

                public Deadline Deadline { get; }
            }
        }
    }
}
