// -----------------------------------------------------------------------
//  <copyright file="DownUnreachableIndirectlyConnected15NodeSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode.SBR;

    public class DownUnreachableIndirectlyConnected15NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }
        public RoleName Node4 { get; }
        public RoleName Node5 { get; }
        public RoleName Node6 { get; }
        public RoleName Node7 { get; }
        public RoleName Node8 { get; }
        public RoleName Node9 { get; }
        public RoleName Node10 { get; }
        public RoleName Node11 { get; }
        public RoleName Node12 { get; }
        public RoleName Node13 { get; }
        public RoleName Node14 { get; }
        public RoleName Node15 { get; }

        public RoleName[] Nodes { get; }

        public DownUnreachableIndirectlyConnected15NodeSpecConfig()
        {
            Node1 = Role("node1");
            Node2 = Role("node2");
            Node3 = Role("node3");
            Node4 = Role("node4");
            Node5 = Role("node5");
            Node6 = Role("node6");
            Node7 = Role("node7");
            Node8 = Role("node8");
            Node9 = Role("node9");
            Node10 = Role("node10");
            Node11 = Role("node11");
            Node12 = Role("node12");
            Node13 = Role("node13");
            Node14 = Role("node14");
            Node15 = Role("node15");

            Nodes = new[]
            {
                Node1, Node2, Node3, Node4, Node5, 
                Node6, Node7, Node8, Node9, Node10, 
                Node11, Node12, Node13, Node14, Node15
            };

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = DEBUG
                    cluster {
                        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                        failure-detector.acceptable-heartbeat-pause = 3s
                        split-brain-resolver.active-strategy = keep-majority
                        split-brain-resolver.stable-after = 10s
                        split-brain-resolver.down-all-when-unstable = off

                        run-coordinated-shutdown-when-down = off
                        log-info-verbose = on

                        debug {
                          # log heartbeat events (very verbose, useful mostly when debugging heartbeating issues)
                          verbose-heartbeat-logging = off

	                      # log gossip merge events (very verbose, useful when debugging convergence issues)
	                      verbose-receive-gossip-logging = on
                        }
                    }

                    actor.provider = cluster

                    test.filter-leeway = 10s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class DownUnreachableIndirectlyConnected15NodeSpec : MultiNodeClusterSpec
    {
        private readonly DownUnreachableIndirectlyConnected15NodeSpecConfig _config;

        public DownUnreachableIndirectlyConnected15NodeSpec()
            : this(new DownUnreachableIndirectlyConnected15NodeSpecConfig())
        {
        }

        protected DownUnreachableIndirectlyConnected15NodeSpec(DownUnreachableIndirectlyConnected15NodeSpecConfig config)
            : base(config, typeof(DownUnreachableIndirectlyConnected15NodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public Task DownAllUnstable5NodeSpecTests()
        {
            return A_5_node_cluster_with_down_all_when_unstable_should_down_all_when_instability_continues();
        }

        public async Task A_5_node_cluster_with_down_all_when_unstable_should_down_all_when_instability_continues()
        {
            var cluster = Cluster.Get(Sys);

            RunOn(() =>
            {
                cluster.Join(cluster.SelfAddress);
            }, _config.Node1);
            EnterBarrier("node1 joined");
            
            RunOn(() =>
            {
                cluster.Join(Node(_config.Node1).Address);
            }, 
                _config.Node2, 
                _config.Node3,
                _config.Node4,
                _config.Node5,
                _config.Node6,
                _config.Node7,
                _config.Node8,
                _config.Node9,
                _config.Node10,
                _config.Node11,
                _config.Node12,
                _config.Node13,
                _config.Node14,
                _config.Node15);
            
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    cluster.State.Members.Count.Should().Be(15);
                    foreach (var m in cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
            });

            EnterBarrier("Cluster formed");

            await RunOnAsync(async () =>
            {
                var rnd = new Random();

                var tasks = new List<Task>();
                foreach (var i in Enumerable.Range(4, 5))
                {
                    tasks.Add(TestConductor.Exit(_config.Nodes[i], 0));
                    await Task.Delay(TimeSpan.FromMilliseconds(1300 + rnd.Next(0, 700)));
                }
                await Task.WhenAll(tasks);
                
            }, _config.Node1);
            
            EnterBarrier("long-delay-process-kill");

            // stable-after delay
            await Task.Delay(10.Seconds());
            
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    cluster.State.Members.Count.Should().Be(10);
                    foreach (var m in cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
                LogClusterInfos(cluster);
            });
            EnterBarrier("unreachable-indirect-clean-SBR-decision");

            EnterBarrier("done");
        }
        
        private void LogClusterInfos(Cluster cluster)
        {
            var state = cluster.State.Copy();

            var sb = new StringBuilder()
                .AppendLine("============")
                .AppendLine($"Cluster Leader : {state.Leader}")
                .AppendLine("All Members :");
                
            foreach (var member in state.Members)
            {
                sb.AppendLine($"{member.Address} | Status : {member.Status}. (Unreachable: {state.Unreachable.Contains(member)})");
            }

            sb.AppendLine("============");
            Sys.Log.Info(sb.ToString());
        }
    }
