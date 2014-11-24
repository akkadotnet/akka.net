using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class ClusterDeathWatchSpecConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get { return _first; } }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }
        readonly RoleName _third;
        public RoleName Third { get { return _third; } }
        readonly RoleName _fourth;
        public RoleName Fourth { get { return _fourth; } }
        readonly RoleName _fifth;
        public RoleName Fifth { get { return _fifth; } }

        public ClusterDeathWatchSpecConfig()
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");
            _fourth = Role("fourth");
            _fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loggers = [""Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog""]
                akka.cluster.publish-stats-interval = 25s").WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class ClusterDeathWatchMultiNode1 : ClusterDeathWatchSpec
    {
    }

    public class ClusterDeathWatchMultiNode2 : ClusterDeathWatchSpec
    {
    }

    public class ClusterDeathWatchMultiNode3 : ClusterDeathWatchSpec
    {
    }

    public class ClusterDeathWatchMultiNode4 : ClusterDeathWatchSpec
    {
    }

    public class ClusterDeathWatchMultiNode5 : ClusterDeathWatchSpec
    {
    }

    public abstract class ClusterDeathWatchSpec : MultiNodeClusterSpec
    {
        readonly ClusterDeathWatchSpecConfig _config;

        protected ClusterDeathWatchSpec()
            : this(new ClusterDeathWatchSpecConfig())
        {
        }

        private ClusterDeathWatchSpec(ClusterDeathWatchSpecConfig config)
            : base(config)
        {
            _config = config;
        }

        protected override void AtStartup()
        {
            //TODO: Mute
        }

        [MultiNodeFact]
        public void ConvergenceSpecTests()
        {
        }

        public void AnActorWatchingARemoteActorInTheClusterReceiveTerminatedWhenWatchedNodeBecomesDownRemoved()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("cluster-up");

            RunOn(() =>
            {
                EnterBarrier("subjected-started");

                var path2 = new RootActorPath(GetAddress(_config.Second)) / "user" / "subject";
                var path3 = new RootActorPath(GetAddress(_config.Third)) / "user" / "subject";
                var watchEstablished = new TestLatch(Sys, 2);

            });
        }

        class Observer : UntypedActor
        {
            readonly Address _oldLeaderAddress;
            readonly TestLatch _latch;

            public Observer(ActorPath path2, ActorPath path3)
            {
                Context.ActorSelection(path2).Tell(new Identify(path2));
                Context.ActorSelection(path3).Tell(new Identify(path3));
            }

            protected override void OnReceive(object message)
            {
                var actorIdentity = message as ActorIdentity;
                if (actorIdentity != null)
                {
                    if (actorIdentity.MessageId == "path2")
                        Context.Watch(actorIdentity.Subject);

                }
                var memberExited = message as ClusterEvent.MemberExited;
                if (memberExited != null && memberExited.Member.Address == _oldLeaderAddress)
                    _latch.CountDown();
            }
        }
    }
}
