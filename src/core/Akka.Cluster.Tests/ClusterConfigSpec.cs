//-----------------------------------------------------------------------
// <copyright file="ClusterConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
using FluentAssertions;

namespace Akka.Cluster.Tests
{
    public class ClusterConfigSpec : AkkaSpec
    {
        public ClusterConfigSpec() : base(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""") { }

        [Fact]
        public void Clustering_must_be_able_to_parse_generic_cluster_config_elements()
        {
            var settings = new ClusterSettings(Sys.Settings.Config, Sys.Name);
            settings.LogInfo.Should().BeTrue();

            settings.SeedNodes.Should().BeEquivalentTo(ImmutableList.Create<Address>());
            settings.SeedNodeTimeout.Should().Be(5.Seconds());
            settings.RetryUnsuccessfulJoinAfter.Should().Be(10.Seconds());
            settings.PeriodicTasksInitialDelay.Should().Be(1.Seconds());
            settings.GossipInterval.Should().Be(1.Seconds());
            settings.GossipTimeToLive.Should().Be(2.Seconds());
            settings.HeartbeatInterval.Should().Be(1.Seconds());
            settings.MonitoredByNrOfMembers.Should().Be(5);
            settings.HeartbeatExpectedResponseAfter.Should().Be(1.Seconds());
            settings.LeaderActionsInterval.Should().Be(1.Seconds());
            settings.UnreachableNodesReaperInterval.Should().Be(1.Seconds());
            settings.PublishStatsInterval.Should().NotHaveValue();
            settings.AutoDownUnreachableAfter.Should().NotHaveValue();
            settings.DownRemovalMargin.Should().Be(TimeSpan.Zero);
            settings.MinNrOfMembers.Should().Be(1);
            settings.MinNrOfMembersOfRole.Should().Equal(ImmutableDictionary<string, int>.Empty);
            settings.Roles.Should().BeEquivalentTo(ImmutableHashSet<string>.Empty);
            settings.UseDispatcher.Should().Be(Dispatchers.DefaultDispatcherId);
            settings.GossipDifferentViewProbability.Should().Be(0.8);
            settings.ReduceGossipDifferentViewProbability.Should().Be(400);

            Type.GetType(settings.FailureDetectorImplementationClass).Should().Be(typeof(PhiAccrualFailureDetector));
            settings.FailureDetectorConfig.GetTimeSpan("heartbeat-interval").Should().Be(1.Seconds());
            settings.FailureDetectorConfig.GetDouble("threshold").Should().Be(8.0d);
            settings.FailureDetectorConfig.GetInt("max-sample-size").Should().Be(1000);
            settings.FailureDetectorConfig.GetTimeSpan("min-std-deviation").Should().Be(100.Milliseconds());
            settings.FailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause").Should().Be(3.Seconds());
            settings.FailureDetectorConfig.GetInt("monitored-by-nr-of-members").Should().Be(5);
            settings.FailureDetectorConfig.GetTimeSpan("expected-response-after").Should().Be(1.Seconds());

            settings.SchedulerTickDuration.Should().Be(33.Milliseconds());
            settings.SchedulerTicksPerWheel.Should().Be(512);

            settings.VerboseHeartbeatLogging.Should().BeFalse();
        }
    }
}