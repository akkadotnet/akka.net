using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Remote;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Cluster.Tests
{
    [TestClass]
    public class ClusterConfigSpec : AkkaSpec
    {
        protected override string GetConfig()
        {
            return File.ReadAllText("reference.conf");
        }

        [TestMethod]
        public void ClusteringMustBeAbleToPareseGenericClusterConfigElements()
        {
            var settings = new ClusterSettings(System.Settings.Config, System.Name);
            Assert.IsTrue(settings.LogInfo);
            Assert.AreEqual(8, settings.FailureDetectorConfig.GetDouble("threshold"));
            Assert.AreEqual(1000, settings.FailureDetectorConfig.GetInt("max-sample-size"));
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), settings.FailureDetectorConfig.GetMillisDuration("min-std-deviation"));
            Assert.AreEqual(TimeSpan.FromSeconds(3), settings.FailureDetectorConfig.GetMillisDuration("acceptable-heartbeat-pause"));
            Assert.AreEqual(typeof(PhiAccrualFailureDetector).FullName, settings.FailureDetectorImplementationClass);
            CollectionAssert.AreEqual(ImmutableList.Create<Address>(), settings.SeedNodes);
            Assert.AreEqual(TimeSpan.FromSeconds(5), settings.SeedNodeTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(10), settings.RetryUnsuccessfulJoinAfter);
            Assert.AreEqual(TimeSpan.FromSeconds(1), settings.PeriodicTasksInitialDelay);
            Assert.AreEqual(TimeSpan.FromSeconds(1), settings.GossipInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(2), settings.GossipTimeToLive);
            Assert.AreEqual(TimeSpan.FromSeconds(1), settings.HeartbeatInterval);
            Assert.AreEqual(5, settings.MonitoredByNrOfMembers);
            Assert.AreEqual(TimeSpan.FromSeconds(5), settings.HeartbeatExpectedResponseAfter);
            Assert.AreEqual(TimeSpan.FromSeconds(1), settings.LeaderActionsInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(1), settings.UnreachableNodesReaperInterval);
            Assert.IsNull(settings.PublishStatsInterval);
            Assert.IsNull(settings.AutoDownUnreachableAfter);
            Assert.AreEqual(1, settings.MinNrOfMembers);
            //TODO:
            //Assert.AreEqual(ImmutableDictionary.Create<string, int>(), settings.);
            CollectionAssert.AreEqual(ImmutableHashSet.Create<string>(), settings.Roles);
            //TODO: Jmx
            Assert.AreEqual(Dispatchers.DefaultDispatcherId, settings.UseDispatcher);
            Assert.AreEqual(.8, settings.GossipDifferentViewProbability);
            Assert.AreEqual(400, settings.ReduceGossipDifferentViewProbability);
            Assert.AreEqual(TimeSpan.FromMilliseconds(33), settings.SchedulerTickDuration);
            Assert.AreEqual(512, settings.SchedulerTicksPerWheel);
            Assert.IsTrue(settings.MetricsEnabled);
            //TODO:
            //Assert.AreEqual(typeof(SigarMetricsCollector).FullName, settings.MetricsCollectorClass);
            Assert.AreEqual(TimeSpan.FromSeconds(3), settings.MetricsGossipInterval);
            Assert.AreEqual(TimeSpan.FromSeconds(12), settings.MetricsMovingAverageHalfLife);
        }
    }
}
