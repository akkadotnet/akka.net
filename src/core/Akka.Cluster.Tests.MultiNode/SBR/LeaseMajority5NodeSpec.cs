//-----------------------------------------------------------------------
// <copyright file="LeaseMajority5NodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Coordination.Tests;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute; 

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class LeaseMajority5NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }
        public RoleName Node4 { get; }
        public RoleName Node5 { get; }


        public LeaseMajority5NodeSpecConfig()
        {
            Node1 = Role("node1");
            Node2 = Role("node2");
            Node3 = Role("node3");
            Node4 = Role("node4");
            Node5 = Role("node5");

            CommonConfig = ConfigurationFactory.ParseString(@"

                akka {
                    loglevel = INFO
                    cluster {
                        gossip-interval                     = 200 ms
                        leader-actions-interval             = 200 ms
                        periodic-tasks-initial-delay        = 300 ms
                        failure-detector.heartbeat-interval = 500 ms

                        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                        split-brain-resolver {
                            active-strategy = lease-majority
                            stable-after = 1.5s
                            lease-majority {
                                lease-implementation = test-lease
                                acquire-lease-delay-for-minority = 1s
                                release-after = 3s
                            }
                        }

                        run-coordinated-shutdown-when-down = off
                    }

                    actor.provider = cluster

                    test.filter-leeway = 10s
                }

                test-lease {
                    lease-class = ""Akka.Coordination.Tests.TestLease, Akka.Coordination.Tests""
                    heartbeat-interval = 1s
                    heartbeat-timeout = 120s
                    lease-operation-timeout = 3s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class LeaseMajority5NodeSpec : MultiNodeClusterSpec
    {
        private readonly LeaseMajority5NodeSpecConfig _config;
        private const string testLeaseName = "LeaseMajority5NodeSpec-akka-sbr";

        public LeaseMajority5NodeSpec()
            : this(new LeaseMajority5NodeSpecConfig())
        {
        }

        protected LeaseMajority5NodeSpec(LeaseMajority5NodeSpecConfig config)
            : base(config, typeof(LeaseMajority5NodeSpec))
        {
            _config = config;
        }

        /// <summary>
        /// Sort the roles in the address order used by the cluster node ring.
        /// </summary>
        private class ClusterOrdering : IComparer<RoleName>
        {
            private readonly Func<RoleName, ActorPath> _node;

            public ClusterOrdering(Func<RoleName, ActorPath> node)
            {
                _node = node;
            }

            public int Compare(RoleName x, RoleName y)
            {
                return Member.AddressOrdering.Compare(_node(x).Address, _node(y).Address);
            }
        }

        List<RoleName> SortByAddress(RoleName[] roles)
        {
            return roles.OrderBy(r => Node(r).Address, Member.AddressOrdering).ToList();
        }

        RoleName Leader(params RoleName[] roles) => SortByAddress(roles).First();


        [MultiNodeFact]
        public void LeaseMajority5NodeSpecTests()
        {
            LeaseMajority_in_a_5_node_cluster_should_setup_cluster();
            LeaseMajority_in_a_5_node_cluster_should_keep_the_side_that_can_acquire_the_lease();
            LeaseMajority_in_a_5_node_cluster_should_keep_the_side_that_can_acquire_the_lease_round_2();
        }

        public void LeaseMajority_in_a_5_node_cluster_should_setup_cluster()
        {
            RunOn(() =>
            {
                Cluster.Join(Cluster.SelfAddress);
            }, _config.Node1);
            EnterBarrier("node1 joined");
            RunOn(() =>
            {
                Cluster.Join(Node(_config.Node1).Address);
            }, _config.Node2, _config.Node3, _config.Node4, _config.Node5);
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count.Should().Be(5);
                    foreach (var m in Cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
            });
            EnterBarrier("Cluster formed");
        }

        public void LeaseMajority_in_a_5_node_cluster_should_keep_the_side_that_can_acquire_the_lease()
        {
            var lease = TestLeaseExt.Get(Sys).GetTestLease(testLeaseName);
            var leaseProbe = lease.Probe;

            RunOn(() =>
            {
                lease.SetNextAcquireResult(Task.FromResult(true));
            }, _config.Node1, _config.Node2, _config.Node3);
            RunOn(() =>
            {
                lease.SetNextAcquireResult(Task.FromResult(false));
            }, _config.Node4, _config.Node5);
            EnterBarrier("lease-in-place");
            RunOn(() =>
            {
                foreach (var x in new[] { _config.Node1, _config.Node2, _config.Node3 })
                {
                    foreach (var y in new[] { _config.Node4, _config.Node5 })
                    {
                        TestConductor.Blackhole(x, y, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }
            }, _config.Node1);
            EnterBarrier("blackholed-clean-partition");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count.Should().Be(3);
                    });
                });
                RunOn(() =>
                {
                    leaseProbe.ExpectMsg<TestLease.AcquireReq>();
                    // after 2 * stable-after
                    leaseProbe.ExpectMsg<TestLease.ReleaseReq>(TimeSpan.FromSeconds(14));
                }, Leader(_config.Node1, _config.Node2, _config.Node3));
            }, _config.Node1, _config.Node2, _config.Node3);
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.IsTerminated.Should().BeTrue();
                    });
                    RunOn(() =>
                    {
                        leaseProbe.ExpectMsg<TestLease.AcquireReq>();
                    }, Leader(_config.Node4, _config.Node5));
                });
            }, _config.Node4, _config.Node5);
            EnterBarrier("downed-and-removed");
            leaseProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));

            EnterBarrier("done-1");
        }

        public void LeaseMajority_in_a_5_node_cluster_should_keep_the_side_that_can_acquire_the_lease_round_2()
        {
            var lease = TestLeaseExt.Get(Sys).GetTestLease(testLeaseName);

            RunOn(() =>
            {
                lease.SetNextAcquireResult(Task.FromResult(true));
            }, _config.Node1);
            RunOn(() =>
            {
                lease.SetNextAcquireResult(Task.FromResult(false));
            }, _config.Node2, _config.Node3);
            EnterBarrier("lease-in-place-2");
            RunOn(() =>
            {
                foreach (var x in new[] { _config.Node1 })
                {
                    foreach (var y in new[] { _config.Node2, _config.Node3 })
                    {
                        TestConductor.Blackhole(x, y, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }
            }, _config.Node1);
            EnterBarrier("blackholed-clean-partition-2");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count.Should().Be(1);
                    });
                });
            }, _config.Node1);
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.IsTerminated.Should().BeTrue();
                    });
                });
            }, _config.Node2, _config.Node3);

            EnterBarrier("done-2");
        }
    }
}
