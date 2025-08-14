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
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Cluster.Tests.MultiNode;

public class SplitBrainConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }
    public RoleName Fifth { get; }

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
    private readonly List<RoleName> _side1;
    private readonly List<RoleName> _side2;

    protected SplitBrainSpec(bool failureDetectorPuppet, Type type) : this(new SplitBrainConfig(failureDetectorPuppet), type)
    {
    }

    protected SplitBrainSpec(SplitBrainConfig config, Type type) : base(config, type)
    {
        _config = config;
        _side1 = new List<RoleName> { _config.First, _config.Second };
        _side2 = new List<RoleName> { _config.Third, _config.Fourth, _config.Fifth };
    }

    [MultiNodeFact]
    public async Task SplitBrainSpecs()
    {
        await Cluster_of_5_members_must_reach_initial_convergence();
        await Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster();
    }

    public async Task Cluster_of_5_members_must_reach_initial_convergence()
    {
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth);

        await EnterBarrierAsync("after-1");
    }

    public async Task Cluster_of_5_members_must_detect_network_partition_and_mark_nodes_on_other_side_as_unreachable_and_form_new_cluster()
    {
        await EnterBarrierAsync("before-split");

        await RunOnAsync(async () =>
        {
            // split the cluster in two parts (first, second) / (third, fourth, fifth)
            foreach (var role1 in _side1)
            {
                foreach (var role2 in _side2)
                {
                    await TestConductor.BlackholeAsync(role1, role2, ThrottleTransportAdapter.Direction.Both);
                }
            }
        }, _config.First);
        await EnterBarrierAsync("after-split");

        await RunOnAsync(async () =>
        {
            foreach (var role in _side2)
            {
                MarkNodeAsUnavailable(GetAddress(role));
            }

            // auto-down
            await AwaitMembersUpAsync(_side1.Count, _side2.Select(GetAddress).ToImmutableHashSet());
            AssertLeader(_side1.ToArray());
        }, _side1.ToArray());

        await RunOnAsync(async () =>
        {
            foreach (var role in _side1)
            {
                MarkNodeAsUnavailable(GetAddress(role));
            }

            // auto-down
            await AwaitMembersUpAsync(_side2.Count, _side1.Select(GetAddress).ToImmutableHashSet());
            AssertLeader(_side2.ToArray());
        }, _side2.ToArray());

        await EnterBarrierAsync("after-2");
    }
}