//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRolePartitioningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingMinMembersPerRoleConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public Config R1Config { get; }
        public Config R2Config { get; }

        public ClusterShardingMinMembersPerRoleConfig()
            : base(loglevel: "DEBUG")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            R1Config = ConfigurationFactory.ParseString(@"akka.cluster.roles = [ ""R1"" ]");
            R2Config = ConfigurationFactory.ParseString(@"akka.cluster.roles = [ ""R2"" ]");
            Configure();
        }

        protected virtual void Configure()
        {
        }
    }

    public class ClusterShardingMinMembersPerRoleNotConfiguredConfig : ClusterShardingMinMembersPerRoleConfig
    {
        public ClusterShardingMinMembersPerRoleNotConfiguredConfig()
        {
        }

        protected override void Configure()
        {
            var commonRoleConfig = ConfigurationFactory.ParseString("akka.cluster.min-nr-of-members = 2");

            NodeConfig(new[] { First, Second, Third }, new[] { R1Config.WithFallback(commonRoleConfig) });
            NodeConfig(new[] { Fourth, Fifth }, new[] { R2Config.WithFallback(commonRoleConfig) });
        }
    }

    public class ClusterShardingMinMembersPerRoleConfiguredConfig : ClusterShardingMinMembersPerRoleConfig
    {
        public ClusterShardingMinMembersPerRoleConfiguredConfig()
        {
        }

        protected override void Configure()
        {
            var commonRoleConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.min-nr-of-members = 3
                akka.cluster.role.R1.min-nr-of-members = 3
                akka.cluster.role.R2.min-nr-of-members = 2
            ");

            NodeConfig(new[] { First, Second, Third }, new[] { R1Config.WithFallback(commonRoleConfig) });
            NodeConfig(new[] { Fourth, Fifth }, new[] { R2Config.WithFallback(commonRoleConfig) });
        }
    }

    public class ClusterShardingMinMembersPerRoleNotConfiguredSpec : ClusterShardingRolePartitioningSpec
    {
        public ClusterShardingMinMembersPerRoleNotConfiguredSpec()
            : base(new ClusterShardingMinMembersPerRoleNotConfiguredConfig(), typeof(ClusterShardingMinMembersPerRoleNotConfiguredSpec))
        {
        }
    }

    public class ClusterShardingMinMembersPerRoleSpec : ClusterShardingRolePartitioningSpec
    {
        public ClusterShardingMinMembersPerRoleSpec()
            : base(new ClusterShardingMinMembersPerRoleConfiguredConfig(), typeof(ClusterShardingMinMembersPerRoleSpec))
        {
        }
    }

    public abstract class ClusterShardingRolePartitioningSpec : MultiNodeClusterShardingSpec<ClusterShardingMinMembersPerRoleConfig>
    {
        #region setup

        private static class E1
        {
            public const string TypeKey = "Datatype1";

            public sealed class MessageExtractor: IMessageExtractor
            {
                public string EntityId(object message)
                    => message switch
                    {
                        string id => id,
                        _ => null
                    };

                public object EntityMessage(object message)
                    => message;

                public string ShardId(object message)
                    => message switch
                    {
                        string id => id,
                        _ => null
                    };

                public string ShardId(string entityId, object messageHint = null)
                    => entityId;
            }
        }

        private static class E2
        {
            public const string TypeKey = "Datatype2";

            public sealed class MessageExtractor: IMessageExtractor
            {
                public string EntityId(object message)
                    => message switch
                    {
                        int id => id.ToString(),
                        _ => null
                    };

                public object EntityMessage(object message)
                    => message;

                public string ShardId(object message)
                    => message switch
                    {
                        int id => id.ToString(),
                        _ => null
                    };

                public string ShardId(string entityId, object messageHint = null)
                    => entityId;
            }
        }

        private readonly Address fourthAddress;
        private readonly Address fifthAddress;

        protected ClusterShardingRolePartitioningSpec(ClusterShardingMinMembersPerRoleConfig config, Type type)
            : base(config, type)
        {
            fourthAddress = Node(config.Fourth).Address;
            fifthAddress = Node(config.Fifth).Address;
        }

        #endregion

        [MultiNodeFact]
        public async Task Cluster_Sharding_with_roles_specs()
        {
            await Cluster_Sharding_with_roles_must_start_the_cluster_await_convergence_init_sharding_on_every_node_2_data_types__akka_cluster_min_nr_of_members_2_partition_shard_location_by_2_roles();
            await Cluster_Sharding_with_roles_must_access_role_R2_nodes_4_5_from_one_of_the_proxy_nodes_1_2_3();
        }

        private async Task Cluster_Sharding_with_roles_must_start_the_cluster_await_convergence_init_sharding_on_every_node_2_data_types__akka_cluster_min_nr_of_members_2_partition_shard_location_by_2_roles()
        {
            // start sharding early
            StartSharding(
              Sys,
              typeName: E1.TypeKey,
              entityProps: SimpleEchoActor.Props(),
              // nodes 1,2,3: role R1, shard region E1, proxy region E2
              settings: Settings.Value.WithRole("R1"),
              messageExtractor: new E1.MessageExtractor());

            // when run on first, second and third (role R1) proxy region is started
            StartSharding(
                Sys,
                typeName: E2.TypeKey,
                entityProps: SimpleEchoActor.Props(),
                // nodes 4,5: role R2, shard region E2, proxy region E1
                settings: Settings.Value.WithRole("R2"),
                messageExtractor: new E2.MessageExtractor());

            await AwaitClusterUpAsync(Config.First, Config.Second, Config.Third, Config.Fourth, Config.Fifth);

            await RunOnAsync(async () =>
            {
                // Wait for all regions to register. Use an explicit, generous window here:
                // during the initial 5-node join the shard-coordinator singleton can still be
                // migrating between R1 nodes, so region (re-)registration routinely takes longer
                // than the default 5s AwaitAssert budget on a busy CI agent. See MNTR flakiness
                // where this asserted 2 regions instead of 3 before convergence completed.
                await AwaitAssertAsync(async () =>
                {
                    var region = ClusterSharding.Get(Sys).ShardRegion(E1.TypeKey);
                    region.Tell(GetCurrentRegions.Instance);
                    (await ExpectMsgAsync<CurrentRegions>()).Regions.Count.Should().Be(3);
                }, TimeSpan.FromSeconds(30));
                await AwaitAssertAsync(async () =>
                {
                    var region = ClusterSharding.Get(Sys).ShardRegion(E2.TypeKey);
                    region.Tell(GetCurrentRegions.Instance);
                    (await ExpectMsgAsync<CurrentRegions>()).Regions.Count.Should().Be(2);
                }, TimeSpan.FromSeconds(30));
            }, Config.Fourth);

            await EnterBarrierAsync($"{Roles.Count}-up");
        }

        private async Task Cluster_Sharding_with_roles_must_access_role_R2_nodes_4_5_from_one_of_the_proxy_nodes_1_2_3()
        {
            await RunOnAsync(async () =>
            {
                // have first message reach the entity from a proxy with 2 nodes of role R2 and 'min-nr-of-members' set globally versus per role (nodes 4,5, with 1,2,3 proxying)
                // RegisterProxy messages from nodes 1,2,3 are deadlettered
                // Register messages sent are eventually successful on the fifth node, once coordinator moves to active state
                var region = ClusterSharding.Get(Sys).ShardRegion(E2.TypeKey);

                // Use AwaitAssert for the first message to handle coordinator readiness.
                // The coordinator may not respond to GetShardHome requests until HasAllRegionsRegistered()
                // returns true. This happens when _aliveRegions.Count >= _minMembers. Even though we verified
                // regions are registered above, the coordinator's internal state (specifically _allRegionsRegistered)
                // may not be set yet, causing GetShardHome requests to be silently ignored and messages to be
                // buffered. The ShardRegion only retries GetShardHome on its retry interval (default 2-10s),
                // which can exceed our ExpectMsg timeout.
                // By wrapping the first message in AwaitAssert, we retry until the coordinator is fully ready.
                // Use an explicit 30s window: the ShardRegion only retries GetShardHome on its retry
                // interval (2-10s), so the default 5s AwaitAssert budget allows barely one or two 3s
                // ExpectMsg attempts - not enough headroom on a busy CI agent (source of MNTR flakiness).
                await AwaitAssertAsync(async () =>
                {
                    region.Tell(1);
                    await ExpectMsgAsync(1, TimeSpan.FromSeconds(3));
                }, TimeSpan.FromSeconds(30));

                // After the first message succeeds, the shard is allocated and subsequent messages
                // to the same or new shards should succeed without delay (coordinator is ready)
                foreach (var n in Enumerable.Range(2, 19))
                {
                    region.Tell(n);
                    await ExpectMsgAsync(n); // R2 entity received, does not timeout
                }

                region.Tell(new GetClusterShardingStats(TimeSpan.FromSeconds(10)));
                var stats = await ExpectMsgAsync<ClusterShardingStats>();

                stats.Regions.Keys.Should().BeEquivalentTo(fourthAddress, fifthAddress);
                stats.Regions.Values.SelectMany(i => i.Stats.Values).Count().Should().Be(20);
            }, Config.First);
            await EnterBarrierAsync("proxy-node-other-role-to-shard");
        }
    }
}
