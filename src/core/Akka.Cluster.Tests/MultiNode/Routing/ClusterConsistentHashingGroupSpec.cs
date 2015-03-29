using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.TestKit;

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
                if (message is Get) Sender.Tell(new Collected(_receivedMessages));
                else
                {
                    _receivedMessages.Add(message);
                }
            }
        }

        #endregion

        private readonly RoleName _first;
        public RoleName First { get { return _first; } }

        private readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        private readonly RoleName _third;

        public RoleName Third { get { return _third; } }

        public ClusterConsistentHashingGroupSpecConfig()
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");

            CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(false))
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.cluster.publish-stats-interval = 5s
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterConsistentHashingGroupMultiNode1 : ClusterConsistentHashingGroupSpec { }
    public class ClusterConsistentHashingGroupMultiNode2 : ClusterConsistentHashingGroupSpec { }
    public class ClusterConsistentHashingGroupMultiNode3 : ClusterConsistentHashingGroupSpec { }


    public abstract class ClusterConsistentHashingGroupSpec : MultiNodeClusterSpec
    {
        private readonly ClusterConsistentHashingGroupSpecConfig _config;

        protected ClusterConsistentHashingGroupSpec()
            : this(new ClusterConsistentHashingGroupSpecConfig())
        {

        }

        protected ClusterConsistentHashingGroupSpec(ClusterConsistentHashingGroupSpecConfig config)
            : base(config)
        {
            _config = config;
        }

        protected Routees CurrentRoutees(ActorRef router)
        {
            var routerAsk = router.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            return routerAsk.Result;
        }

        /// <summary>
        /// Fills in the self address for local ActorRef
        /// </summary>
        protected Address FullAddress(ActorRef actorRef)
        {
            if (string.IsNullOrEmpty(actorRef.Path.Address.Host) || !actorRef.Path.Address.Port.HasValue)
                return Cluster.SelfAddress;
            return actorRef.Path.Address;
        }

        [MultiNodeFact]
        public void AClusterRouterWithConsistentHashingGroupMustSendToSameDestinationsFromDifferentNodes()
        {
            Sys.ActorOf(Props.Create<ClusterConsistentHashingGroupSpecConfig.Destination>(), "dest");
            AwaitClusterUp(_config.First, _config.Second, _config.Third);
            EnterBarrier("after-1");

            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is string) return msg;
                return null;
            };

            var paths = new List<string>() { "/user/dest" };
            var router =
                Sys.ActorOf(
                    new ClusterRouterGroup(new ConsistentHashingGroup(paths).WithHashMapping(hashMapping),
                        new ClusterRouterGroupSettings(10, true, null, ImmutableHashSet.Create<string>(paths.ToArray())))
                        .Props(), "router");

            // it may take some time until router receives cluster member events
            AwaitAssert(() =>
            {
                var members = CurrentRoutees(router).Members;
                members.Count().ShouldBe(3);
            });
            var keys = new[] { "A", "B", "C", "D", "E", "F", "G" };
            foreach (var key in Enumerable.Range(1, 10).SelectMany(i => keys))
            {
                router.Tell(key, TestActor);
            }
            EnterBarrier("messages-sent");
            router.Tell(new Broadcast(new Get()));
            var a = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;
            var b = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;
            var c = ExpectMsg<ClusterConsistentHashingGroupSpecConfig.Collected>().Messages;

            a.Intersect(b).Count().ShouldBe(0);
            a.Intersect(c).Count().ShouldBe(0);
            b.Intersect(c).Count().ShouldBe(0);

            (a.Count + b.Count + c.Count).ShouldBe(keys.Length);
            EnterBarrier("after-2");
        }
    }
}
