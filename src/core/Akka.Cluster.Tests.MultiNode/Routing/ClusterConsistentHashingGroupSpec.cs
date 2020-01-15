//-----------------------------------------------------------------------
// <copyright file="ClusterConsistentHashingGroupSpec.cs" company="Akka.NET Project">
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
using Akka.Remote.TestKit;
using Akka.Routing;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class ClusterConsistentHashingGroupSpecConfig : MultiNodeConfig
    {
        #region Test classes
        public sealed class Get { }

        public sealed class Collected
        {
            public Collected(HashSet<object> messages)
            {
                Messages = messages;
            }

            public HashSet<object> Messages { get; private set; }
        }

        public class Destination : UntypedActor
        {
            private readonly HashSet<object> _receivedMessages = new HashSet<object>();

            protected override void OnReceive(object message)
            {
                if (message is Get)
                {
                    Sender.Tell(new Collected(_receivedMessages));
                }
                else
                {
                    _receivedMessages.Add(message);
                }
            }
        }

        #endregion

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterConsistentHashingGroupSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterConsistentHashingGroupSpec : MultiNodeClusterSpec
    {
        private readonly ClusterConsistentHashingGroupSpecConfig _config;

        public ClusterConsistentHashingGroupSpec()
            : this(new ClusterConsistentHashingGroupSpecConfig())
        {

        }

        protected ClusterConsistentHashingGroupSpec(ClusterConsistentHashingGroupSpecConfig config)
            : base(config, typeof(ClusterConsistentHashingGroupSpec))
        {
            _config = config;
        }

        private Routees CurrentRoutees(IActorRef router)
        {
            var routerAsk = router.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            return routerAsk.Result;
        }

        [MultiNodeFact]
        public void ClusterConsistentHashingGroupSpecs()
        {
            A_cluster_router_with_consistent_hashing_group_must_start_cluster_with_3_nodes();
            A_cluster_router_with_consistent_hashing_group_must_send_to_same_destinations_from_different_nodes();
        }

        public void A_cluster_router_with_consistent_hashing_group_must_start_cluster_with_3_nodes()
        {
            Sys.ActorOf(Props.Create<ClusterConsistentHashingGroupSpecConfig.Destination>(), "dest");
            AwaitClusterUp(_config.First, _config.Second, _config.Third);
            EnterBarrier("after-1");
        }

        public void A_cluster_router_with_consistent_hashing_group_must_send_to_same_destinations_from_different_nodes()
        {
            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is string) return msg;
                return null;
            };

            var paths = new List<string> { "/user/dest" };
            var router =
                Sys.ActorOf(
                    new ClusterRouterGroup(
                        local: new ConsistentHashingGroup(paths).WithHashMapping(hashMapping),
                        settings: new ClusterRouterGroupSettings(
                            10,
                            ImmutableHashSet.Create(paths.ToArray()),
                            allowLocalRoutees: true,
                            useRole: null))
                        .Props(), "router");

            // it may take some time until router receives cluster member events
            AwaitAssert(() =>
            {
                CurrentRoutees(router).Members.Should().HaveCount(3);
            });
            var keys = new[] { "A", "B", "C", "D", "E", "F", "G" };
            foreach (var key in Enumerable.Range(1, 10).SelectMany(i => keys))
            {
                router.Tell(key, TestActor);
            }
            EnterBarrier("messages-sent");
            router.Tell(new Broadcast(new ClusterConsistentHashingGroupSpecConfig.Get()));
            var a = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;
            var b = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;
            var c = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;

            a.Intersect(b).Count().Should().Be(0);
            a.Intersect(c).Count().Should().Be(0);
            b.Intersect(c).Count().Should().Be(0);

            (a.Count + b.Count + c.Count).Should().Be(keys.Length);
            EnterBarrier("after-2");
        }
    }
}
