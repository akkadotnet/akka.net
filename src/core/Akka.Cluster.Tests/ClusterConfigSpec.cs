//-----------------------------------------------------------------------
// <copyright file="ClusterConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.SBR;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
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
            settings.WeaklyUpAfter.Should().Be(7.Seconds());
            settings.PublishStatsInterval.Should().NotHaveValue();
#pragma warning disable CS0618
            settings.AutoDownUnreachableAfter.Should().NotHaveValue();
#pragma warning restore CS0618
            settings.DownRemovalMargin.Should().Be(TimeSpan.Zero);
            settings.MinNrOfMembers.Should().Be(1);
            settings.MinNrOfMembersOfRole.Should().Equal(ImmutableDictionary<string, int>.Empty);
            settings.Roles.Should().BeEquivalentTo(ImmutableHashSet<string>.Empty);

            var appVersion = AppVersion.AppVersionFromAssemblyVersion();
            settings.AppVersion.Should().Be(appVersion);
            settings.UseDispatcher.Should().Be(Dispatchers.InternalDispatcherId);
            settings.GossipDifferentViewProbability.Should().Be(0.8);
            settings.ReduceGossipDifferentViewProbability.Should().Be(400);

            Type.GetType(settings.FailureDetectorImplementationClass).Should().Be(typeof(PhiAccrualFailureDetector));
            settings.FailureDetectorConfig.GetTimeSpan("heartbeat-interval").Should().Be(1.Seconds());
            settings.FailureDetectorConfig.GetDouble("threshold").Should().Be(8.0d);
            settings.FailureDetectorConfig.GetInt("max-sample-size").Should().Be(1000);
            settings.FailureDetectorConfig.GetTimeSpan("min-std-deviation").Should().Be(100.Milliseconds());
            settings.FailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause").Should().Be(3.Seconds());
            settings.FailureDetectorConfig.GetInt("monitored-by-nr-of-members").Should().Be(9);
            settings.FailureDetectorConfig.GetTimeSpan("expected-response-after").Should().Be(1.Seconds());

            settings.SchedulerTickDuration.Should().Be(33.Milliseconds());
            settings.SchedulerTicksPerWheel.Should().Be(512);

            settings.VerboseHeartbeatLogging.Should().BeFalse();
            settings.VerboseGossipReceivedLogging.Should().BeFalse();
            settings.RunCoordinatedShutdownWhenDown.Should().BeTrue();
            
            // downing provider settings
            settings.DowningProviderType.Should().Be<SplitBrainResolverProvider>();
            var sbrSettings = new SplitBrainResolverSettings(Sys.Settings.Config);
            sbrSettings.DowningStableAfter.Should().Be(20.Seconds());
            sbrSettings.DownAllWhenUnstable.Should().Be(15.Seconds()); // 3/4 OF DowningStableAfter
            sbrSettings.DowningStrategy.Should().Be("keep-majority");
        }

        /// <summary>
        /// To verify that overriding AppVersion from HOCON works
        /// </summary>
        [Fact]
        public void Clustering_should_parse_nondefault_AppVersion()
        {
            Config config = "akka.cluster.app-version = \"0.0.0\"";
            var settings = new ClusterSettings(config.WithFallback(Sys.Settings.Config), Sys.Name);
            settings.AppVersion.Should().Be(AppVersion.Zero);
        }

        /// <summary>
        /// Validate that we can disable the default downing provider if needed
        /// </summary>
        [Fact]
        public void Cluster_should_allow_disabling_of_default_DowningProvider()
        {
            // configure HOCON to disable the default akka.cluster downing provider
            Config config = "akka.cluster.downing-provider-class = \"\"";
            var settings = new ClusterSettings(config.WithFallback(Sys.Settings.Config), Sys.Name);
            settings.DowningProviderType.Should().Be<NoDowning>();
        }
    }
}
