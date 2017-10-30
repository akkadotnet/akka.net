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
        public Props DowningActorProps => SplitBrainStrategy.Props(_splitBrainResolverConfig);
    }

    internal abstract class SplitBrainStrategy : UntypedActor
    {
        #region internal classes

        protected sealed class StabilityReached
        {
            public static readonly StabilityReached Instance = new StabilityReached();
            private StabilityReached() { }
        }

        #endregion

        public static Actor.Props Props(Config config)
        {
            var stabilityTimeout = config.GetTimeSpan("stable-after");
            var activeStrategy = config.GetString("active-strategy");
            switch (activeStrategy)
            {
                case "static-quorum": return Actor.Props.Create(() => new StaticQuorum(config.GetConfig("static-quorum"), stabilityTimeout)).WithDeploy(Deploy.Local);
                case "keep-majority": return Actor.Props.Create(() => new KeepMajority(config.GetConfig("keep-majority"), stabilityTimeout)).WithDeploy(Deploy.Local);
                case "keep-oldest": return Actor.Props.Create(() => new KeepOldest(config.GetConfig("keep-oldest"), stabilityTimeout)).WithDeploy(Deploy.Local);
                case "keep-referee": return Actor.Props.Create(() => new KeepReferee(config.GetConfig("keep-referee"), stabilityTimeout)).WithDeploy(Deploy.Local);
                default: throw new ConfigurationException($"Unrecognized value [{activeStrategy}] of `akka.cluster.split-brain-resolver.active-strategy`. Supported options are: static-quorum | keep-majority | keep-oldest | keep-referee.");
            }
        }

        protected readonly Cluster Cluster;
        protected readonly TimeSpan StabilityTimeout;

        private ImmutableSortedSet<Member> _reachable = ImmutableSortedSet<Member>.Empty;
        private ImmutableSortedSet<Member> _unreachable = ImmutableSortedSet<Member>.Empty;
        private ICancelable _stabilityTask;
        private ILoggingAdapter _log;

        protected SplitBrainStrategy(TimeSpan stabilityTimeout)
        {
            StabilityTimeout = stabilityTimeout;
            Cluster = Cluster.Get(Context.System);
        }

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected abstract string Role { get; }
        protected abstract void ApplyStrategy(ImmutableSortedSet<Member> reachable, ImmutableSortedSet<Member> unreachable);

        protected override void PreStart()
        {
            base.PreStart();
            Cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent));
        }

        protected override void PostStop()
        {
            Cluster.Unsubscribe(Self);
            base.PostStop();
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    ResetStabilityTimeout();
                    _reachable = state.Members.Where(m => m.Status == MemberStatus.Up && HasRole(m)).ToImmutableSortedSet(Member.AgeOrdering);
                    _unreachable = state.Unreachable.Where(HasRole).ToImmutableSortedSet(Member.AgeOrdering);
                    return;
                case ClusterEvent.IMemberEvent memberEvent:
                    ResetStabilityTimeout();
                    switch (memberEvent)
                    {
                        case ClusterEvent.MemberUp up when HasRole(up.Member):
                            _reachable = _reachable.Add(up.Member);
                            break;
                        case ClusterEvent.MemberRemoved removed when HasRole(removed.Member):
                            _reachable = _reachable.Remove(removed.Member);
                            _unreachable = _unreachable.Remove(removed.Member);
                            break;
                    }
                    return;
                case ClusterEvent.IReachabilityEvent reachabilityEvent:
                    ResetStabilityTimeout();
                    switch (reachabilityEvent)
                    {
                        case ClusterEvent.ReachableMember reachable when HasRole(reachable.Member):
                            _reachable = _reachable.Add(reachable.Member);
                            _unreachable = _unreachable.Remove(reachable.Member);
                            break;
                        case ClusterEvent.UnreachableMember unreachable when HasRole(unreachable.Member):
                            _reachable = _reachable.Remove(unreachable.Member);
                            _unreachable = _unreachable.Add(unreachable.Member);
                            break;
                    }
                    return;
                case StabilityReached _:
                    ApplyStrategy(_reachable, _unreachable);
                    return;
            }
        }

        private void ResetStabilityTimeout()
        {
            _stabilityTask?.Cancel();
            _stabilityTask = Context.System.Scheduler.ScheduleTellOnceCancelable(StabilityTimeout, Self, StabilityReached.Instance, ActorRefs.NoSender);
        }

        private bool HasRole(Member member) => string.IsNullOrEmpty(Role) || member.HasRole(Role);
    }

    internal sealed class StaticQuorum : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    quorumSize: config.GetInt("quorum-size"),
                    role: config.GetString("role", string.Empty));
            }

            public Settings(int quorumSize, string role)
            {
                QuorumSize = quorumSize;
                Role = role;
            }

            /// <summary>
            /// Minimum number of nodes that the cluster must have.
            /// </summary>
            public int QuorumSize { get; }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role'.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        private readonly Settings _settings;
        protected override string Role => _settings.Role;

        public StaticQuorum(Settings settings, TimeSpan stabilityTimeout) : base(stabilityTimeout)
        {
            _settings = settings;
        }

        public StaticQuorum(Config config, TimeSpan stabilityTimeout) : this(Settings.Create(config), stabilityTimeout)
        {
        }

        protected override void ApplyStrategy(ImmutableSortedSet<Member> reachable, ImmutableSortedSet<Member> unreachable)
        {
            if (reachable.Count < _settings.QuorumSize)
            {
                Log.Info("Static quorum failed - only {0} of min. {1} of expected nodes were reachable. Shutting down cluster node.", reachable.Count, _settings.QuorumSize);
                Cluster.Down(Cluster.SelfAddress);
            }
        }
    }

    internal sealed class KeepMajority : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    role: config.GetString("role", string.Empty));
            }

            public Settings(string role)
            {
                Role = role;
            }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role'.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        private readonly Settings _settings;
        protected override string Role => _settings.Role;

        public KeepMajority(Settings settings, TimeSpan stabilityTimeout) : base(stabilityTimeout)
        {
            _settings = settings;
        }

        public KeepMajority(Config config, TimeSpan stabilityTimeout) : this(Settings.Create(config), stabilityTimeout)
        {
        }

        protected override void ApplyStrategy(ImmutableSortedSet<Member> reachable, ImmutableSortedSet<Member> unreachable)
        {
            if (reachable.Count < unreachable.Count)
            {
                Log.Info("Failed to reach majority of the nodes. Reachable members: {0}. Unreachable members: {1}. Shutting down cluster node.", reachable.Count, unreachable.Count);
                Cluster.Down(Cluster.SelfAddress);
            }
        }
    }

    internal sealed class KeepOldest : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    downIfAlone: config.GetBoolean("down-if-alone", true),
                    role: config.GetString("role", string.Empty));
            }

            public Settings(bool downIfAlone, string role)
            {
                DownIfAlone = downIfAlone;
                Role = role;
            }

            /// <summary>
            /// Enable downing of the oldest node when it is partitioned from all other nodes.
            /// </summary>
            public bool DownIfAlone { get; }

            /// <summary>
            /// If the 'role' is defined the decision is based only on members with that 'role',
            /// i.e. using the oldest member (singleton) within the nodes with that role.
            /// </summary>
            public string Role { get; }
        }

        #endregion

        private readonly Settings _settings;
        protected override string Role => _settings.Role;

        public KeepOldest(Settings settings, TimeSpan stabilityTimeout) : base(stabilityTimeout)
        {
            _settings = settings;
        }

        public KeepOldest(Config config, TimeSpan stabilityTimeout) : this(Settings.Create(config), stabilityTimeout)
        {
        }

        protected override void ApplyStrategy(ImmutableSortedSet<Member> reachable, ImmutableSortedSet<Member> unreachable)
        {
            var oldestReachable = reachable.FirstOrDefault();
            var oldestUnreachable = unreachable.FirstOrDefault();

            if (oldestReachable == null) Down(Cluster.SelfAddress);
            else if (oldestUnreachable != null && oldestUnreachable.IsOlderThan(oldestReachable))
            {
                if (_settings.DownIfAlone && unreachable.Count == 1)
                {
                    // the oldest node is the only one unreachable
                    Down(oldestUnreachable.Address);
                    return;
                }
                
                Down(Cluster.SelfAddress);
            }
        }

        private void Down(Address address)
        {
            Log.Info("Oldest node was not reached, shutting down {0}", address);
            Cluster.Down(address);
        }
    }

    internal sealed class KeepReferee : SplitBrainStrategy
    {
        #region internal classes

        public sealed class Settings
        {
            public static Settings Create(Config config)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));

                return new Settings(
                    refereeAddress: Address.Parse(config.GetString("address")), 
                    downAllIfLessThanNodes: config.GetInt("down-all-if-less-than-nodes", 1));
            }

            public Settings(Address refereeAddress, int downAllIfLessThanNodes)
            {
                ReferreeAddress = refereeAddress;
                DownAllIfLessThanNodes = downAllIfLessThanNodes;
            }

            public Address ReferreeAddress { get; }
            public int DownAllIfLessThanNodes { get; }
        }

        #endregion

        private readonly Settings _settings;
        protected override string Role => null;

        public KeepReferee(Settings settings, TimeSpan stabilityTimeout) : base(stabilityTimeout)
        {
            _settings = settings;
        }

        public KeepReferee(Config config, TimeSpan stabilityTimeout) : this(Settings.Create(config), stabilityTimeout)
        {
        }

        protected override void ApplyStrategy(ImmutableSortedSet<Member> reachable, ImmutableSortedSet<Member> unreachable)
        {
            var refereeReachable = reachable.Select(m => m.Address).Contains(_settings.ReferreeAddress);

            if (!refereeReachable || reachable.Count < _settings.DownAllIfLessThanNodes)
            {
                Log.Info("A referee node address {0} was not reached or a reachable number of nodes ({1}) didn't pass required threshold ({2}). Shutting down cluster node.", _settings.ReferreeAddress, reachable.Count, _settings.DownAllIfLessThanNodes);
                Cluster.Down(Cluster.SelfAddress);
            }
        }
    }
}