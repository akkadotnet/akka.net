//-----------------------------------------------------------------------
// <copyright file="ClusterSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Cluster
{
    /// <summary>
    /// This class represents configuration information used when setting up a cluster.
    /// </summary>
    public sealed class ClusterSettings
    {
        internal const string DcRolePrefix = "dc-";
        internal const string DefaultDataCenter = "default";

        readonly Config _failureDetectorConfig;
        readonly string _useDispatcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration to use when setting up the cluster.</param>
        /// <param name="systemName">The name of the actor system hosting the cluster.</param>
        public ClusterSettings(Config config, string systemName)
        {
            //TODO: Requiring!
            var cc = config.GetConfig("akka.cluster");
            LogInfo = cc.GetBoolean("log-info");
            _failureDetectorConfig = cc.GetConfig("failure-detector");
            FailureDetectorImplementationClass = _failureDetectorConfig.GetString("implementation-class");
            HeartbeatInterval = _failureDetectorConfig.GetTimeSpan("heartbeat-interval");
            HeartbeatExpectedResponseAfter = _failureDetectorConfig.GetTimeSpan("expected-response-after");
            MonitoredByNrOfMembers = _failureDetectorConfig.GetInt("monitored-by-nr-of-members");

            SeedNodes = cc.GetStringList("seed-nodes").Select(Address.Parse).ToImmutableList();
            SeedNodeTimeout = cc.GetTimeSpan("seed-node-timeout");
            RetryUnsuccessfulJoinAfter = cc.GetTimeSpanWithOffSwitch("retry-unsuccessful-join-after");
            PeriodicTasksInitialDelay = cc.GetTimeSpan("periodic-tasks-initial-delay");
            GossipInterval = cc.GetTimeSpan("gossip-interval");
            GossipTimeToLive = cc.GetTimeSpan("gossip-time-to-live");
            LeaderActionsInterval = cc.GetTimeSpan("leader-actions-interval");
            UnreachableNodesReaperInterval = cc.GetTimeSpan("unreachable-nodes-reaper-interval");
            PublishStatsInterval = cc.GetTimeSpanWithOffSwitch("publish-stats-interval");

            var key = "down-removal-margin";
            DownRemovalMargin = cc.GetString(key).ToLowerInvariant().Equals("off") 
                ? TimeSpan.Zero
                : cc.GetTimeSpan("down-removal-margin");

            AutoDownUnreachableAfter = cc.GetTimeSpanWithOffSwitch("auto-down-unreachable-after");

            Roles = cc.GetStringList("roles").ToImmutableHashSet();
            MinNrOfMembers = cc.GetInt("min-nr-of-members");
            //TODO:
            //_minNrOfMembersOfRole = cc.GetConfig("role").Root.GetArray().ToImmutableDictionary(o => o. )
            _useDispatcher = cc.GetString("use-dispatcher");
            if (String.IsNullOrEmpty(_useDispatcher)) _useDispatcher = Dispatchers.DefaultDispatcherId;
            GossipDifferentViewProbability = cc.GetDouble("gossip-different-view-probability");
            ReduceGossipDifferentViewProbability = cc.GetInt("reduce-gossip-different-view-probability");
            SchedulerTickDuration = cc.GetTimeSpan("scheduler.tick-duration");
            SchedulerTicksPerWheel = cc.GetInt("scheduler.ticks-per-wheel");

            MinNrOfMembersOfRole = cc.GetConfig("role").Root.GetObject().Items
                .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.GetObject().GetKey("min-nr-of-members").GetInt());

            VerboseHeartbeatLogging = cc.GetBoolean("debug.verbose-heartbeat-logging");
            VerboseGossipReceivedLogging = cc.GetBoolean("debug.verbose-receive-gossip-logging");

            var downingProviderClassName = cc.GetString("downing-provider-class");
            if (!string.IsNullOrEmpty(downingProviderClassName))
                DowningProviderType = Type.GetType(downingProviderClassName, true);
            else if (AutoDownUnreachableAfter.HasValue)
                DowningProviderType = typeof(AutoDowning);
            else
                DowningProviderType = typeof(NoDowning);

            RunCoordinatedShutdownWhenDown = cc.GetBoolean("run-coordinated-shutdown-when-down");
            AllowWeaklyUpMembers = cc.GetBoolean("allow-weakly-up-members");

            SelfDataCenter = cc.GetString("multi-data-center.self-data-center");
            MultiDataCenter = new MultiDataCenterSettings(cc.GetConfig("multi-data-center"));
            PruneGossipTombstonesAfter = cc.GetTimeSpan("prune-gossip-tombstones-after");
            
            if (PruneGossipTombstonesAfter == TimeSpan.Zero) throw new ArgumentException("`prune-gossip-tombstones-after` must be greater than zero");
            
            Debug = new DebugSettings(
                verboseHeartbeatLogging: cc.GetBoolean("debug.verbose-heartbeat-logging"),
                verboseGossipLogging: cc.GetBoolean("debug.verbose-gossip-logging"));

            if (config.GetString("shutdown-after-unsuccessful-join-seed-nodes").ToLowerInvariant() == "off")
                ShutdownAfterUnsuccessfulJoinSeedNodes = null;
            else
            {
                var t = config.GetTimeSpan("shutdown-after-unsuccessful-join-seed-nodes");
                if (t == TimeSpan.Zero) throw new ArgumentException("`shutdown-after-unsuccessful-join-seed-nodes` must be greater than zero");
                
                ShutdownAfterUnsuccessfulJoinSeedNodes = t;
            }
        }

        /// <summary>
        /// Determine whether to log <see cref="Akka.Event.LogLevel.InfoLevel"/> messages.
        /// </summary>
        public bool LogInfo { get; }

        /// <summary>
        /// The configuration for the underlying failure detector used by Akka.Cluster.
        /// </summary>
        public Config FailureDetectorConfig => _failureDetectorConfig;

        /// <summary>
        /// The fully qualified type name of the failure detector class that will be used.
        /// </summary>
        public string FailureDetectorImplementationClass { get; }

        /// <summary>
        /// The amount of time between when heartbeat messages are sent.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; }

        /// <summary>
        /// The amount of time we expect a heartbeat response after first contact with a new node.
        /// </summary>
        public TimeSpan HeartbeatExpectedResponseAfter { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MonitoredByNrOfMembers { get; }

        /// <summary>
        /// A list of designated seed nodes for the cluster.
        /// </summary>
        public ImmutableList<Address> SeedNodes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SeedNodeTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? RetryUnsuccessfulJoinAfter { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan PeriodicTasksInitialDelay { get; }

        /// <summary>
        /// The amount of time between when gossip messages are sent.
        /// </summary>
        public TimeSpan GossipInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan GossipTimeToLive { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan LeaderActionsInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan UnreachableNodesReaperInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? PublishStatsInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? AutoDownUnreachableAfter { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<string> Roles { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public double GossipDifferentViewProbability { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int ReduceGossipDifferentViewProbability { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public string UseDispatcher => _useDispatcher;

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SchedulerTickDuration { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int SchedulerTicksPerWheel { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MinNrOfMembers { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableDictionary<string, int> MinNrOfMembersOfRole { get; }

        /// <summary>
        /// Obsolete. Use <see cref="P:Cluster.DowningProvider.DownRemovalMargin"/>.
        /// </summary>
        [Obsolete("Use Cluster.DowningProvider.DownRemovalMargin [1.1.2]")]
        public TimeSpan DownRemovalMargin { get; }

        /// <summary>
        /// Determine whether or not to log heartbeat message in verbose mode.
        /// </summary>
        public bool VerboseHeartbeatLogging { get; }

        /// <summary>
        /// Determines whether or not to log gossip consumption logging in verbose mode
        /// </summary>
        public bool VerboseGossipReceivedLogging { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Type DowningProviderType { get; }

        /// <summary>
        /// Trigger the <see cref="CoordinatedShutdown"/> even if this node was removed by non-graceful
        /// means, such as being downed.
        /// </summary>
        public bool RunCoordinatedShutdownWhenDown { get; }

        /// <summary>
        /// If this is set to "off", the leader will not move <see cref="MemberStatus.Joining"/> members to <see cref="MemberStatus.Up"/> during a network
        /// split. This feature allows the leader to accept <see cref="MemberStatus.Joining"/> members to be <see cref="MemberStatus.WeaklyUp"/>
        /// so they become part of the cluster even during a network split. The leader will
        /// move <see cref="MemberStatus.Joining"/> members to <see cref="MemberStatus.WeaklyUp"/> after 3 rounds of 'leader-actions-interval'
        /// without convergence.
        /// The leader will move <see cref="MemberStatus.WeaklyUp"/> members to <see cref="MemberStatus.Up"/> status once convergence has been reached.
        /// </summary>
        public bool AllowWeaklyUpMembers { get; }

        public string SelfDataCenter { get; }

        public MultiDataCenterSettings MultiDataCenter { get; }
        public DebugSettings Debug { get; }
        public TimeSpan? ShutdownAfterUnsuccessfulJoinSeedNodes { get; }
        public TimeSpan PruneGossipTombstonesAfter { get; set; }
    }

    public sealed class MultiDataCenterSettings
    {
        public int CrossDcConnections { get; }
        public double CrossDcGossipProbability { get; }
        public CrossDcFailureDetectorSettings CrossDcFailureDetectorSettings { get; }

        public MultiDataCenterSettings(int crossDcConnections, double crossDcGossipProbability, CrossDcFailureDetectorSettings crossDcFailureDetectorSettings)
        {
            if (crossDcConnections <= 0) throw new ArgumentException("cross-data-center-connections must be > 0", nameof(crossDcConnections));
            if (crossDcGossipProbability < 0 || crossDcGossipProbability > 1) throw new ArgumentException("cross-data-center-gossip-probability must be >= 0.0 and <= 1.0", nameof(crossDcGossipProbability));

            CrossDcConnections = crossDcConnections;
            CrossDcGossipProbability = crossDcGossipProbability;
            CrossDcFailureDetectorSettings = crossDcFailureDetectorSettings;
        }

        public MultiDataCenterSettings(Config config) : this(
            crossDcConnections: config.GetInt("cross-data-center-connections"),
            crossDcGossipProbability: config.GetInt("cross-data-center-gossip-probability"),
            crossDcFailureDetectorSettings: new CrossDcFailureDetectorSettings(config.GetConfig("failure-detector"), config.GetInt("cross-data-center-connections")))
        {
        }
    }

    public sealed class DebugSettings
    {
        public bool VerboseHeartbeatLogging { get; }
        public bool VerboseGossipLogging { get; }

        public DebugSettings(bool verboseHeartbeatLogging, bool verboseGossipLogging)
        {
            VerboseHeartbeatLogging = verboseHeartbeatLogging;
            VerboseGossipLogging = verboseGossipLogging;
        }
    }

    public sealed class CrossDcFailureDetectorSettings
    {
        public Config Config { get; }
        public string ImplementationClass { get; }
        public TimeSpan HeartbeatInterval { get; }
        public TimeSpan HeartbeatExpectedResponseAfter { get; }
        public int NrOfMonitoringActors { get; }

        public CrossDcFailureDetectorSettings(Config config, int nrOfMonitoringActors)
        {
            Config = config;
            ImplementationClass = config.GetString("failure-detector.implementation-class");
            HeartbeatInterval = config.GetTimeSpan("failure-detector.heartbeat-interval");
            HeartbeatExpectedResponseAfter = config.GetTimeSpan("failure-detector.expected-response-after");
            NrOfMonitoringActors = nrOfMonitoringActors;
            
            if (HeartbeatInterval == TimeSpan.Zero) throw new ArgumentException("failure-detector.heartbeat-interval must be > 0", nameof(config));
            if (HeartbeatExpectedResponseAfter == TimeSpan.Zero) throw new ArgumentException("failure-detector.expected-response-after > 0", nameof(config));
        }
    }
}

