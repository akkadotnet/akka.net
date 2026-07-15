//-----------------------------------------------------------------------
// <copyright file="RollingUpdateShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class RollingUpdateShardAllocationSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public RollingUpdateShardAllocationSpecConfig()
            : base(additionalConfig: @"
                akka.cluster.sharding {
                    # speed up forming and handovers a bit
                    retry-interval = 500ms
                    waiting-for-state-timeout = 500ms
                    rebalance-interval = 1s
                    # we are leaving cluster nodes but they need to stay in test
                    akka.coordinated-shutdown.terminate-actor-system = off
                    # use the new LeastShardAllocationStrategy
                    akka.cluster.sharding.least-shard-allocation-strategy.rebalance-absolute-limit = 1
                }")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            NodeConfig(new[] { First, Second }, new[] { ConfigurationFactory.ParseString("akka.cluster.app-version = 1.0.0") });
            NodeConfig(new[] { Third, Fourth }, new[] { ConfigurationFactory.ParseString("akka.cluster.app-version = 1.0.1") });
        }
    }

    public class RollingUpdateShardAllocationSpec : MultiNodeClusterShardingSpec<RollingUpdateShardAllocationSpecConfig>
    {
        protected class GiveMeYourHome : ActorBase
        {
            public class Get
            {
                public Get(string id)
                {
                    Id = id;
                }

                public string Id { get; }
            }

            public class Home
            {
                public Home(Address address)
                {
                    Address = address;
                }

                public Address Address { get; }
            }

            public sealed class MessageExtractor: IMessageExtractor
            {
                // shard == id to make testing easier
                public string EntityId(object message)
                    => message switch
                    {
                        Get g => g.Id,
                        _ => null
                    };

                public object EntityMessage(object message)
                    => message;

                public string ShardId(object message)
                    => message switch
                    {
                        Get g => g.Id,
                        _ => null
                    };

                public string ShardId(string entityId, object messageHint = null)
                    => entityId;
            }

            private ILoggingAdapter _log;
            private ILoggingAdapter Log => _log ??= Context.GetLogger();

            private Address SelfAddress => Cluster.Get(Context.System).SelfAddress;

            public GiveMeYourHome()
            {
                Log.Info("Started on {0}", SelfAddress);
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Get _:
                        Sender.Tell(new Home(SelfAddress));
                        return true;
                }
                return false;
            }
        }


        private const string TypeName = "home";
        private readonly Lazy<IActorRef> shardRegion;

        public RollingUpdateShardAllocationSpec()
            : this(new RollingUpdateShardAllocationSpecConfig(), typeof(RollingUpdateShardAllocationSpec))
        {
        }

        protected RollingUpdateShardAllocationSpec(RollingUpdateShardAllocationSpecConfig config, Type type)
            : base(config, type)
        {
            shardRegion = new Lazy<IActorRef>(() =>
                StartSharding(
                    Sys,
                    typeName: TypeName,
                    entityProps: Props.Create(() => new GiveMeYourHome()),
                    messageExtractor: new GiveMeYourHome.MessageExtractor()));
        }

        private IEnumerable<Member> UpMembers => Cluster.State.Members.Where(m => m.Status == MemberStatus.Up);


        [MultiNodeFact]
        public async Task ClusterSharding_with_rolling_update_specs()
        {
            await ClusterSharding_must_form_cluster();
            await ClusterSharding_must_start_cluster_sharding_on_first();
            await ClusterSharding_must_start_a_rolling_upgrade();
            await ClusterSharding_must_complete_a_rolling_upgrade();
        }

        private async Task ClusterSharding_must_form_cluster()
        {
            await AwaitClusterUpAsync(Config.First, Config.Second);
            await EnterBarrierAsync("cluster-started");
        }

        private async Task ClusterSharding_must_start_cluster_sharding_on_first()
        {
            await RunOnAsync(async () =>
            {
                // make sure both regions have completed registration before triggering entity allocation
                // so the folloing allocations end up as one on each node
                //
                // Fresh probe per attempt + bounded inner expect + explicit 1s interval (the
                // #8363 / ClusterShardingQueriesSpec pattern): a no-duration AwaitAssert defaults
                // its outer window to akka.test.single-expect-default (5s), which is also the
                // inner ExpectMsg's default timeout -- so the outer "retry loop" only ever gets
                // one attempt on the shared TestActor mailbox, and a late reply arriving after
                // that attempt times out can pollute the next read. Bounding the inner expect well
                // under the outer window, and routing replies to a fresh probe each attempt,
                // restores genuine retries.
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                    (await probe.ExpectMsgAsync<CurrentRegions>(TimeSpan.FromSeconds(3))).Regions.Should().HaveCount(2);
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));

                shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"));
                // started on either of the nodes
                var address1 = (await ExpectMsgAsync<GiveMeYourHome.Home>()).Address;

                shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"));
                // started on the other of the nodes (because least
                var address2 = (await ExpectMsgAsync<GiveMeYourHome.Home>()).Address;

                // one on each node
                ImmutableHashSet.Create(address1, address2).Should().HaveCount(2);
            }, Config.First, Config.Second);
            await EnterBarrierAsync("first-version-started");
        }

        private async Task ClusterSharding_must_start_a_rolling_upgrade()
        {
            Join(Config.Third, Config.First);

            await RunOnAsync(async () =>
            {
                _ = shardRegion.Value;

                // new shards should now go on third since that is the highest version,
                // however there is a race where the shard has not yet completed registration
                // with the coordinator and shards will be allocated on the old nodes, so we need
                // to make sure the third region has completed registration before trying
                // if we didn't the strategy will default it back to the old nodes
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                    (await probe.ExpectMsgAsync<CurrentRegions>(TimeSpan.FromSeconds(3))).Regions.Should().HaveCount(3);
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
            }, Config.First, Config.Second, Config.Third);

            await EnterBarrierAsync("third-region-registered");
            await RunOnAsync(async () =>
            {
                shardRegion.Value.Tell(new GiveMeYourHome.Get("id3"));
                await ExpectMsgAsync<GiveMeYourHome.Home>();
            }, Config.First, Config.Second);
            await RunOnAsync(async () =>
            {
                // now third region should be only option as the other two are old versions
                // but first new allocated shard would anyway go there because of balance, so we
                // need to do more than one

                for (int n = 3; n <= 5; n++)
                {
                    shardRegion.Value.Tell(new GiveMeYourHome.Get($"id{n}"));
                    (await ExpectMsgAsync<GiveMeYourHome.Home>()).Address.Should().Be(Cluster.Get(Sys).SelfAddress);
                }
            }, Config.Third);
            await EnterBarrierAsync("rolling-upgrade-in-progress");
        }

        private async Task ClusterSharding_must_complete_a_rolling_upgrade()
        {
            Join(Config.Fourth, Config.First);

            RunOn(() =>
            {
                var cluster = Cluster.Get(Sys);
                cluster.Leave(cluster.SelfAddress);
            }, Config.First);
            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    UpMembers.Count().Should().Be(3);
                }, TimeSpan.FromSeconds(30));
            }, Config.Second, Config.Third, Config.Fourth);
            await EnterBarrierAsync("first-left");

            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                    (await probe.ExpectMsgAsync<CurrentRegions>(TimeSpan.FromSeconds(3))).Regions.Should().HaveCount(3);
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
            }, Config.Second, Config.Third, Config.Fourth);
            await EnterBarrierAsync("sharding-handed-off");

            // trigger allocation (no verification because we don't know which id was on node 1)
            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"), probe.Ref);
                    await probe.ExpectMsgAsync<GiveMeYourHome.Home>(TimeSpan.FromSeconds(3));

                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"), probe.Ref);
                    await probe.ExpectMsgAsync<GiveMeYourHome.Home>(TimeSpan.FromSeconds(3));
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
            }, Config.Second, Config.Third, Config.Fourth);
            await EnterBarrierAsync("first-allocated");

            RunOn(() =>
            {
                var cluster = Cluster.Get(Sys);
                cluster.Leave(cluster.SelfAddress);
            }, Config.Second);
            await RunOnAsync(async () =>
            {
                // make sure coordinator has noticed there are only two regions
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                    (await probe.ExpectMsgAsync<CurrentRegions>(TimeSpan.FromSeconds(3))).Regions.Should().HaveCount(2);
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
            }, Config.Third, Config.Fourth);
            await EnterBarrierAsync("second-left");

            // trigger allocation and verify where each was started
            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"), probe.Ref);
                    var address1 = (await probe.ExpectMsgAsync<GiveMeYourHome.Home>(TimeSpan.FromSeconds(3))).Address;
                    UpMembers.Select(i => i.Address).Should().Contain(address1);

                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"), probe.Ref);
                    var address2 = (await probe.ExpectMsgAsync<GiveMeYourHome.Home>(TimeSpan.FromSeconds(3))).Address;
                    UpMembers.Select(i => i.Address).Should().Contain(address2);
                }, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
            }, Config.Third, Config.Fourth);
            await EnterBarrierAsync("completo");
        }
    }
}
