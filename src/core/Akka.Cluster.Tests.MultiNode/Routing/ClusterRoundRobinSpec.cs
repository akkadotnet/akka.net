//-----------------------------------------------------------------------
// <copyright file="ClusterRoundRobinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Routing;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class ClusterRoundRobinSpecConfig : MultiNodeConfig
    {
        internal interface IRouteeType { }
        internal class PoolRoutee : IRouteeType { }
        internal class GroupRoutee : IRouteeType { }

        internal class Reply
        {
            public Reply(IRouteeType routeeType, IActorRef actorRef)
            {
                RouteeType = routeeType;
                ActorRef = actorRef;
            }

            public IRouteeType RouteeType { get; }

            public IActorRef ActorRef { get; }
        }

        internal class SomeActor : ReceiveActor
        {
            private readonly IRouteeType _routeeType;

            public SomeActor() : this(new PoolRoutee())
            {

            }

            public SomeActor(IRouteeType routeeType)
            {
                _routeeType = routeeType;
                Receive<string>(s => s == "hit", s =>
                {
                    Sender.Tell(new Reply(_routeeType, Self));
                });
            }
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public ClusterRoundRobinSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                      akka.actor.deployment {
                        /router1 {
                          router = round-robin-pool
                          cluster {
                            enabled = on
                            max-nr-of-instances-per-node = 2
                            max-total-nr-of-instances = 10
                          }
                        }
                        /router3 {
                          router = round-robin-pool
                          cluster {
                            enabled = on
                            max-nr-of-instances-per-node = 1
                            max-total-nr-of-instances = 10
                            allow-local-routees = off
                          }
                        }
                        /router4 {
                          router = round-robin-group
                          routees.paths = [""/user/myserviceA"", ""/user/myserviceB""]
                          cluster.enabled = on
                          cluster.max-total-nr-of-instances = 10
                        }
                        /router5 {
                          router = round-robin-pool
                          cluster {
                            enabled = on
                            use-role = a
                            max-total-nr-of-instances = 10
                          }
                        }
                      }
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new List<RoleName> { First, Second }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""a"", ""c""]")  });
            NodeConfig(new List<RoleName> { Third }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""b"", ""c""]") });

            TestTransport = true;
        }
    }

    public class ClusterRoundRobinSpec : MultiNodeClusterSpec
    {
        private readonly ClusterRoundRobinSpecConfig _config;
        private readonly Lazy<IActorRef> router1;
        private readonly Lazy<IActorRef> router2;
        private readonly Lazy<IActorRef> router3;
        private readonly Lazy<IActorRef> router4;
        private readonly Lazy<IActorRef> router5;

        public ClusterRoundRobinSpec() : this(new ClusterRoundRobinSpecConfig())
        {
        }

        protected ClusterRoundRobinSpec(ClusterRoundRobinSpecConfig config)
            : base(config, typeof(ClusterRoundRobinSpec))
        {
            _config = config;

            router1 = new Lazy<IActorRef>(() => Sys.ActorOf(FromConfig.Instance.Props(Props.Create<ClusterRoundRobinSpecConfig.SomeActor>()), "router1"));
            router2 = new Lazy<IActorRef>(() => Sys.ActorOf(
                new ClusterRouterPool(
                    new RoundRobinPool(0),
                    new ClusterRouterPoolSettings(3, 1, true, null)).Props(Props.Create<ClusterRoundRobinSpecConfig.SomeActor>()),
                "router2"));
            router3 = new Lazy<IActorRef>(() => Sys.ActorOf(FromConfig.Instance.Props(Props.Create<ClusterRoundRobinSpecConfig.SomeActor>()), "router3"));
            router4 = new Lazy<IActorRef>(() => Sys.ActorOf(FromConfig.Instance.Props(), "router4"));
            router5 = new Lazy<IActorRef>(() => Sys.ActorOf(new RoundRobinPool(0).Props(Props.Create<ClusterRoundRobinSpecConfig.SomeActor>()), "router5"));
        }

        private IEnumerable<Routee> CurrentRoutees(IActorRef router)
        {
            var routerAsk = router.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            return routerAsk.Result.Members;
        }

        private Address FullAddress(IActorRef actorRef)
        {
            if (string.IsNullOrEmpty(actorRef.Path.Address.Host) || !actorRef.Path.Address.Port.HasValue)
                return Cluster.SelfAddress;
            return actorRef.Path.Address;
        }

        private Dictionary<Address, int> ReceiveReplays(ClusterRoundRobinSpecConfig.IRouteeType routeeType, int expectedReplies)
        {
            var zero = Roles.Select(c => GetAddress(c)).ToDictionary(c => c, c => 0);
            var replays = ReceiveWhile(5.Seconds(), msg =>
            {
                if (msg is ClusterRoundRobinSpecConfig.Reply routee && routee.RouteeType.GetType() == routeeType.GetType())
                    return FullAddress(routee.ActorRef);
                return null;
            }, expectedReplies).Aggregate(zero, (replyMap, address) =>
            {
                replyMap[address]++;
                return replyMap;
            });

            return replays;
        }

        [MultiNodeFact]
        public void ClusterRoundRobinSpecs()
        {
            A_cluster_router_with_a_RoundRobin_router_must_start_cluster_with_2_nodes();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_the_member_nodes_in_the_cluster();
            A_cluster_router_with_a_RoundRobin_router_must_lookup_routees_on_the_member_nodes_in_the_cluster();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_new_nodes_in_the_cluster();
            A_cluster_router_with_a_RoundRobin_router_must_lookup_routees_on_new_nodes_in_the_cluster();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_only_remote_nodes_when_allowlocalrouteesoff();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_specified_node_role();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_programatically_defined_routees_to_the_member_nodes_in_the_cluster();
            A_cluster_router_with_a_RoundRobin_router_must_remove_routees_for_unreachable_nodes_and_add_when_reachable_again();
            A_cluster_router_with_a_RoundRobin_router_must_deploy_programatically_defined_routees_to_other_node_when_a_node_becomes_down();
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_start_cluster_with_2_nodes()
        {
            AwaitClusterUp(_config.First, _config.Second);
            EnterBarrier("after-1");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_the_member_nodes_in_the_cluster()
        {
            RunOn(() =>
            {
                router1.Value.Should().BeOfType<RoutedActorRef>();

                // max-nr-of-instances-per-node=2 times 2 nodes
                AwaitAssert(() => CurrentRoutees(router1.Value).Count().Should().Be(4));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router1.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);

                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().Be(0);
                replays[GetAddress(_config.Fourth)].Should().Be(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-2");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_lookup_routees_on_the_member_nodes_in_the_cluster()
        {
            // cluster consists of first and second
            Sys.ActorOf(Props.Create(() => new ClusterRoundRobinSpecConfig.SomeActor(new ClusterRoundRobinSpecConfig.GroupRoutee())),
                "myserviceA");
            Sys.ActorOf(Props.Create(() => new ClusterRoundRobinSpecConfig.SomeActor(new ClusterRoundRobinSpecConfig.GroupRoutee())),
                "myserviceB");
            EnterBarrier("myservice-started");

            RunOn(() =>
            {
                // 2 nodes, 2 routees on each node
                Within(10.Seconds(), () =>
                {
                    AwaitAssert(() => CurrentRoutees(router4.Value).Count().Should().Be(4));
                });

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router4.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.GroupRoutee(), iterationCount);

                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().Be(0);
                replays[GetAddress(_config.Fourth)].Should().Be(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-3");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_new_nodes_in_the_cluster()
        {
            // add third and fourth
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);

            RunOn(() =>
            {
                // max-nr-of-instances-per-node=2 times 4 nodes
                AwaitAssert(() => CurrentRoutees(router1.Value).Count().Should().Be(8));

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router1.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);

                replays.Values.ForEach(x => x.Should().BeGreaterThan(0));
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-4");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_lookup_routees_on_new_nodes_in_the_cluster()
        {
            // cluster consists of first, second, third and fourth
            RunOn(() =>
            {
                // 4 nodes, 2 routee on each node
                AwaitAssert(() => CurrentRoutees(router4.Value).Count().Should().Be(8));

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router4.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.GroupRoutee(), iterationCount);

                replays.Values.ForEach(x => x.Should().BeGreaterThan(0));
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-5");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_only_remote_nodes_when_allowlocalrouteesoff()
        {
            RunOn(() =>
            {
                // max-nr-of-instances-per-node=1 times 3 nodes
                AwaitAssert(() => CurrentRoutees(router3.Value).Count().Should().Be(3));

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router3.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);

                replays[GetAddress(_config.First)].Should().Be(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Fourth)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-6");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_routees_to_specified_node_role()
        {
            RunOn(() =>
            {
                AwaitAssert(() => CurrentRoutees(router5.Value).Count().Should().Be(2));

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router5.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);

                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().Be(0);
                replays[GetAddress(_config.Fourth)].Should().Be(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-7");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_programatically_defined_routees_to_the_member_nodes_in_the_cluster()
        {
            RunOn(() =>
            {
                router2.Value.Should().BeOfType<RoutedActorRef>();

                // totalInstances = 3, maxInstancesPerNode = 1
                AwaitAssert(() => CurrentRoutees(router2.Value).Count().Should().Be(3));

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router2.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);
                
                // note that router2 has totalInstances = 3, maxInstancesPerNode = 1
                var routees = CurrentRoutees(router2.Value);
                var routeeAddresses = routees.Where(c => c is ActorRefRoutee).Select(c => FullAddress(((ActorRefRoutee)c).Actor));

                routeeAddresses.Count().Should().Be(3);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-8");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_remove_routees_for_unreachable_nodes_and_add_when_reachable_again()
        {
            Within(30.Seconds(), () =>
            {
                // myservice is already running

                Func<List<Routee>> routees = () => CurrentRoutees(router4.Value).ToList();
                Func<List<Address>> routeeAddresses = () => routees()
                        .Where(c => c is ActorSelectionRoutee)
                        .Select(c => FullAddress(((ActorSelectionRoutee)c).Selection.Anchor))
                        .ToList();

                RunOn(() =>
                {
                    // 4 nodes, 2 routees on each node
                    AwaitAssert(() => CurrentRoutees(router4.Value).Count().Should().Be(8));

                    TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();

                    AwaitAssert(() => routees().Count.Should().Be(6));
                    routeeAddresses().Should().NotContain(GetAddress(_config.Second));

                    TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both);
                    AwaitAssert(() => routees().Count.Should().Be(8));
                    routeeAddresses().Should().Contain(GetAddress(_config.Second));
                }, _config.First);
            });

            EnterBarrier("after-9");
        }

        private void A_cluster_router_with_a_RoundRobin_router_must_deploy_programatically_defined_routees_to_other_node_when_a_node_becomes_down()
        {
            MuteMarkingAsUnreachable();

            RunOn(() =>
            {
                Func<List<Routee>> routees = () => CurrentRoutees(router2.Value).ToList();
                Func<List<Address>> routeeAddresses = () => routees()
                    .Where(c => c is ActorRefRoutee)
                    .Select(c => FullAddress(((ActorRefRoutee)c).Actor)).ToList();

                routees().ForEach(actorRef =>
                {
                    var actorRefRoutee = actorRef as ActorRefRoutee;
                    if (actorRefRoutee != null)
                    {
                        Watch(actorRefRoutee.Actor);
                    }
                });

                var notUsedAddress = Roles.Select(c => GetAddress(c)).Except(routeeAddresses()).First();
                var downAddress = routeeAddresses().Find(c => c != GetAddress(_config.First));
                var downRouteeRef = routees()
                    .Where(c => c is ActorRefRoutee && ((ActorRefRoutee)c).Actor.Path.Address == downAddress)
                    .Select(c => ((ActorRefRoutee)c).Actor).First();

                Cluster.Down(downAddress);
                ExpectMsg<Terminated>(15.Seconds()).ActorRef.Should().Be(downRouteeRef);
                AwaitAssert(() =>
                {
                    routeeAddresses().Should().Contain(notUsedAddress);
                    routeeAddresses().Should().NotContain(downAddress);
                });

                var iterationCount = 10;
                for (var i = 0; i < iterationCount; i++)
                {
                    router2.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterRoundRobinSpecConfig.PoolRoutee(), iterationCount);
                routeeAddresses().Count.Should().Be(3);
                replays.Values.Sum().Should().Be(iterationCount);

            }, _config.First);
            EnterBarrier("after-10");
        }
    }
}
