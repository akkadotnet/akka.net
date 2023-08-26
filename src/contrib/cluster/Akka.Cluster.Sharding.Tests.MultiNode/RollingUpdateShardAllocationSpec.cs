//-----------------------------------------------------------------------
// <copyright file="RollingUpdateShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

            static internal ExtractEntityId ExtractEntityId = message =>
            {
                if (message is Get get)
                    return (get.Id, get);
                return Option<(string, object)>.None;
            };

            static internal ExtractShardId ExtractShardId = message =>
            {
                switch (message)
                {
                    case Get get:
                        return get.Id;
                }
                return null;
            };

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
                    extractEntityId: GiveMeYourHome.ExtractEntityId,
                    extractShardId: GiveMeYourHome.ExtractShardId));
        }

        private IEnumerable<Member> UpMembers => Cluster.State.Members.Where(m => m.Status == MemberStatus.Up);


        [MultiNodeFact]
        public void ClusterSharding_with_rolling_update_specs()
        {
            ClusterSharding_must_form_cluster();
            ClusterSharding_must_start_cluster_sharding_on_first();
            ClusterSharding_must_start_a_rolling_upgrade();
            ClusterSharding_must_complete_a_rolling_upgrade();
        }

        private void ClusterSharding_must_form_cluster()
        {
            AwaitClusterUp(config.First, config.Second);
            EnterBarrier("cluster-started");
        }

        private void ClusterSharding_must_start_cluster_sharding_on_first()
        {
            RunOn(() =>
            {
                // make sure both regions have completed registration before triggering entity allocation
                // so the folloing allocations end up as one on each node
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>().Regions.Should().HaveCount(2);
                });

                shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"));
                // started on either of the nodes
                var address1 = ExpectMsg<GiveMeYourHome.Home>().Address;

                shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"));
                // started on the other of the nodes (because least
                var address2 = ExpectMsg<GiveMeYourHome.Home>().Address;

                // one on each node
                ImmutableHashSet.Create(address1, address2).Should().HaveCount(2);
            }, config.First, config.Second);
            EnterBarrier("first-version-started");
        }

        private void ClusterSharding_must_start_a_rolling_upgrade()
        {
            Join(config.Third, config.First);

            RunOn(() =>
            {
                _ = shardRegion.Value;

                // new shards should now go on third since that is the highest version,
                // however there is a race where the shard has not yet completed registration
                // with the coordinator and shards will be allocated on the old nodes, so we need
                // to make sure the third region has completed registration before trying
                // if we didn't the strategy will default it back to the old nodes
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>().Regions.Should().HaveCount(3);
                });
            }, config.First, config.Second, config.Third);

            EnterBarrier("third-region-registered");
            RunOn(() =>
            {
                shardRegion.Value.Tell(new GiveMeYourHome.Get("id3"));
                ExpectMsg<GiveMeYourHome.Home>();
            }, config.First, config.Second);
            RunOn(() =>
            {
                // now third region should be only option as the other two are old versions
                // but first new allocated shard would anyway go there because of balance, so we
                // need to do more than one

                for (int n = 3; n <= 5; n++)
                {
                    shardRegion.Value.Tell(new GiveMeYourHome.Get($"id{n}"));
                    ExpectMsg<GiveMeYourHome.Home>().Address.Should().Be(Cluster.Get(Sys).SelfAddress);
                }
            }, config.Third);
            EnterBarrier("rolling-upgrade-in-progress");
        }

        private void ClusterSharding_must_complete_a_rolling_upgrade()
        {
            Join(config.Fourth, config.First);

            RunOn(() =>
            {
                var cluster = Cluster.Get(Sys);
                cluster.Leave(cluster.SelfAddress);
            }, config.First);
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    UpMembers.Count().Should().Be(3);
                });
            }, config.Second, config.Third, config.Fourth);
            EnterBarrier("first-left");

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>().Regions.Should().HaveCount(3);

                }, TimeSpan.FromSeconds(30));
            }, config.Second, config.Third, config.Fourth);
            EnterBarrier("sharding-handed-off");

            // trigger allocation (no verification because we don't know which id was on node 1)
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"));
                    ExpectMsg<GiveMeYourHome.Home>();

                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"));
                    ExpectMsg<GiveMeYourHome.Home>();
                });
            }, config.Second, config.Third, config.Fourth);
            EnterBarrier("first-allocated");

            RunOn(() =>
            {
                var cluster = Cluster.Get(Sys);
                cluster.Leave(cluster.SelfAddress);
            }, config.Second);
            RunOn(() =>
            {
                // make sure coordinator has noticed there are only two regions
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>().Regions.Should().HaveCount(2);
                }, TimeSpan.FromSeconds(30));
            }, config.Third, config.Fourth);
            EnterBarrier("second-left");

            // trigger allocation and verify where each was started
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id1"));
                    var address1 = ExpectMsg<GiveMeYourHome.Home>().Address;
                    UpMembers.Select(i => i.Address).Should().Contain(address1);

                    shardRegion.Value.Tell(new GiveMeYourHome.Get("id2"));
                    var address2 = ExpectMsg<GiveMeYourHome.Home>().Address;
                    UpMembers.Select(i => i.Address).Should().Contain(address2);
                });
            }, config.Third, config.Fourth);
            EnterBarrier("completo");
        }
    }
}
