//-----------------------------------------------------------------------
// <copyright file="SplitBrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Cluster.Tests.MultiNode
{
    public class SplitBrainConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }
        public RoleName Third { get; set; }
        public RoleName Fourth { get; set; }
        public RoleName Fifth { get; set; }

        public SplitBrainConfig(bool failureDetectorPuppet)
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.remote.retry-gate-closed-for = 3s
                    akka.cluster.auto-down-unreachable-after = 1s
                    akka.cluster.failure-detector.threshold = 4
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));

            TestTransport = true;
        }
    }

    public class SplitBrainWithFailureDetectorPuppetMultiNode1 : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode1() : base(true)
        {
        }
    }

    public class SplitBrainWithFailureDetectorPuppetMultiNode2 : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode2() : base(true)
        {
        }
    }

    public class SplitBrainWithFailureDetectorPuppetMultiNode3 : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode3() : base(true)
        {
        }
    }

    public class SplitBrainWithFailureDetectorPuppetMultiNode4 : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode4() : base(true)
        {
        }
    }

    public class SplitBrainWithFailureDetectorPuppetMultiNode5 : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode5() : base(true)
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode1 : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode1() : base(false)
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode2 : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode2() : base(false)
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode3 : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode3() : base(false)
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode4 : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode4() : base(false)
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode5 : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode5() : base(false)
        {
        }
    }

    public abstract class SplitBrainSpec : MultiNodeClusterSpec
    {
        private readonly SplitBrainConfig _config;
        private List<RoleName> side1;
        private List<RoleName> side2;

        protected SplitBrainSpec(bool failureDetectorPuppet) : this(new SplitBrainConfig(failureDetectorPuppet))
        {
        }

        protected SplitBrainSpec(SplitBrainConfig config) : base(config)
        {
            _config = config;
            side1 = new List<RoleName> { _config.First, _config.Second };
            side2 = new List<RoleName> { _config.Third, _config.Fourth, _config.Fifth };
        }

        [MultiNodeFact]
        public void SplitBrainSpecs()
        {
            Cluster_of_5_members_must_reach_initial_convergence();
            Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster();
        }

        public void Cluster_of_5_members_must_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);

            EnterBarrier("after-1");
        }

        public void Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster()
        {
            EnterBarrier("before-split");

            RunOn(() =>
            {
                // split the cluster in two parts (first, second) / (third, fourth, fifth)
                foreach (var role1 in side1)
                {
                    foreach (var role2 in side2)
                    {
                        TestConductor.Blackhole(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }
            }, _config.First);
            EnterBarrier("after-split");

            RunOn(() =>
            {
                foreach (var role in side2)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }

                // auto-down
                AwaitMembersUp(side1.Count, side2.Select(r => GetAddress(r)).ToImmutableHashSet());
                AssertLeader(side1.ToArray());
            }, side1.ToArray());

            RunOn(() =>
            {
                foreach (var role in side1)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }

                // auto-down
                AwaitMembersUp(side2.Count, side1.Select(r => GetAddress(r)).ToImmutableHashSet());
                AssertLeader(side2.ToArray());
            }, side2.ToArray());

            EnterBarrier("after-2");
        }
    }
}
