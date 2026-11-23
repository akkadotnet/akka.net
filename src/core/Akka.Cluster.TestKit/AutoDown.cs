//-----------------------------------------------------------------------
// <copyright file="AutoDown.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Globalization;
using Akka.Actor;
using Akka.Configuration;
using static Akka.Cluster.MembershipState;

namespace Akka.Cluster.TestKit
{
    /// <summary>
    /// Test-only downing provider that removes unreachable cluster members after a configured delay.
    /// </summary>
    /// <remarks>
    /// Auto-downing is unsafe for production because both sides of a network partition can remove the other side
    /// and form independent clusters. Use this provider only in tests that deliberately exercise failure detection
    /// followed by member removal. Production systems should use a split-brain resolver strategy.
    /// </remarks>
    public sealed class AutoDowning : IDowningProvider
    {
        private const string AutoDownUnreachableAfterPath = "akka.cluster.testkit.auto-down-unreachable-after";

        private readonly Cluster _cluster;
        private readonly TimeSpan? _autoDownUnreachableAfter;

        /// <summary>
        /// Initializes the test-only auto-downing provider.
        /// </summary>
        /// <param name="system">The actor system hosting the cluster.</param>
        /// <param name="cluster">The cluster extension.</param>
        public AutoDowning(ActorSystem system, Cluster cluster)
        {
            _cluster = cluster;
            _autoDownUnreachableAfter = ReadAutoDownUnreachableAfter(system.Settings.Config);
        }

        /// <summary>
        /// Creates configuration that explicitly selects this provider and sets its downing delay.
        /// </summary>
        /// <param name="autoDownUnreachableAfter">
        /// The amount of time a member must remain unreachable before the cluster leader downs it.
        /// </param>
        /// <returns>Configuration that can be composed with the rest of a test's configuration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="autoDownUnreachableAfter"/> is negative.
        /// </exception>
        public static Config GetConfig(TimeSpan autoDownUnreachableAfter)
        {
            if (autoDownUnreachableAfter < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(autoDownUnreachableAfter), "Auto-down delay must be non-negative.");

            var providerType = typeof(AutoDowning).AssemblyQualifiedName
                ?? throw new InvalidOperationException($"Could not determine the assembly-qualified name of {typeof(AutoDowning).FullName}.");
            var delayInMilliseconds = autoDownUnreachableAfter.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);

            return ConfigurationFactory.ParseString($@"
                akka.cluster.downing-provider-class = ""{providerType}""
                {AutoDownUnreachableAfterPath} = {delayInMilliseconds}ms");
        }

        /// <inheritdoc />
        public TimeSpan DownRemovalMargin => _cluster.Settings.DownRemovalMargin;

        /// <inheritdoc />
        public Props? DowningActorProps => _autoDownUnreachableAfter is { } delay
            ? AutoDown.Props(delay, _cluster)
            : null;

        private static TimeSpan? ReadAutoDownUnreachableAfter(Config config)
        {
            if (!config.HasPath(AutoDownUnreachableAfterPath))
                return null;

            var configuredValue = config.GetString(AutoDownUnreachableAfterPath, string.Empty);
            if (IsDisabled(configuredValue))
                return null;

            var delay = config.GetTimeSpan(AutoDownUnreachableAfterPath, null);
            if (delay < TimeSpan.Zero)
                throw new ConfigurationException($"{AutoDownUnreachableAfterPath} must be non-negative or off.");

            return delay;
        }

