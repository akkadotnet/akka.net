//-----------------------------------------------------------------------
// <copyright file="ClusterShardingInternalsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingInternalsSpec : Akka.TestKit.Xunit2.TestKit
    {
        ClusterSharding clusterSharding;

        public ClusterShardingInternalsSpec() : base(GetConfig())
        {
            clusterSharding = ClusterSharding.Get(Sys);
        }

        private Tuple<string, object> ExtractEntityId(object message)
        {
            switch (message)
            {
                case int i:
                    return new Tuple<string, object>(i.ToString(), message);
            }
            throw new NotSupportedException();
        }

        private string ExtractShardId(object message)
        {
            switch (message)
            {
                case int i:
                    return (i % 10).ToString();
            }
            throw new NotSupportedException();
        }


        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = cluster")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
        }

        [Fact]
        public void ClusterSharding_must_start_a_region_in_proxy_mode_in_case_of_node_role_mismatch()
        {
            var settingsWithRole = ClusterShardingSettings.Create(Sys).WithRole("nonExistingRole");
            var typeName = "typeName";

            var region = clusterSharding.Start(
                  typeName: typeName,
                  entityProps: Props.Empty,
                  settings: settingsWithRole,
                  extractEntityId: ExtractEntityId,
                  extractShardId: ExtractShardId,
                  allocationStrategy: new LeastShardAllocationStrategy(0, 0),
                  handOffStopMessage: PoisonPill.Instance);

            var proxy = clusterSharding.StartProxy(
                  typeName: typeName,
                  role: settingsWithRole.Role,
                  extractEntityId: ExtractEntityId,
                  extractShardId: ExtractShardId
                );

            region.Should().BeSameAs(proxy);
        }
    }
}
