//-----------------------------------------------------------------------
// <copyright file="ClusterConsistentHashingRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class ConsistentHashingRouterMultiNodeConfig : MultiNodeConfig
    {
        #region Test classes

        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(Self);
            }
        }

        #endregion

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ConsistentHashingRouterMultiNodeConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    common-router-settings = {
                        router = consistent-hashing-pool
                        cluster {
                            enabled = on
                            max-nr-of-instances-per-node = 2
                            max-total-nr-of-instances = 10
                        }
                    }
                    akka.actor.deployment {
                        /router1 = ${common-router-settings}
                        /router3 = ${common-router-settings}
                        /router4 = ${common-router-settings}
                    }
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterConsistentHashingRouterSpec : MultiNodeClusterSpec
    {
        private readonly ConsistentHashingRouterMultiNodeConfig _config;
        private Lazy<IActorRef> router1;

        public ClusterConsistentHashingRouterSpec() : this(new ConsistentHashingRouterMultiNodeConfig()) { }

        protected ClusterConsistentHashingRouterSpec(ConsistentHashingRouterMultiNodeConfig config) : base(config, typeof(ClusterConsistentHashingRouterSpec))
        {
            _config = config;

            router1 = new Lazy<IActorRef>(() =>
            {
                return Sys.ActorOf(FromConfig.Instance.Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router1");
            });
        }


        private IEnumerable<Routee> CurrentRoutees(IActorRef router)
        {
            return router.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null)).Result.Members;
        }

        private Address FullAddress(IActorRef actorRef)
        {
            if (string.IsNullOrEmpty(actorRef.Path.Address.Host) || !actorRef.Path.Address.Port.HasValue)
                return Cluster.SelfAddress;
            return actorRef.Path.Address;
        }

        private void AssertHashMapping(IActorRef router)
        {
            // it may take some time until router receives cluster member events
            AwaitAssert(() =>
            {
                CurrentRoutees(router).Count().ShouldBe(6);
            });
            var routees = CurrentRoutees(router);
            var routerMembers = new HashSet<Address>(routees.Select(x => FullAddress(((ActorRefRoutee)x).Actor)));
            var expected = new HashSet<Address>(Roles.Select(GetAddress).ToList());
            routerMembers.SetEquals(expected).Should().BeTrue($"Expected [{string.Join(",", expected)}] but was [{string.Join(",", routerMembers)}]");

            router.Tell("a", TestActor);
            var destinationA = ExpectMsg<IActorRef>();
            router.Tell("a", TestActor);
            ExpectMsg(destinationA);
        }

        [MultiNodeFact]
        public void ClusterConsistentHashingRouterSpecs()
        {
            A_cluster_router_with_consistent_hashing_pool_must_start_cluster_with2_nodes();
            A_cluster_router_with_consistent_hashing_pool_must_create_routees_from_configuration();
            A_cluster_router_with_consistent_hashing_pool_must_select_destination_based_on_hash_key();
            A_cluster_router_with_consistent_hashing_pool_must_deploy_routees_to_new_member_nodes_in_the_cluster();
            A_cluster_router_with_consistent_hashing_pool_must_deploy_programatically_defined_routees_to_the_member_nodes_in_the_cluster();
            A_cluster_router_with_consistent_hashing_pool_must_handle_combination_of_configured_router_and_programatically_defined_hash_mapping();
            A_cluster_router_with_consistent_hashing_pool_must_handle_combination_of_configured_router_and_programatically_defined_hash_mapping_and_cluster_config();

            //Custom specs
             A_cluster_router_with_consistent_hashing_pool_must_remove_routees_from_downed_node();
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_start_cluster_with2_nodes()
        {
            AwaitClusterUp(_config.First, _config.Second);
            EnterBarrier("after-1");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_create_routees_from_configuration()
        {
            RunOn(() =>
            {
                // it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(router1.Value).Should().HaveCount(4);
                });
                var routees = CurrentRoutees(router1.Value);
                routees
                    .Select(x => FullAddress(((ActorRefRoutee)x).Actor))
                    .ToImmutableHashSet()
                    .Should()
                    .BeEquivalentTo(new List<Address> { GetAddress(_config.First), GetAddress(_config.Second) });
            }, _config.First);

            EnterBarrier("after-2");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_select_destination_based_on_hash_key()
        {
            RunOn(() =>
            {
                router1.Value.Tell(new ConsistentHashableEnvelope("A", "a"));
                var destinationA = ExpectMsg<IActorRef>();
                router1.Value.Tell(new ConsistentHashableEnvelope("AA", "a"));
                ExpectMsg(destinationA);
            }, _config.First);

            EnterBarrier("after-2");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_deploy_routees_to_new_member_nodes_in_the_cluster()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                //it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(router1.Value).Should().HaveCount(6);
                });
                var routees = CurrentRoutees(router1.Value);
                var routerMembers = routees.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.Should().BeEquivalentTo(Roles.Select(GetAddress).ToList());
            }, _config.First);

            EnterBarrier("after-3");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_deploy_programatically_defined_routees_to_the_member_nodes_in_the_cluster()
        {
            RunOn(() =>
            {
                var router2 =
                    Sys.ActorOf(
                        new ClusterRouterPool(
                            local: new ConsistentHashingPool(nrOfInstances: 0),
                            settings: new ClusterRouterPoolSettings(
                                10,
                                2,
                                allowLocalRoutees: true,
                                useRole: null)).Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router2");

                //it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(router2).Should().HaveCount(6);
                });
                var routees = CurrentRoutees(router2);
                var routerMembers = routees.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.Should().BeEquivalentTo(Roles.Select(GetAddress).ToList());
            }, _config.First);

            EnterBarrier("after-4");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_handle_combination_of_configured_router_and_programatically_defined_hash_mapping()
        {
            RunOn(() =>
            {
                ConsistentHashMapping hashMapping = msg =>
                {
                    if (msg is string) return msg;
                    return null;
                };
                var router3 = Sys.ActorOf(new ConsistentHashingPool(nrOfInstances: 0).WithHashMapping(hashMapping)
                    .Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router3");

                AssertHashMapping(router3);
            }, _config.First);

            EnterBarrier("after-5");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_handle_combination_of_configured_router_and_programatically_defined_hash_mapping_and_cluster_config()
        {
            RunOn(() =>
            {
                ConsistentHashMapping hashMapping = msg =>
                {
                    if (msg is string) return msg;
                    return null;
                };

                var router4 =
                    Sys.ActorOf(
                        new ClusterRouterPool(
                            local: new ConsistentHashingPool(0).WithHashMapping(hashMapping),
                            settings: new ClusterRouterPoolSettings(
                                10,
                                1,
                                allowLocalRoutees: true,
                                useRole: null)).Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router4");

                AssertHashMapping(router4);
            }, _config.First);

            EnterBarrier("after-6");
        }

        protected void A_cluster_router_with_consistent_hashing_pool_must_remove_routees_from_downed_node()
        {
            RunOn(() =>
            {
                Cluster.Down(GetAddress(_config.Third));
                //removed
                AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.UnreachableMembers.Select(x => x.Address)));
                AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.Members.Select(x => x.Address)));

                // it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(router1.Value).Count().ShouldBe(4);
                });
                var routees = CurrentRoutees(router1.Value);
                var routerMembers = routees.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.ShouldBe(new List<Address>() { GetAddress(_config.First), GetAddress(_config.Second) });

            }, _config.First);

            EnterBarrier("after-8");
        }
    }
}
