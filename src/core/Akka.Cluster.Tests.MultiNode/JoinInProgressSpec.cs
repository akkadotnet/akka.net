﻿// -----------------------------------------------------------------------
//  <copyright file="JoinInProgressSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode;

public class JoinInProgressMultiNodeConfig : MultiNodeConfig
{
    public JoinInProgressMultiNodeConfig()
    {
        First = Role("first");
        Second = Role("second");

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

    public RoleName First { get; }

    public RoleName Second { get; }
}

public class JoinInProgressSpec : MultiNodeClusterSpec
{
    private readonly JoinInProgressMultiNodeConfig _config;

    public JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
    {
    }

    private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config, typeof(JoinInProgressSpec))
    {
        _config = config;
    }

    [MultiNodeFact]
    public void
        A_cluster_node_must_send_heartbeats_immediately_when_joining_to_avoid_false_failure_detection_due_to_delayed_gossip()
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