        private static bool IsDisabled(string value)
        {
            return string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// An unreachable member will be downed by this actor if it remains unreachable for the configured duration
    /// and this actor is running on the leader node in the cluster.
    /// </summary>
    internal sealed class AutoDown : AutoDownBase
    {
        private readonly Cluster _cluster;

        public AutoDown(TimeSpan autoDownUnreachableAfter, Cluster cluster)
            : base(autoDownUnreachableAfter)
        {
            _cluster = cluster;
        }

        internal static Props Props(TimeSpan autoDownUnreachableAfter, Cluster cluster)
        {
            return Actor.Props.Create(() => new AutoDown(autoDownUnreachableAfter, cluster));
        }

        internal sealed class UnreachableTimeout : IEquatable<UnreachableTimeout>
        {
            internal UnreachableTimeout(UniqueAddress node)
            {
                Node = node;
            }

            internal UniqueAddress Node { get; }

            public bool Equals(UnreachableTimeout? other)
            {
                return other is not null && Node.Equals(other.Node);
            }

            public override bool Equals(object? obj)
            {
                return ReferenceEquals(this, obj) || obj is UnreachableTimeout other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Node.GetHashCode();
            }
        }

        public override Address SelfAddress => _cluster.SelfAddress;

        public override IScheduler Scheduler => _cluster.Scheduler;

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IClusterDomainEvent) });
            base.PreStart();
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            base.PostStop();
        }

        public override void Down(Address node)
        {
            if (!_leader)
                throw new InvalidOperationException("Must be leader to down node");

            _cluster.LogInfo("Leader is auto-downing unreachable node [{0}]", node);
            _cluster.Down(node);
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// State machine shared by the test-only auto-downing actor and its unit-test implementation.
    /// </summary>
    internal abstract class AutoDownBase : UntypedActor
    {
        private readonly ImmutableHashSet<MemberStatus> _skipMemberStatus =
            ConvergenceSkipUnreachableWithMemberStatus;
        private readonly TimeSpan _autoDownUnreachableAfter;

        private ImmutableDictionary<UniqueAddress, ICancelable> _scheduledUnreachable =
            ImmutableDictionary<UniqueAddress, ICancelable>.Empty;
        private ImmutableHashSet<UniqueAddress> _pendingUnreachable = ImmutableHashSet<UniqueAddress>.Empty;

        protected bool _leader;

        protected AutoDownBase(TimeSpan autoDownUnreachableAfter)
        {
            _autoDownUnreachableAfter = autoDownUnreachableAfter;
        }

        public abstract Address SelfAddress { get; }

        public abstract IScheduler Scheduler { get; }

        public abstract void Down(Address node);

        protected override void PostStop()
        {
            foreach (var cancelable in _scheduledUnreachable.Values)
                cancelable.Cancel();

            base.PostStop();
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    _leader = state.Leader is not null && state.Leader.Equals(SelfAddress);
                    foreach (var member in state.Unreachable)
                        UnreachableMember(member);
                    return;
                case ClusterEvent.UnreachableMember unreachableMember:
                    UnreachableMember(unreachableMember.Member);
                    return;
                case ClusterEvent.ReachableMember reachableMember:
                    Remove(reachableMember.Member.UniqueAddress);
                    return;
                case ClusterEvent.MemberRemoved memberRemoved:
                    Remove(memberRemoved.Member.UniqueAddress);
                    return;
                case ClusterEvent.LeaderChanged leaderChanged:
                    _leader = leaderChanged.Leader is not null && leaderChanged.Leader.Equals(SelfAddress);
                    if (_leader)
                    {
                        foreach (var node in _pendingUnreachable)
                            Down(node.Address);

                        _pendingUnreachable = ImmutableHashSet<UniqueAddress>.Empty;
                    }
                    return;
                case AutoDown.UnreachableTimeout unreachableTimeout:
                    if (_scheduledUnreachable.ContainsKey(unreachableTimeout.Node))
                    {
                        _scheduledUnreachable = _scheduledUnreachable.Remove(unreachableTimeout.Node);
                        DownOrAddPending(unreachableTimeout.Node);
                    }
                    return;
            }
        }

        private void UnreachableMember(Member member)
        {
            if (!_skipMemberStatus.Contains(member.Status)
                && !_scheduledUnreachable.ContainsKey(member.UniqueAddress))
            {
                ScheduleUnreachable(member.UniqueAddress);
            }
        }

        private void ScheduleUnreachable(UniqueAddress node)
        {
            if (_autoDownUnreachableAfter == TimeSpan.Zero)
            {
                DownOrAddPending(node);
                return;
            }

            var cancelable = Scheduler.ScheduleTellOnceCancelable(
                _autoDownUnreachableAfter,
                Self,
                new AutoDown.UnreachableTimeout(node),
                Self);
            _scheduledUnreachable = _scheduledUnreachable.Add(node, cancelable);
        }

        private void DownOrAddPending(UniqueAddress node)
        {
            if (_leader)
                Down(node.Address);
            else
                _pendingUnreachable = _pendingUnreachable.Add(node);
        }

        private void Remove(UniqueAddress node)
        {
            if (_scheduledUnreachable.TryGetValue(node, out var cancelable))
                cancelable.Cancel();

            _scheduledUnreachable = _scheduledUnreachable.Remove(node);
            _pendingUnreachable = _pendingUnreachable.Remove(node);
        }
    }
}
