//-----------------------------------------------------------------------
// <copyright file="ClusterSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// TBD
    /// </summary>
    public sealed class ClusterSettings
    {
        readonly Config _failureDetectorConfig;
        readonly string _useDispatcher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="systemName">TBD</param>
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

            var downingProviderClassName = cc.GetString("downing-provider-class");
            if (!string.IsNullOrEmpty(downingProviderClassName))
                DowningProviderType = Type.GetType(downingProviderClassName, true);
            else if (AutoDownUnreachableAfter.HasValue)
                DowningProviderType = typeof(AutoDowning);
            else
                DowningProviderType = typeof(NoDowning);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool LogInfo { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Config FailureDetectorConfig => _failureDetectorConfig;

        /// <summary>
        /// TBD
        /// </summary>
        public string FailureDetectorImplementationClass { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan HeartbeatInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan HeartbeatExpectedResponseAfter { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MonitoredByNrOfMembers { get; }

        /// <summary>
        /// TBD
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
        /// TBD
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
        /// TBD
        /// </summary>
        [Obsolete("Use Cluster.DowningProvider.DownRemovalMargin")]
        public TimeSpan DownRemovalMargin { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool VerboseHeartbeatLogging { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Type DowningProviderType { get; }
    }
}

