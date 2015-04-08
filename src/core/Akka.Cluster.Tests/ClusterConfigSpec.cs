//-----------------------------------------------------------------------
// <copyright file="ClusterConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Remote;
using Akka.TestKit;
using Xunit;
using Assert = Xunit.Assert;

namespace Akka.Cluster.Tests
{
    public class ClusterConfigSpec : AkkaSpec
    {
        public ClusterConfigSpec() : base(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""") { }

        [Fact]
        public void ClusteringMustBeAbleToParseGenericClusterConfigElements()
        {
            var settings = new ClusterSettings(Sys.Settings.Config, Sys.Name);
            Assert.True(settings.LogInfo);
            Assert.Equal(8, settings.FailureDetectorConfig.GetDouble("threshold"));
            Assert.Equal(1000, settings.FailureDetectorConfig.GetInt("max-sample-size"));
            Assert.Equal(TimeSpan.FromMilliseconds(100), settings.FailureDetectorConfig.GetTimeSpan("min-std-deviation"));
            Assert.Equal(TimeSpan.FromSeconds(3), settings.FailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause"));
            Assert.Equal(typeof(PhiAccrualFailureDetector), Type.GetType(settings.FailureDetectorImplementationClass));
            Assert.Equal(ImmutableList.Create<Address>(), settings.SeedNodes);
            Assert.Equal(TimeSpan.FromSeconds(5), settings.SeedNodeTimeout);
            Assert.Equal(TimeSpan.FromSeconds(10), settings.RetryUnsuccessfulJoinAfter);
            Assert.Equal(TimeSpan.FromSeconds(1), settings.PeriodicTasksInitialDelay);
            Assert.Equal(TimeSpan.FromSeconds(1), settings.GossipInterval);
            Assert.Equal(TimeSpan.FromSeconds(2), settings.GossipTimeToLive);
            Assert.Equal(TimeSpan.FromSeconds(1), settings.HeartbeatInterval);
            Assert.Equal(5, settings.MonitoredByNrOfMembers);
            Assert.Equal(TimeSpan.FromSeconds(5), settings.HeartbeatExpectedResponseAfter);
            Assert.Equal(TimeSpan.FromSeconds(1), settings.LeaderActionsInterval);
            Assert.Equal(TimeSpan.FromSeconds(1), settings.UnreachableNodesReaperInterval);
            Assert.Null(settings.PublishStatsInterval);
            Assert.Null(settings.AutoDownUnreachableAfter);
            Assert.Equal(1, settings.MinNrOfMembers);
            //TODO:
            //Assert.AreEqual(ImmutableDictionary.Create<string, int>(), settings.);
            Assert.Equal(ImmutableHashSet.Create<string>(), settings.Roles);
            Assert.Equal(Dispatchers.DefaultDispatcherId, settings.UseDispatcher);
            Assert.Equal(.8, settings.GossipDifferentViewProbability);
            Assert.Equal(400, settings.ReduceGossipDifferentViewProbability);
            Assert.Equal(TimeSpan.FromMilliseconds(33), settings.SchedulerTickDuration);
            Assert.Equal(512, settings.SchedulerTicksPerWheel);
            Assert.True(settings.MetricsEnabled);
            //TODO:
            //Assert.AreEqual(typeof(SigarMetricsCollector).FullName, settings.MetricsCollectorClass);
            Assert.Equal(TimeSpan.FromSeconds(3), settings.MetricsGossipInterval);
            Assert.Equal(TimeSpan.FromSeconds(12), settings.MetricsMovingAverageHalfLife);
        }
    }
}
