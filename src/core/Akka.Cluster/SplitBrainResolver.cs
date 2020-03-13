//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Cluster
{
    public sealed class SplitBrainResolver : IDowningProvider
    {
        private readonly ClusterSettings _clusterSettings;

        public SplitBrainResolver(ActorSystem system)
        {
            _clusterSettings = Cluster.Get(system).Settings;
            var config = system.Settings.Config.GetConfig("akka.cluster.split-brain-resolver");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<SplitBrainResolver>("akka.cluster.split-brain-resolver");

            StableAfter = config.GetTimeSpan("stable-after", null);
            Strategy = ResolveSplitBrainStrategy(config);
        }

        public TimeSpan DownRemovalMargin => _clusterSettings.DownRemovalMargin;
        public TimeSpan StableAfter { get; }
        public Props DowningActorProps => SplitBrainDecider.Props(StableAfter, Strategy);

        internal ISplitBrainStrategy Strategy { get; }

        private ISplitBrainStrategy ResolveSplitBrainStrategy(Config config)
        {
            var activeStrategy = config.GetString("active-strategy", null);
            switch (activeStrategy)
            {
                case "static-quorum": return new StaticQuorum(config.GetConfig("static-quorum"));
                case "keep-majority": return new KeepMajority(config.GetConfig("keep-majority"));
                case "keep-oldest": return new KeepOldest(config.GetConfig("keep-oldest"));
                case "keep-referee": return new KeepReferee(config.GetConfig("keep-referee"));
                default: throw new ArgumentException($"`akka.cluster.split-brain-resolver.active-strategy` setting not recognized: [{activeStrategy}]. Available options are: static-quorum, keep-majority, keep-oldest, keep-referee.");
            }
        }
    }

    internal sealed class NetworkPartitionContext
    {
        /// <summary>
        /// A set of nodes, that have been detected as unreachable since cluster state stability has been reached.
        /// </summary>
        public ImmutableSortedSet<Member> Unreachable { get; }

        /// <summary>
        /// A set of nodes, that have been connected to a current cluster node since the last cluster state
        /// stability has been reached.
        /// </summary>
        public ImmutableSortedSet<Member> Remaining { get; }

        public NetworkPartitionContext(ImmutableSortedSet<Member> unreachable, ImmutableSortedSet<Member> remaining)
        {
            Unreachable = unreachable;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// A split brain resolver strategy used to determine which nodes should be downed when a network partition has been detected.
    /// </summary>
    internal interface ISplitBrainStrategy
    {
        /// <summary>
        /// This strategy is expected to be applied among nodes with that role.
        /// </summary>
        string Role { get; }

        /// <summary>
        /// Determines a behavior of the current cluster node in the face of network partition.
        /// Returns a list of cluster nodes to be downed (by the current node).
        /// </summary>
        IEnumerable<Member> Apply(NetworkPartitionContext context);
    }

    // TODO: Can quorum size be 0 and role be null?
    internal sealed class StaticQuorum : ISplitBrainStrategy
    {
        public StaticQuorum(Config config) : this(
           quorumSize: config.GetInt("quorum-size", 0),
           role: config.GetString("role", null))
        { }

        public StaticQuorum(int quorumSize, string role)
        {
            QuorumSize = quorumSize;
            Role = role;
        }

        public int QuorumSize { get; }
        public string Role { get; }

        public IEnumerable<Member> Apply(NetworkPartitionContext context)
        {
            var remainingCount = string.IsNullOrEmpty(Role)
                ? context.Remaining.Count
                : context.Remaining.Count(m => m.HasRole(Role));

            return remainingCount < QuorumSize
                ? context.Remaining
                : context.Unreachable;
        }
        public override string ToString() => $"StaticQuorum(quorumSize: {QuorumSize}, role: '{Role}')";
    }

    internal sealed class KeepMajority : ISplitBrainStrategy
    {
        public KeepMajority(Config config) : this(
            role: config.GetString("role", null))
        { }

        public KeepMajority(string role = null)
        {
            Role = role;
        }

        public string Role { get; }

        public IEnumerable<Member> Apply(NetworkPartitionContext context)
        {
            var remaining = MembersWithRole(context.Remaining);
            var unreachable = MembersWithRole(context.Unreachable);

            if (remaining.Count < unreachable.Count) return context.Remaining;
            if (remaining.Count > unreachable.Count) return context.Unreachable;
            if (remaining.IsEmpty && unreachable.IsEmpty) return new Member[0];

            // if the parts are of equal size the part containing the node with the lowest address is kept.
            var oldest = remaining.Union(unreachable).First();
            return remaining.Contains(oldest)
                ? context.Unreachable
                : context.Remaining;
        }

        private ImmutableSortedSet<Member> MembersWithRole(ImmutableSortedSet<Member> members) => string.IsNullOrEmpty(Role)
            ? members
            : members.Where(m => m.HasRole(Role)).ToImmutableSortedSet();

        public override string ToString() => $"KeepMajority(role: '{Role}')";
    }

    internal sealed class KeepOldest : ISplitBrainStrategy
    {
        public KeepOldest(Config config) : this(
            downIfAlone: config.GetBoolean("down-if-alone", true),
            role: config.GetString("role", null))
        { }

        public KeepOldest(bool downIfAlone, string role = null)
        {
            DownIfAlone = downIfAlone;
            Role = role;
        }

        public string Role { get; }
        public bool DownIfAlone { get; }

        public IEnumerable<Member> Apply(NetworkPartitionContext context)
        {
            var remaining = MembersWithRole(context.Remaining);
            var unreachable = MembersWithRole(context.Unreachable);

            if (remaining.IsEmpty && unreachable.IsEmpty) // prevent exception due to both lists being empty
            {
                return new Member[0];
            }

            var oldest = remaining.Union(unreachable).ToImmutableSortedSet(Member.AgeOrdering).First();
            if (remaining.Contains(oldest))
            {
                return DownIfAlone && context.Remaining.Count == 1 && context.Unreachable.Count > 0 // oldest is current node, and it's alone, but not the only node in the cluster
                    ? context.Remaining 
                    : context.Unreachable;
            }
            if (DownIfAlone && context.Unreachable.Count == 1) // oldest is unreachable, but it's alone
            {
                return context.Unreachable;
            }
            return context.Remaining;
        }

        private ImmutableSortedSet<Member> MembersWithRole(ImmutableSortedSet<Member> members) => string.IsNullOrEmpty(Role)
            ? members
            : members.Where(m => m.HasRole(Role)).ToImmutableSortedSet();

        public override string ToString() => $"KeepOldest(downIfAlone: {DownIfAlone}, role: '{Role})'";
    }

    internal sealed class KeepReferee : ISplitBrainStrategy
    {
        public KeepReferee(Config config) : this(
            address: Address.Parse(config.GetString("address", null)),
            downAllIfLessThanNodes: config.GetInt("down-all-if-less-than-nodes", 1))
        { }

        public KeepReferee(Address address, int downAllIfLessThanNodes)
        {
            Address = address;
            DownAllIfLessThanNodes = downAllIfLessThanNodes;
        }

        public Address Address { get; }
        public int DownAllIfLessThanNodes { get; }
        public string Role => null;

        public IEnumerable<Member> Apply(NetworkPartitionContext context)
        {
            var isRefereeReachable = context.Remaining.Any(m => m.Address == Address);

            if (!isRefereeReachable) return context.Remaining; // referee is unreachable
            else if (context.Remaining.Count < DownAllIfLessThanNodes) return context.Remaining.Union(context.Unreachable); // referee is reachable but there are too few remaining nodes 
            else return context.Unreachable;
        }

        public override string ToString() => $"KeepReferee(refereeAddress: {Address}, downIfLessThanNodes: {DownAllIfLessThanNodes})";
    }

    internal sealed class SplitBrainDecider : UntypedActor
    {
        #region internal classes

        private sealed class StabilityReached
        {
            public static readonly StabilityReached Instance = new StabilityReached();
            private StabilityReached() { }
        }

        #endregion

        public static Actor.Props Props(TimeSpan stableAfter, ISplitBrainStrategy strategy) => 
            Actor.Props.Create(() => new SplitBrainDecider(stableAfter, strategy)).WithDeploy(Deploy.Local);

        private readonly Cluster _cluster;
        private readonly TimeSpan _stabilityTimeout;
        private readonly ISplitBrainStrategy _strategy;

        private ImmutableSortedSet<Member> _reachable = ImmutableSortedSet<Member>.Empty;
        private ImmutableSortedSet<Member> _unreachable = ImmutableSortedSet<Member>.Empty;
        private ICancelable _stabilityTask;
        private ILoggingAdapter _log;

        public SplitBrainDecider(TimeSpan stableAfter, ISplitBrainStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));

            _stabilityTimeout = stableAfter;
            _strategy = strategy;
            _cluster = Cluster.Get(Context.System);
        }

        public ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected override void PreStart()
        {
            base.PreStart();
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent));
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            base.PostStop();
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    ResetStabilityTimeout();
                    _reachable = state.Members.Where(m => m.Status == MemberStatus.Up).ToImmutableSortedSet(Member.AgeOrdering);
                    _unreachable = state.Unreachable.ToImmutableSortedSet(Member.AgeOrdering);
                    return;
                case ClusterEvent.IMemberEvent memberEvent:
                    ResetStabilityTimeout();
                    switch (memberEvent)
                    {
                        case ClusterEvent.MemberUp up:
                            _reachable = _reachable.Add(up.Member);
                            break;
                        case ClusterEvent.MemberRemoved removed:
                            _reachable = _reachable.Remove(removed.Member);
                            _unreachable = _unreachable.Remove(removed.Member);
                            break;
                    }
                    return;
                case ClusterEvent.IReachabilityEvent reachabilityEvent:
                    ResetStabilityTimeout();
                    switch (reachabilityEvent)
                    {
                        case ClusterEvent.ReachableMember reachable:
                            _reachable = _reachable.Add(reachable.Member);
                            _unreachable = _unreachable.Remove(reachable.Member);
                            break;
                        case ClusterEvent.UnreachableMember unreachable:
                            _reachable = _reachable.Remove(unreachable.Member);
                            _unreachable = _unreachable.Add(unreachable.Member);
                            break;
                    }
                    return;
                case StabilityReached _:
                    HandleStabilityReached();
                    return;
            }
        }

        private void HandleStabilityReached()
        {
            if (Log.IsInfoEnabled && _unreachable.Any())
            {
                Log.Info("A network partition detected - unreachable nodes: [{0}], remaining: [{1}]", string.Join(", ", _unreachable.Select(m => m.Address)), string.Join(", ", _reachable.Select(m => m.Address)));
            }

            var context = new NetworkPartitionContext(_unreachable, _reachable);
            var nodesToDown = _strategy.Apply(context).ToImmutableArray();

            if (nodesToDown.Length > 0)
            {
                if (Log.IsInfoEnabled)
                {
                    Log.Info("A network partition has been detected. {0} decided to down following nodes: [{1}]", _strategy, string.Join(", ", nodesToDown));
                }

                foreach (var member in nodesToDown)
                {
                    _cluster.Down(member.Address);
                }
            }
        }

        private void ResetStabilityTimeout()
        {
            _stabilityTask?.Cancel();
            _stabilityTask = Context.System.Scheduler.ScheduleTellOnceCancelable(_stabilityTimeout, Self, StabilityReached.Instance, ActorRefs.NoSender);
        }
    }
}
