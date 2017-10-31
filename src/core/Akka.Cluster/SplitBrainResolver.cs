//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly Config _splitBrainResolverConfig;

        public SplitBrainResolver(ActorSystem system)
        {
            _clusterSettings = Cluster.Get(system).Settings;
            _splitBrainResolverConfig = system.Settings.Config.GetConfig("akka.cluster.split-brain-resolver");
        }

        public TimeSpan DownRemovalMargin => _clusterSettings.DownRemovalMargin;
        public Props DowningActorProps => SplitBrainDecider.Props(_splitBrainResolverConfig);
    }

    internal sealed class NetworkPartitionContext
    {
        /// <summary>
        /// Address of a current cluster node.
        /// </summary>
        public Address SelfAddress { get; }

        /// <summary>
        /// A set of nodes, that have been detected as unreachable since cluster state stability has been reached.
        /// </summary>
        public ImmutableSortedSet<Member> Unreachable { get; }

        /// <summary>
        /// A set of nodes, that have been connected to a current cluster node since the last cluster state
        /// stability has been reached.
        /// </summary>
        public ImmutableSortedSet<Member> Remaining { get; }

        public NetworkPartitionContext(Address selfAddress, ImmutableSortedSet<Member> unreachable, ImmutableSortedSet<Member> remaining)
        {
            SelfAddress = selfAddress;
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

    internal sealed class StaticQuorum : ISplitBrainStrategy
    {
        public StaticQuorum(Config config) : this(
           quorumSize: config.GetInt("quorum-size"),
           role: config.GetString("role"))
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
    }

    internal sealed class KeepMajority : ISplitBrainStrategy
    {
        public KeepMajority(Config config) : this(
            role: config.GetString("role"))
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
            else if (remaining.Count > unreachable.Count) return context.Unreachable;
            else
            {
                // if the parts are of equal size the part containing the node with the lowest address is kept.
                var oldest = remaining.Union(unreachable).First();
                return remaining.Contains(oldest) 
                    ? context.Unreachable 
                    : context.Remaining;
            }
        }

        private ImmutableSortedSet<Member> MembersWithRole(ImmutableSortedSet<Member> members) => string.IsNullOrEmpty(Role)
            ? members
            : members.Where(m => m.HasRole(Role)).ToImmutableSortedSet();
    }

    internal sealed class KeepOldest : ISplitBrainStrategy
    {
        public KeepOldest(Config config) : this(
            downIfAlone: config.GetBoolean("down-if-alone"),
            role: config.GetString("role"))
        { }

        public KeepOldest(bool downIfAlone, string role)
        {
            DownIfAlone = downIfAlone;
            Role = role;
        }

        public string Role { get; }
        public bool DownIfAlone { get; }

        public IEnumerable<Member> Apply(NetworkPartitionContext context)
        {
            var oldest = context.Remaining.Union(context.Unreachable).First();
            if (context.Remaining.Contains(oldest))
            {
                if (DownIfAlone && context.Remaining.Count == 1) // oldest is current node, and it's alone
                {
                    yield return oldest;
                }
            }
            else if (DownIfAlone && context.Unreachable.Count == 1) // oldest is unreachable, but it's alone
            {
                yield return oldest;
            }
        }
    }

    internal sealed class KeepReferee : ISplitBrainStrategy
    {
        public KeepReferee(Config config) : this(
            address: Address.Parse(config.GetString("address")),
            downAllIfLessThanNodes: config.GetInt("down-all-if-less-than-nodes"))
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

        public static Actor.Props Props(Config config) => Actor.Props.Create(() => new SplitBrainDecider(config)).WithDeploy(Deploy.Local);

        private readonly Cluster _cluster;
        private readonly TimeSpan _stabilityTimeout;
        private readonly ISplitBrainStrategy _strategy;

        private ImmutableSortedSet<Member> _reachable = ImmutableSortedSet<Member>.Empty;
        private ImmutableSortedSet<Member> _unreachable = ImmutableSortedSet<Member>.Empty;
        private ICancelable _stabilityTask;
        private ILoggingAdapter _log;

        public SplitBrainDecider(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _stabilityTimeout = config.GetTimeSpan("stable-after");
            _strategy = ResolveSplitBrainStrategy(config);
            _cluster = Cluster.Get(Context.System);
        }

        private ISplitBrainStrategy ResolveSplitBrainStrategy(Config config)
        {
            var activeStrategy = config.GetString("active-strategy");
            switch (activeStrategy)
            {
                case "static-quorum": return new StaticQuorum(config.GetConfig("static-quorum"));
                case "keep-majority": return new KeepMajority(config.GetConfig("keep-majority"));
                case "keep-oldest": return new KeepOldest(config.GetConfig("keep-oldest"));
                case "keep-referee": return new KeepReferee(config.GetConfig("keep-referee"));
                default: throw new ArgumentException($"`akka.cluster.split-brain-resolver.active-strategy` setting not recognized: [{activeStrategy}]. Available options are: static-quorum, keep-majority, keep-oldest, keep-referee.");
            }
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
            var context = new NetworkPartitionContext(_cluster.SelfAddress, _unreachable, _reachable);
            var nodesToDown = _strategy.Apply(context).ToImmutableArray();

            if (nodesToDown.Length > 0)
            {
                if (Log.IsInfoEnabled)
                {
                    Log.Info("A network partition has been detected. Split brain resolver decided to down following nodes: [{0}]", string.Join(", ", nodesToDown));
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