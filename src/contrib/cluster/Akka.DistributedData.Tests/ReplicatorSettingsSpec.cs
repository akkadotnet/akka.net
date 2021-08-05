// //-----------------------------------------------------------------------
// // <copyright file="DDataSettingsSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
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
            
            Sys.Settings.Config.GetTimeSpan("akka.cluster.distributed-data.serializer-cache-time-to-live")
                .ShouldBe(TimeSpan.FromSeconds(10));
            
            Sys.Settings.Config.GetString("akka.cluster.distributed-data.durable.store-actor-class")
                .ShouldBe("Akka.DistributedData.LightningDB.LmdbDurableStore, Akka.DistributedData.LightningDB");
            
            Sys.Settings.Config.GetString("akka.cluster.distributed-data.durable.use-dispatcher")
                .ShouldBe("akka.cluster.distributed-data.durable.pinned-store");
            
            Sys.Settings.Config.GetString("akka.actor.serializers.akka-data-replication")
                .ShouldBe("Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData");
            
            Sys.Settings.Config.GetString("akka.actor.serializers.akka-replicated-data")
                .ShouldBe("Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData");
            
            Sys.Settings.Config.GetString("akka.actor.serialization-bindings.\"Akka.DistributedData.IReplicatorMessage, Akka.DistributedData\"")
                .ShouldBe("akka-data-replication");
            
            Sys.Settings.Config.GetString("akka.actor.serialization-bindings.\"Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData\"")
                .ShouldBe("akka-replicated-data");
            
            
            Sys.Settings.Config.GetInt("akka.actor.serialization-identifiers.\"Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData\"")
                .ShouldBe(11);
            
            Sys.Settings.Config.GetInt("akka.actor.serialization-identifiers.\"Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData\"")
                .ShouldBe(12);
        }
    }
}