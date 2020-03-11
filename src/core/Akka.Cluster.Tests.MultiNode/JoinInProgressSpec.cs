//-----------------------------------------------------------------------
// <copyright file="JoinInProgressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class JoinInProgressMultiNodeConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get {return _first;} }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        public JoinInProgressMultiNodeConfig()
        {
            _first = Role("first");
            _second = Role("second");

            CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(true)).WithFallback(
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

    public class JoinInProgressSpec : MultiNodeClusterSpec
    {
        readonly JoinInProgressMultiNodeConfig _config;

        public JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
        {
        }

        private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config, typeof(JoinInProgressSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void A_cluster_node_must_send_heartbeats_immediately_when_joining_to_avoid_false_failure_detection_due_to_delayed_gossip()
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

