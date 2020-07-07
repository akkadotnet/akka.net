//-----------------------------------------------------------------------
// <copyright file="ClusterSettings.cs" company="Akka.NET Project">
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
using Akka.Dispatch;

namespace Akka.Cluster
{
    /// <summary>
    /// This class represents configuration information used when setting up a cluster.
    /// </summary>
    public sealed class ClusterSettings
    {
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
            var clusterConfig = config.GetConfig("akka.cluster");
            if (clusterConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterSettings>("akka.cluster");

            LogInfoVerbose = clusterConfig.GetBoolean("log-info-verbose", false);
            LogInfo = LogInfoVerbose || clusterConfig.GetBoolean("log-info", false);
            _failureDetectorConfig = clusterConfig.GetConfig("failure-detector");
            FailureDetectorImplementationClass = _failureDetectorConfig.GetString("implementation-class", null);
            HeartbeatInterval = _failureDetectorConfig.GetTimeSpan("heartbeat-interval", null);
            HeartbeatExpectedResponseAfter = _failureDetectorConfig.GetTimeSpan("expected-response-after", null);
            MonitoredByNrOfMembers = _failureDetectorConfig.GetInt("monitored-by-nr-of-members", 0);

            SeedNodes = clusterConfig.GetStringList("seed-nodes", new string[] { }).Select(Address.Parse).ToImmutableList();
            SeedNodeTimeout = clusterConfig.GetTimeSpan("seed-node-timeout", null);
            RetryUnsuccessfulJoinAfter = clusterConfig.GetTimeSpanWithOffSwitch("retry-unsuccessful-join-after");
            ShutdownAfterUnsuccessfulJoinSeedNodes = clusterConfig.GetTimeSpanWithOffSwitch("shutdown-after-unsuccessful-join-seed-nodes");
            PeriodicTasksInitialDelay = clusterConfig.GetTimeSpan("periodic-tasks-initial-delay", null);
            GossipInterval = clusterConfig.GetTimeSpan("gossip-interval", null);
            GossipTimeToLive = clusterConfig.GetTimeSpan("gossip-time-to-live", null);
            LeaderActionsInterval = clusterConfig.GetTimeSpan("leader-actions-interval", null);
            UnreachableNodesReaperInterval = clusterConfig.GetTimeSpan("unreachable-nodes-reaper-interval", null);
            PublishStatsInterval = clusterConfig.GetTimeSpanWithOffSwitch("publish-stats-interval");

            var key = "down-removal-margin";
            var useDownRemoval = clusterConfig.GetString(key, "");
            DownRemovalMargin = 
                (
                    useDownRemoval.ToLowerInvariant().Equals("off") || 
                    useDownRemoval.ToLowerInvariant().Equals("false") || 
                    useDownRemoval.ToLowerInvariant().Equals("no")
                ) ? TimeSpan.Zero : 
                clusterConfig.GetTimeSpan("down-removal-margin", null);

            AutoDownUnreachableAfter = clusterConfig.GetTimeSpanWithOffSwitch("auto-down-unreachable-after");

            Roles = clusterConfig.GetStringList("roles", new string[] { }).ToImmutableHashSet();
            MinNrOfMembers = clusterConfig.GetInt("min-nr-of-members", 0);

            _useDispatcher = clusterConfig.GetString("use-dispatcher", null);
            if (String.IsNullOrEmpty(_useDispatcher)) _useDispatcher = Dispatchers.DefaultDispatcherId;
            GossipDifferentViewProbability = clusterConfig.GetDouble("gossip-different-view-probability", 0);
            ReduceGossipDifferentViewProbability = clusterConfig.GetInt("reduce-gossip-different-view-probability", 0);
            SchedulerTickDuration = clusterConfig.GetTimeSpan("scheduler.tick-duration", null);
            SchedulerTicksPerWheel = clusterConfig.GetInt("scheduler.ticks-per-wheel", 0);

            MinNrOfMembersOfRole = clusterConfig.GetConfig("role").Root.GetObject().Items
                .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.GetObject().GetKey("min-nr-of-members").GetInt());

            VerboseHeartbeatLogging = clusterConfig.GetBoolean("debug.verbose-heartbeat-logging", false);
            VerboseGossipReceivedLogging = clusterConfig.GetBoolean("debug.verbose-receive-gossip-logging", false);

            var downingProviderClassName = clusterConfig.GetString("downing-provider-class", null);
            if (!string.IsNullOrEmpty(downingProviderClassName))
                DowningProviderType = Type.GetType(downingProviderClassName, true);
            else if (AutoDownUnreachableAfter.HasValue)
                DowningProviderType = typeof(AutoDowning);
            else
                DowningProviderType = typeof(NoDowning);

            RunCoordinatedShutdownWhenDown = clusterConfig.GetBoolean("run-coordinated-shutdown-when-down", false);
            AllowWeaklyUpMembers = clusterConfig.GetBoolean("allow-weakly-up-members", false);
        }

        /// <summary>
        /// Determine whether to log verbose <see cref="Akka.Event.LogLevel.InfoLevel"/> messages for temporary troubleshooting.
        /// </summary>
        public bool LogInfoVerbose { get; }

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
        public TimeSpan? ShutdownAfterUnsuccessfulJoinSeedNodes { get; }

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
    }
}

