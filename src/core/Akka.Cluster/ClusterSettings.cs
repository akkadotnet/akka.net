//-----------------------------------------------------------------------
// <copyright file="ClusterSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public sealed class ClusterSettings
    {
        readonly bool _logInfo;
        readonly Config _failureDetectorConfig;
        readonly string _failureDetectorImplementationClass;
        readonly TimeSpan _heartbeatInterval;
        readonly TimeSpan _heartbeatExpectedResponseAfter;
        readonly int _monitoredByNrOfMembers;
        readonly ImmutableList<Address> _seedNodes;
        readonly TimeSpan _seedNodeTimeout;
        readonly TimeSpan? _retryUnsuccessfulJoinAfter;
        readonly TimeSpan _periodicTasksInitialDelay;
        readonly TimeSpan _gossipInterval;
        readonly TimeSpan _gossipTimeToLive;
        readonly TimeSpan _leaderActionsInterval;
        readonly TimeSpan _unreachableNodesReaperInterval;
        readonly TimeSpan? _publishStatsInterval;
        readonly TimeSpan? _autoDownUnreachableAfter;
        readonly ImmutableHashSet<string> _roles;
        readonly string _useDispatcher;
        readonly double _gossipDifferentViewProbability;
        readonly int _reduceGossipDifferentViewProbability;
        readonly TimeSpan _schedulerTickDuration;
        readonly int _schedulerTicksPerWheel;
        readonly bool _metricsEnabled;
        readonly string _metricsCollectorClass;
        readonly TimeSpan _metricsInterval;
        readonly TimeSpan _metricsGossipInterval;
        readonly TimeSpan _metricsMovingAverageHalfLife;
        readonly int _minNrOfMembers;
        readonly ImmutableDictionary<string, int> _minNrOfMembersOfRole;
        readonly TimeSpan _downRemovalMargin;

        public ClusterSettings(Config config, string systemName)
        {
            //TODO: Requiring!
            var cc = config.GetConfig("akka.cluster");
            _logInfo = cc.GetBoolean("log-info");
            _failureDetectorConfig = cc.GetConfig("failure-detector");
            _failureDetectorImplementationClass = _failureDetectorConfig.GetString("implementation-class");
            _heartbeatInterval = _failureDetectorConfig.GetTimeSpan("heartbeat-interval");
            _heartbeatExpectedResponseAfter = _failureDetectorConfig.GetTimeSpan("expected-response-after");
            _monitoredByNrOfMembers = _failureDetectorConfig.GetInt("monitored-by-nr-of-members");

            _seedNodes = cc.GetStringList("seed-nodes").Select(Address.Parse).ToImmutableList();
            _seedNodeTimeout = cc.GetTimeSpan("seed-node-timeout");
            _retryUnsuccessfulJoinAfter = cc.GetTimeSpanWithOffSwitch("retry-unsuccessful-join-after");
            _periodicTasksInitialDelay = cc.GetTimeSpan("periodic-tasks-initial-delay");
            _gossipInterval = cc.GetTimeSpan("gossip-interval");
            _gossipTimeToLive = cc.GetTimeSpan("gossip-time-to-live");
            _leaderActionsInterval = cc.GetTimeSpan("leader-actions-interval");
            _unreachableNodesReaperInterval = cc.GetTimeSpan("unreachable-nodes-reaper-interval");
            _publishStatsInterval = cc.GetTimeSpanWithOffSwitch("publish-stats-interval");
            _downRemovalMargin = cc.GetTimeSpan("down-removal-margin");

            _autoDownUnreachableAfter = cc.GetTimeSpanWithOffSwitch("auto-down-unreachable-after");

            _roles = cc.GetStringList("roles").ToImmutableHashSet();
            _minNrOfMembers = cc.GetInt("min-nr-of-members");
            //TODO:
            //_minNrOfMembersOfRole = cc.GetConfig("role").Root.GetArray().ToImmutableDictionary(o => o. )
            //TODO: Ignored jmx
            _useDispatcher = cc.GetString("use-dispatcher");
            if (String.IsNullOrEmpty(_useDispatcher)) _useDispatcher = Dispatchers.DefaultDispatcherId;
            _gossipDifferentViewProbability = cc.GetDouble("gossip-different-view-probability");
            _reduceGossipDifferentViewProbability = cc.GetInt("reduce-gossip-different-view-probability");
            _schedulerTickDuration = cc.GetTimeSpan("scheduler.tick-duration");
            _schedulerTicksPerWheel = cc.GetInt("scheduler.ticks-per-wheel");
            _metricsEnabled = cc.GetBoolean("metrics.enabled");
            _metricsCollectorClass = cc.GetString("metrics.collector-class");
            _metricsInterval = cc.GetTimeSpan("metrics.collect-interval");
            _metricsGossipInterval = cc.GetTimeSpan("metrics.gossip-interval");
            _metricsMovingAverageHalfLife = cc.GetTimeSpan("metrics.moving-average-half-life");

            _minNrOfMembersOfRole = cc.GetConfig("role").Root.GetObject().Items
                .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.GetObject().GetKey("min-nr-of-members").GetInt());
        }

        public bool LogInfo
        {
            get { return _logInfo; }
        }

        public Config FailureDetectorConfig
        {
            get { return _failureDetectorConfig; }
        }

        public string FailureDetectorImplementationClass
        {
            get { return _failureDetectorImplementationClass; }
        }

        public TimeSpan HeartbeatInterval
        {
            get { return _heartbeatInterval; }
        }

        public TimeSpan HeartbeatExpectedResponseAfter
        {
            get { return _heartbeatExpectedResponseAfter; }
        }

        public int MonitoredByNrOfMembers
        {
            get { return _monitoredByNrOfMembers; }
        }

        public ImmutableList<Address> SeedNodes
        {
            get { return _seedNodes; }
        }

        public TimeSpan SeedNodeTimeout
        {
            get { return _seedNodeTimeout; }
        }

        public TimeSpan? RetryUnsuccessfulJoinAfter
        {
            get { return _retryUnsuccessfulJoinAfter; }
        }

        public TimeSpan PeriodicTasksInitialDelay
        {
            get { return _periodicTasksInitialDelay; }
        }

        public TimeSpan GossipInterval
        {
            get { return _gossipInterval; }
        }

        public TimeSpan GossipTimeToLive
        {
            get { return _gossipTimeToLive; }
        }

        public TimeSpan LeaderActionsInterval
        {
            get { return _leaderActionsInterval; }
        }

        public TimeSpan UnreachableNodesReaperInterval
        {
            get { return _unreachableNodesReaperInterval; }
        }

        public TimeSpan? PublishStatsInterval
        {
            get { return _publishStatsInterval; }
        }

        public TimeSpan? AutoDownUnreachableAfter
        {
            get { return _autoDownUnreachableAfter; }
        }

        public ImmutableHashSet<string> Roles
        {
            get { return _roles; }
        }

        public double GossipDifferentViewProbability
        {
            get { return _gossipDifferentViewProbability; }
        }

        public int ReduceGossipDifferentViewProbability
        {
            get { return _reduceGossipDifferentViewProbability; }
        }

        public string UseDispatcher
        {
            get { return _useDispatcher; }
        }

        public TimeSpan SchedulerTickDuration
        {
            get { return _schedulerTickDuration; }
        }

        public int SchedulerTicksPerWheel
        {
            get { return _schedulerTicksPerWheel; }
        }

        public bool MetricsEnabled
        {
            get { return _metricsEnabled; }
        }

        public string MetricsCollectorClass
        {
            get { return _metricsCollectorClass; }
        }

        public TimeSpan MetricsInterval
        {
            get { return _metricsInterval; }
        }

        public TimeSpan MetricsGossipInterval
        {
            get { return _metricsGossipInterval; }
        }

        public TimeSpan MetricsMovingAverageHalfLife
        {
            get { return _metricsMovingAverageHalfLife; }
        }

        public int MinNrOfMembers
        {
            get { return _minNrOfMembers; }
        }

        public ImmutableDictionary<string, int> MinNrOfMembersOfRole
        {
            get { return _minNrOfMembersOfRole; }
        }

        public TimeSpan DownRemovalMargin
        {
            get { return _downRemovalMargin; }
        }
    }
}

