using System;
using System.Threading;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class JoinInProgressMultiNodeSpec : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get {return _first;} }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        public JoinInProgressMultiNodeSpec()
        {
            _first = Role("first");
            _second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(
                ConfigurationFactory.ParseString(@"
                    akka.stdout-loglevel = DEBUG
                    akka.cluster {
                        # simulate delay in gossip by turning it off
                        gossip-interval = 300 s
                        failure-detector {
                            threshold = 4
                            acceptable-heartbeat-pause = 1 second
                        }
                    }").WithFallback(MultiNodeClusterSpec.ClusterConfig()));
        }
    }

    public class JoinInProgressMultiNode1 : JoinInProgressSpec
    {
    }

    public class JoinInProgressMultiNode2 : JoinInProgressSpec
    {
    }

    public abstract class JoinInProgressSpec : MultiNodeClusterSpec
    {
        readonly JoinInProgressMultiNodeSpec _config;

        protected JoinInProgressSpec() : this(new JoinInProgressMultiNodeSpec())
        {
        }

        private JoinInProgressSpec(JoinInProgressMultiNodeSpec spec) : base(spec)
        {
            _config = spec;
        }

        [MultiNodeFact]
        public void AClusterNodeMustSendHeartbeatsImmediatelyWhenJoiningToAvoidFalseFailureDetectionDueToDelayedGossip()
        {
            RunOn(StartClusterNode, _config.First);

            EnterBarrier("first-started");

            RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Second);

            RunOn(() =>
            {
                var until = Deadline.Now + TimeSpan.FromSeconds(5);
                while (!until.IsOverdue)
                {
                    Thread.Sleep(200);
                    Assert.True(Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)));
                }
            }, _config.First);

            EnterBarrier("after");
        }
    }
}
