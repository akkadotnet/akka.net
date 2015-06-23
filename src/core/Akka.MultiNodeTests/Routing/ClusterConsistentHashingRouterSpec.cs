//-----------------------------------------------------------------------
// <copyright file="ClusterConsistentHashingRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTests.Routing
{
    public class ConsistentHashingRouterMultiNodeConfig : MultiNodeConfig
    {
        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(Self);
            }
        }

        private readonly RoleName _first;
        public RoleName First { get { return _first; } }

        private readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        private readonly RoleName _third;

        public RoleName Third { get { return _third; } }

        public ConsistentHashingRouterMultiNodeConfig()
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");

            CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(true))
                .WithFallback(ConfigurationFactory.ParseString(@"
                    common-router-settings = {
                        router = consistent-hashing-pool
                        nr-of-instances = 10
                        cluster {
                            enabled = on
                            max-nr-of-instances-per-node = 2
                        }
                    }
                    akka.actor.deployment {
                    /router1 = ${common-router-settings}
                    /router3 = ${common-router-settings}
                    /router4 = ${common-router-settings}
                    }
                    akka.cluster.publish-stats-interval = 5s
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterConsistentHashingRouterMultiNode1 : ClusterConsistentHashingRouterSpec { }
    public class ClusterConsistentHashingRouterMultiNode2 : ClusterConsistentHashingRouterSpec { }
    public class ClusterConsistentHashingRouterMultiNode3 : ClusterConsistentHashingRouterSpec { }

    public abstract class ClusterConsistentHashingRouterSpec : MultiNodeClusterSpec
    {
        private readonly ConsistentHashingRouterMultiNodeConfig _config;

        protected ClusterConsistentHashingRouterSpec() : this(new ConsistentHashingRouterMultiNodeConfig()) { }

        protected ClusterConsistentHashingRouterSpec(ConsistentHashingRouterMultiNodeConfig config) : base(config)
        {
            _config = config;
        }

        private IActorRef _router1 = null;

        protected IActorRef Router1
        {
            get { return _router1 ?? (_router1 = CreateRouter1()); }
        }

        private IActorRef CreateRouter1()
        {
            return
                Sys.ActorOf(
                    Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>().WithRouter(FromConfig.Instance),
                    "router1");
        }

        protected Routees CurrentRoutees(IActorRef router)
        {
            var routerAsk = router.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            return routerAsk.Result;
        }

        /// <summary>
        /// Fills in the self address for local ActorRef
        /// </summary>
        protected Address FullAddress(IActorRef actorRef)
        {
            if (string.IsNullOrEmpty(actorRef.Path.Address.Host) || !actorRef.Path.Address.Port.HasValue)
                return Cluster.SelfAddress;
            return actorRef.Path.Address;
        }

        protected void AssertHashMapping(IActorRef router)
        {
            // it may take some time until router receives cluster member events
            AwaitAssert(() =>
            {
                CurrentRoutees(router).Members.Count().ShouldBe(6);
            });
            var routees = CurrentRoutees(router);
            var routerMembers = routees.Members.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
            routerMembers.ShouldBe(Roles.Select(GetAddress).ToList());

            router.Tell("a", TestActor);
            var destinationA = ExpectMsg<IActorRef>();
            router.Tell("a", TestActor);
            ExpectMsg(destinationA);
        }

        //[MultiNodeFact(Skip = "Race conditions - needs debugging")]
        public void ClusterConsistentHashingRouterSpecs()
        {
            AClusterRouterWithConsistentHashingPoolMustStartClusterWith2Nodes();
            AClusterRouterWithConsistentHashingPoolMustCreateRouteesFromConfiguration();
            AClusterRouterWithConsistentHashingPoolMustSelectDestinationBasedOnHashKey();
            AClusterRouterWithConsistentHashingPoolMustDeployRouteesToNewMemberNodesInTheCluster();
            AClusterRouterWithConsistentHashingPoolMustDeployProgramaticallyDefinedRouteesToTheMemberNodesInTheCluster();
            AClusterRouterWithConsistentHashingPoolMustHandleCombinationOfConfiguredRouterAndProgramaticallyDefinedHashMapping();
            AClusterRouterWithConsistentHashingPoolMustHandleCombinationOfConfiguredRouterAndProgramaticallyDefinedHashMappingAndClusterConfig
                ();
            AClusterRouterWithConsistentHashingPoolMustRemoveRouteesFromDownedNode();
        }

        protected void AClusterRouterWithConsistentHashingPoolMustStartClusterWith2Nodes()
        {
            AwaitClusterUp(_config.First, _config.Second);
            EnterBarrier("after-1");
        }

        protected void AClusterRouterWithConsistentHashingPoolMustCreateRouteesFromConfiguration()
        {
            RunOn(() =>
            {
                // it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(Router1).Members.Count().ShouldBe(4);
                });
                var routees = CurrentRoutees(Router1);
                var routerMembers = routees.Members.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.ShouldBe(new List<Address>(){ GetAddress(_config.First), GetAddress(_config.Second) });
            }, _config.First);

            EnterBarrier("after-2");
        }

        protected void AClusterRouterWithConsistentHashingPoolMustSelectDestinationBasedOnHashKey()
        {
            RunOn(() =>
            {
                Router1.Tell(new ConsistentHashableEnvelope("A", "a"));
                var destinationA = ExpectMsg<IActorRef>();
                Router1.Tell(new ConsistentHashableEnvelope("AA", "a"));
                ExpectMsg(destinationA);
            }, _config.First);

            EnterBarrier("after-3");
        }

        protected void AClusterRouterWithConsistentHashingPoolMustDeployRouteesToNewMemberNodesInTheCluster()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                //it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(Router1).Members.Count().ShouldBe(6);
                });
                var routees = CurrentRoutees(Router1);
                var routerMembers = routees.Members.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.ShouldBe(Roles.Select(GetAddress).ToList());
            }, _config.First);

            EnterBarrier("after-4");
        }

        protected void
            AClusterRouterWithConsistentHashingPoolMustDeployProgramaticallyDefinedRouteesToTheMemberNodesInTheCluster()
        {
            RunOn(() =>
            {
                var router2 =
                    Sys.ActorOf(
                        new ClusterRouterPool(local: new ConsistentHashingPool(0),
                            settings: new ClusterRouterPoolSettings(totalInstances: 10, maxInstancesPerNode: 2,
                                allowLocalRoutees: true, useRole: null)).Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router2");

                //it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    var members = CurrentRoutees(router2).Members.Count();
                    members.ShouldBe(6);
                });
                var routees = CurrentRoutees(router2);
                var routerMembers = routees.Members.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.ShouldBe(Roles.Select(GetAddress).ToList());
            }, _config.First);

            EnterBarrier("after-5");
        }

        protected void
            AClusterRouterWithConsistentHashingPoolMustHandleCombinationOfConfiguredRouterAndProgramaticallyDefinedHashMapping()
        {
            RunOn(() =>
            {
                ConsistentHashMapping hashMapping = msg =>
                {
                    if (msg is string) return msg;
                    return null;
                };
                var router3 =
                    Sys.ActorOf(new ConsistentHashingPool(0).WithHashMapping(hashMapping).Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router3");
                
                AssertHashMapping(router3);
            }, _config.First);

            EnterBarrier("after-6");
        }

        protected void
            AClusterRouterWithConsistentHashingPoolMustHandleCombinationOfConfiguredRouterAndProgramaticallyDefinedHashMappingAndClusterConfig
            ()
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
                        new ClusterRouterPool(local: new ConsistentHashingPool(0).WithHashMapping(hashMapping),
                            settings: new ClusterRouterPoolSettings(totalInstances: 10, maxInstancesPerNode: 2,
                                allowLocalRoutees: true, useRole: null)).Props(Props.Create<ConsistentHashingRouterMultiNodeConfig.Echo>()), "router4");

                AssertHashMapping(router4);
            }, _config.First);

            EnterBarrier("after-7");
        }

        /// <summary>
        /// An explicit check to ensure that our routers can adjust to unreachable member events as well
        /// </summary>
        protected void AClusterRouterWithConsistentHashingPoolMustRemoveRouteesFromDownedNode()
        {
            RunOn(() =>
            {
                Cluster.Down(GetAddress(_config.Third));
                //removed
                AwaitAssert(() => Assert.False(ClusterView.UnreachableMembers.Select(x => x.Address).Contains(GetAddress(_config.Third))));
                AwaitAssert(() => Assert.False(ClusterView.Members.Select(x => x.Address).Contains(GetAddress(_config.Third))));

                // it may take some time until router receives cluster member events
                AwaitAssert(() =>
                {
                    CurrentRoutees(Router1).Members.Count().ShouldBe(4);
                });
                var routees = CurrentRoutees(Router1);
                var routerMembers = routees.Members.Select(x => FullAddress(((ActorRefRoutee)x).Actor)).Distinct().ToList();
                routerMembers.ShouldBe(new List<Address>() { GetAddress(_config.First), GetAddress(_config.Second) });

            }, _config.First);

            EnterBarrier("after-8");
        }
    }
}

