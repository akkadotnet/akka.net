//-----------------------------------------------------------------------
// <copyright file="ClusterConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Remote;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

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
            settings.ShutdownAfterUnsuccessfulJoinSeedNodes .Should().Be(null);
            settings.PeriodicTasksInitialDelay.Should().Be(1.Seconds());
            settings.GossipInterval.Should().Be(1.Seconds());
            settings.GossipTimeToLive.Should().Be(2.Seconds());
            settings.HeartbeatInterval.Should().Be(1.Seconds());
            settings.MonitoredByNrOfMembers.Should().Be(9);
            settings.HeartbeatExpectedResponseAfter.Should().Be(1.Seconds());
            settings.LeaderActionsInterval.Should().Be(1.Seconds());
            settings.UnreachableNodesReaperInterval.Should().Be(1.Seconds());
            settings.AllowWeaklyUpMembers.Should().BeTrue();
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
            settings.FailureDetectorConfig.GetTimeSpan("heartbeat-interval", null).Should().Be(1.Seconds());
            settings.FailureDetectorConfig.GetDouble("threshold", 0).Should().Be(8.0d);
            settings.FailureDetectorConfig.GetInt("max-sample-size", 0).Should().Be(1000);
            settings.FailureDetectorConfig.GetTimeSpan("min-std-deviation", null).Should().Be(100.Milliseconds());
            settings.FailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause", null).Should().Be(3.Seconds());
            settings.FailureDetectorConfig.GetInt("monitored-by-nr-of-members", 0).Should().Be(9);
            settings.FailureDetectorConfig.GetTimeSpan("expected-response-after", null).Should().Be(1.Seconds());

            settings.SchedulerTickDuration.Should().Be(33.Milliseconds());
            settings.SchedulerTicksPerWheel.Should().Be(512);

            settings.VerboseHeartbeatLogging.Should().BeFalse();
            settings.VerboseGossipReceivedLogging.Should().BeFalse();
            settings.RunCoordinatedShutdownWhenDown.Should().BeTrue();
        }
    }
}
