//-----------------------------------------------------------------------
// <copyright file="UseRoleIgnoredSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Routing;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class UseRoleIgnoredSpecConfig : MultiNodeConfig
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
            private ILoggingAdapter log = Context.GetLogger();

            public SomeActor() : this(new PoolRoutee())
            {

            }

            public SomeActor(IRouteeType routeeType)
            {
                _routeeType = routeeType;
                log.Info("Starting on {0}", Self.Path.Address);

                Receive<string>(msg =>
                {
                    log.Info("msg = {0}", msg);
                    Sender.Tell(new Reply(_routeeType, Self));
                });
            }
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public UseRoleIgnoredSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new List<RoleName> { First }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""a"", ""c""]") });
            NodeConfig(new List<RoleName> { Second, Third }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""b"", ""c""]") });
        }
    }

    public class UseRoleIgnoredSpec : MultiNodeClusterSpec
    {
        private readonly UseRoleIgnoredSpecConfig _config;

        public UseRoleIgnoredSpec() : this(new UseRoleIgnoredSpecConfig())
        {
        }

        protected UseRoleIgnoredSpec(UseRoleIgnoredSpecConfig config)
            : base(config, typeof(UseRoleIgnoredSpec))
        {
            _config = config;
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

        private Dictionary<Address, int> ReceiveReplays(UseRoleIgnoredSpecConfig.IRouteeType routeeType,
            int expectedReplies)
        {
            var zero = Roles.Select(c => GetAddress(c)).ToDictionary(c => c, c => 0);
            var replays = ReceiveWhile(5.Seconds(), msg =>
            {
                var routee = msg as UseRoleIgnoredSpecConfig.Reply;
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
        public void UseRoleIgnoredSpecs()
        {
            A_cluster_must_start_cluster();
            A_cluster_must_pool_local_off_roles_off();
            A_cluster_must_group_local_off_roles_off();
            A_cluster_must_pool_local_on_role_b();
            A_cluster_must_group_local_on_role_b();
            A_cluster_must_pool_local_on_role_a();
            A_cluster_must_group_local_on_role_a();
            A_cluster_must_pool_local_on_role_c();
            A_cluster_must_group_local_on_role_c();
        }

        private void A_cluster_must_start_cluster()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                Log.Info("first, roles: " + Cluster.SelfRoles);
            }, _config.First);

            RunOn(() =>
            {
                Log.Info("second, roles: " + Cluster.SelfRoles);
            }, _config.Second);

            RunOn(() =>
            {
                Log.Info("third, roles: " + Cluster.SelfRoles);
            }, _config.Third);

            // routees for the group routers
            Sys.ActorOf(Props.Create(() => new UseRoleIgnoredSpecConfig.SomeActor(new UseRoleIgnoredSpecConfig.GroupRoutee())), "foo");
            Sys.ActorOf(Props.Create(() => new UseRoleIgnoredSpecConfig.SomeActor(new UseRoleIgnoredSpecConfig.GroupRoutee())), "bar");

            EnterBarrier("after-1");
        }

        private void A_cluster_must_pool_local_off_roles_off()
        {
            RunOn(() =>
            {
                var role = "b";

                var router = Sys.ActorOf(
                    new ClusterRouterPool(
                        new RoundRobinPool(6),
                        new ClusterRouterPoolSettings(6, 2, allowLocalRoutees: false, useRole: role)).Props(
                            Props.Create<UseRoleIgnoredSpecConfig.SomeActor>()),
                    "router-2");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(4));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.PoolRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().Be(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-2");
        }

        private void A_cluster_must_group_local_off_roles_off()
        {
            RunOn(() =>
            {
                var role = "b";

                var router = Sys.ActorOf(
                    new ClusterRouterGroup(
                        new RoundRobinGroup(paths: null),
                        new ClusterRouterGroupSettings(6, ImmutableHashSet.Create("/user/foo", "/user/bar"), allowLocalRoutees: false, useRole: role)).Props(),
                    "router-2b");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(4));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.GroupRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().Be(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-2b");
        }

        private void A_cluster_must_pool_local_on_role_b()
        {
            RunOn(() =>
            {
                var role = "b";

                var router = Sys.ActorOf(
                    new ClusterRouterPool(
                        new RoundRobinPool(6),
                        new ClusterRouterPoolSettings(6, 2, allowLocalRoutees: true, useRole: role)).Props(
                            Props.Create<UseRoleIgnoredSpecConfig.SomeActor>()),
                    "router-3");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(4));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.PoolRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().Be(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-3");
        }

        private void A_cluster_must_group_local_on_role_b()
        {
            RunOn(() =>
            {
                var role = "b";

                var router = Sys.ActorOf(
                    new ClusterRouterGroup(
                        new RoundRobinGroup(paths: null),
                        new ClusterRouterGroupSettings(6, ImmutableHashSet.Create("/user/foo", "/user/bar"), allowLocalRoutees: true, useRole: role)).Props(),
                    "router-3b");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(4));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.GroupRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().Be(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-3b");
        }

        private void A_cluster_must_pool_local_on_role_a()
        {
            RunOn(() =>
            {
                var role = "a";

                var router = Sys.ActorOf(
                    new ClusterRouterPool(
                        new RoundRobinPool(6),
                        new ClusterRouterPoolSettings(6, 2, allowLocalRoutees: true, useRole: role)).Props(
                            Props.Create<UseRoleIgnoredSpecConfig.SomeActor>()),
                    "router-4");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(2));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.PoolRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().Be(0);
                replays[GetAddress(_config.Third)].Should().Be(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-4");
        }

        private void A_cluster_must_group_local_on_role_a()
        {
            RunOn(() =>
            {
                var role = "a";

                var router = Sys.ActorOf(
                    new ClusterRouterGroup(
                        new RoundRobinGroup(paths: null),
                        new ClusterRouterGroupSettings(6, ImmutableHashSet.Create("/user/foo", "/user/bar"), allowLocalRoutees: true, useRole: role)).Props(),
                    "router-4b");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(2));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.GroupRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().Be(0);
                replays[GetAddress(_config.Third)].Should().Be(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-4b");
        }

        private void A_cluster_must_pool_local_on_role_c()
        {
            RunOn(() =>
            {
                var role = "c";

                var router = Sys.ActorOf(
                    new ClusterRouterPool(
                        new RoundRobinPool(6),
                        new ClusterRouterPoolSettings(6, 2, allowLocalRoutees: true, useRole: role)).Props(
                            Props.Create<UseRoleIgnoredSpecConfig.SomeActor>()),
                    "router-5");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(6));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.PoolRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-5");
        }

        private void A_cluster_must_group_local_on_role_c()
        {
            RunOn(() =>
            {
                var role = "c";

                var router = Sys.ActorOf(
                    new ClusterRouterGroup(
                        new RoundRobinGroup(paths: null),
                        new ClusterRouterGroupSettings(6, ImmutableHashSet.Create("/user/foo", "/user/bar"), allowLocalRoutees: true, useRole: role)).Props(),
                    "router-5b");

                AwaitAssert(() => CurrentRoutees(router).Count().Should().Be(6));

                var iterationCount = 10;
                for (int i = 0; i < iterationCount; i++)
                {
                    router.Tell($"hit-{i}");
                }

                var replays = ReceiveReplays(new UseRoleIgnoredSpecConfig.GroupRoutee(), iterationCount);

                // should not be deployed locally, does not have required role
                replays[GetAddress(_config.First)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Second)].Should().BeGreaterThan(0);
                replays[GetAddress(_config.Third)].Should().BeGreaterThan(0);
                replays.Values.Sum().Should().Be(iterationCount);
            }, _config.First);

            EnterBarrier("after-5b");
        }
    }
}

