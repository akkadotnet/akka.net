//-----------------------------------------------------------------------
// <copyright file="ClusterBroadcastRouter2266BugfixSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Routing;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class ClusterBroadcastGroupSpecConfig : MultiNodeConfig
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

        public ClusterBroadcastGroupSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = MultiNodeLoggingConfig.LoggingConfig
                .WithFallback(DebugConfig(true))
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.actor.deployment {
                        /router1 {
                            router = broadcast-group
                            routees.paths = [""/user/myserviceA""]
                            cluster {
                                enabled = on
                                allow-local-routees = on
                            }
                        }
                    }"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new List<RoleName> { First }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles = [""a"", ""b""]") });
            NodeConfig(new List<RoleName> { Second }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles = [""a""]") });

            TestTransport = true;
        }
    }

    /// <summary>
    /// Used to verify that https://github.com/akkadotnet/akka.net/issues/2266 is reproducible and can be fixed
    /// </summary>
    public class ClusterBroadcastGroupSpec : MultiNodeClusterSpec
    {
        private readonly ClusterBroadcastGroupSpecConfig _config;
        private readonly Lazy<IActorRef> _router;

        public ClusterBroadcastGroupSpec() : this(new ClusterBroadcastGroupSpecConfig())
        {
        }

        protected ClusterBroadcastGroupSpec(ClusterBroadcastGroupSpecConfig config) : base(config, typeof(ClusterBroadcastGroupSpec))
        {
            _config = config;

            _router = new Lazy<IActorRef>(() => Sys.ActorOf(FromConfig.Instance.Props(), "router1"));
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

        private Dictionary<Address, int> ReceiveReplays(ClusterBroadcastGroupSpecConfig.IRouteeType routeeType, int expectedReplies)
        {
            var zero = Roles.Select(GetAddress).ToDictionary(c => c, c => 0);
            var replays = ReceiveWhile(5.Seconds(), msg =>
            {
                var routee = msg as ClusterBroadcastGroupSpecConfig.Reply;
                if (routee != null && routee.RouteeType.GetType() == routeeType.GetType())
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
        public void ClusterBroadcastGroup()
        {
            A_cluster_router_with_a_BroadcastGroup_router_must_start_cluster_with_2_nodes();
            A_cluster_router_with_a_BroadcastGroup_router_must_lookup_routees_on_the_member_nodes_in_the_cluster();
        }

        private void A_cluster_router_with_a_BroadcastGroup_router_must_start_cluster_with_2_nodes()
        {
            Log.Info("Waiting for cluster up");

            AwaitClusterUp(_config.First, _config.Second);

            RunOn(() =>
            {
                Log.Info("first, roles: " + Cluster.SelfRoles);
            }, _config.First);

            RunOn(() =>
            {
                Log.Info("second, roles: " + Cluster.SelfRoles);
            }, _config.Second);

            Log.Info("Cluster Up");

            EnterBarrier("after-1");
        }

        private void A_cluster_router_with_a_BroadcastGroup_router_must_lookup_routees_on_the_member_nodes_in_the_cluster()
        {
            // cluster consists of first and second
            Sys.ActorOf(Props.Create(() => new ClusterBroadcastGroupSpecConfig.SomeActor(new ClusterBroadcastGroupSpecConfig.GroupRoutee())), "myserviceA");
            EnterBarrier("myservice-started");

            RunOn(() =>
            {
                // 2 nodes, 1 routees on each node
                Within(10.Seconds(), () =>
                {
                    AwaitAssert(() => CurrentRoutees(_router.Value).Count().Should().Be(2)); //only seems to pass with a single routee should be 2
                });

                var routeeCount = CurrentRoutees(_router.Value).Count();
                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    _router.Value.Tell("hit");
                }

                var replays = ReceiveReplays(new ClusterBroadcastGroupSpecConfig.GroupRoutee(), iterationCount*routeeCount);

                replays[GetAddress(_config.First)].Should().Be(10);
                replays[GetAddress(_config.Second)].Should().Be(10);
                replays.Values.Sum().Should().Be(iterationCount*routeeCount);
            }, _config.First);

            EnterBarrier("after-2");
        }
    }
}

