//-----------------------------------------------------------------------
// <copyright file="ClusterShardingInternalsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Util;
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

        private Option<(string, object)> ExtractEntityId(object message)
        {
            switch (message)
            {
                case int i:
                    return (i.ToString(), message);
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
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                     akka.remote.dot-netty.tcp.port = 0")

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

        [Fact]
        public void ClusterSharding_must_stop_entities_from_HandOffStopper_even_if_the_entity_doesnt_handle_handOffStopMessage()
        {
            var probe = CreateTestProbe();
            var shardName = "test";
            var emptyHandlerActor = Sys.ActorOf(Props.Create(() => new EmptyHandlerActor()));
            var handOffStopper = Sys.ActorOf(
                Props.Create(() => new ShardRegion.HandOffStopper(shardName, probe.Ref, new IActorRef[] { emptyHandlerActor }, HandOffStopMessage.Instance, TimeSpan.FromMilliseconds(10)))
              );

            Watch(emptyHandlerActor);
            ExpectTerminated(emptyHandlerActor, TimeSpan.FromSeconds(1));

            probe.ExpectMsg(new PersistentShardCoordinator.ShardStopped(shardName), TimeSpan.FromSeconds(1));
            probe.LastSender.Should().BeSameAs(handOffStopper);

            Watch(handOffStopper);
            ExpectTerminated(handOffStopper, TimeSpan.FromSeconds(1));
        }

        internal class HandOffStopMessage : INoSerializationVerificationNeeded
        {
            public static readonly HandOffStopMessage Instance = new HandOffStopMessage();
            private HandOffStopMessage()
            {
            }
        }

        internal class EmptyHandlerActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }
    }
}
