//-----------------------------------------------------------------------
// <copyright file="SplitBrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
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

    public class SplitBrainWithFailureDetectorPuppetMultiNode : SplitBrainSpec
    {
        public SplitBrainWithFailureDetectorPuppetMultiNode() : base(true, typeof(SplitBrainWithFailureDetectorPuppetMultiNode))
        {
        }
    }

    public class SplitBrainWithAccrualFailureDetectorMultiNode : SplitBrainSpec
    {
        public SplitBrainWithAccrualFailureDetectorMultiNode() : base(false, typeof(SplitBrainWithAccrualFailureDetectorMultiNode))
        {
        }
    }

    public abstract class SplitBrainSpec : MultiNodeClusterSpec
    {
        private readonly SplitBrainConfig _config;
        private List<RoleName> side1;
        private List<RoleName> side2;

        protected SplitBrainSpec(bool failureDetectorPuppet, Type type) : this(new SplitBrainConfig(failureDetectorPuppet), type)
        {
        }

        protected SplitBrainSpec(SplitBrainConfig config, Type type) : base(config, type)
        {
            _config = config;
            side1 = new List<RoleName> { _config.First, _config.Second };
            side2 = new List<RoleName> { _config.Third, _config.Fourth, _config.Fifth };
        }

        [MultiNodeFact]
        public async Task SplitBrainSpecs()
        {
            await Cluster_of_5_members_must_reach_initial_convergence();
            await Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster();
        }

        public async Task Cluster_of_5_members_must_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);

            await EnterBarrierAsync("after-1");
        }

        public async Task Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster()
        {
            await EnterBarrierAsync("before-split");

            await RunOnAsync(async () =>
            {
                // split the cluster in two parts (first, second) / (third, fourth, fifth)
                foreach (var role1 in side1)
                {
                    foreach (var role2 in side2)
                    {
                        await TestConductor.BlackholeAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.First);
            await EnterBarrierAsync("after-split");

            await RunOnAsync(() =>
            {
                foreach (var role in side2)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }

                // auto-down
                AwaitMembersUp(side1.Count, side2.Select(r => GetAddress(r)).ToImmutableHashSet());
                AssertLeader(side1.ToArray());
                return Task.CompletedTask;
            }, side1.ToArray());

            await RunOnAsync(() =>
            {
                foreach (var role in side1)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }

                // auto-down
                AwaitMembersUp(side2.Count, side1.Select(r => GetAddress(r)).ToImmutableHashSet());
                AssertLeader(side2.ToArray());
                return Task.CompletedTask;
            }, side2.ToArray());

            await EnterBarrierAsync("after-2");
        }
    }
}
