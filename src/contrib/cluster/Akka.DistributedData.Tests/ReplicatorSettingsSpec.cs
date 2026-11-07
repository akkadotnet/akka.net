//-----------------------------------------------------------------------
// <copyright file="ReplicatorSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Serialization;
using Akka.Dispatch;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    public class ReplicatorSettingsSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.Empty.WithFallback(DistributedData.DefaultConfig());

        public ReplicatorSettingsSpec() : base(Config)
        {
        }
        
        [Fact]
        public void SettingsShouldContainProperDefaultValues()
        {
            var settings = ReplicatorSettings.Create(Sys);
            settings.Role.ShouldBe(String.Empty);
            settings.GossipInterval.ShouldBe(TimeSpan.FromSeconds(2));
            settings.NotifySubscribersInterval.ShouldBe(TimeSpan.FromMilliseconds(500));
            settings.MaxDeltaElements.ShouldBe(500);
            settings.Dispatcher.ShouldBe("akka.actor.internal-dispatcher");
            settings.PruningInterval.ShouldBe(TimeSpan.FromSeconds(120));
            settings.MaxPruningDissemination.ShouldBe(TimeSpan.FromSeconds(300));
            settings.PruningMarkerTimeToLive.ShouldBe(TimeSpan.FromHours(6));
            settings.RestartReplicatorOnFailure.ShouldBeFalse();
            settings.MaxDeltaSize.ShouldBe(50);
            settings.DurableKeys.Count.ShouldBe(0);
            settings.DurableStoreProps.ShouldBe(Props.Empty);
            settings.DurablePruningMarkerTimeToLive.ShouldBe(TimeSpan.FromDays(10));
            settings.VerboseDebugLogging.Should().BeFalse();
            
            Sys.Settings.Config.GetTimeSpan("akka.cluster.distributed-data.serializer-cache-time-to-live")
                .ShouldBe(TimeSpan.FromSeconds(10));
            
            Sys.Settings.Config.GetString("akka.cluster.distributed-data.durable.store-actor-class")
                .ShouldBe("Akka.DistributedData.LightningDB.LmdbDurableStore, Akka.DistributedData.LightningDB");
            
            Sys.Settings.Config.GetString("akka.cluster.distributed-data.durable.use-dispatcher")
                .ShouldBe("akka.cluster.distributed-data.durable.pinned-store");
            
            // the serializers register from code, not from reference.conf rows, so ask Serialization for them
            var serialization = ((ExtendedActorSystem)Sys).Serialization;

            serialization.GetSerializerById(11).Should().BeOfType<ReplicatedDataSerializer>();
            serialization.GetSerializerById(12).Should().BeOfType<ReplicatorMessageSerializer>();

            serialization.FindSerializerForType(typeof(IReplicatorMessage)).Should().BeOfType<ReplicatorMessageSerializer>();
            serialization.FindSerializerForType(typeof(IReplicatedDataSerialization)).Should().BeOfType<ReplicatedDataSerializer>();
        }
    }
}
