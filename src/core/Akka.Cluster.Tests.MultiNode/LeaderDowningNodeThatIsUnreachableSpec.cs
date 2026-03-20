//-----------------------------------------------------------------------
// <copyright file="LeaderDowningNodeThatIsUnreachableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode;

public class LeaderDowningNodeThatIsUnreachableConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }

    public LeaderDowningNodeThatIsUnreachableConfig(bool failureDetectorPuppet)
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");
        Fourth = Role("fourth");

        CommonConfig = DebugConfig(false)
            .WithFallback(ConfigurationFactory.ParseString(@"akka.cluster.auto-down-unreachable-after = 2s"))
            .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
    }
}

public class LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode : LeaderDowningNodeThatIsUnreachableSpec
{
    public LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode() : base(true, typeof(LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode))
    {
    }
}

public class LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode : LeaderDowningNodeThatIsUnreachableSpec
{
    public LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode() : base(false, typeof(LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode))
    {
    }
}

public abstract class LeaderDowningNodeThatIsUnreachableSpec : MultiNodeClusterSpec
{
    private readonly LeaderDowningNodeThatIsUnreachableConfig _config;

    protected LeaderDowningNodeThatIsUnreachableSpec(bool failureDetectorPuppet, Type type)
        : this(new LeaderDowningNodeThatIsUnreachableConfig(failureDetectorPuppet), type)
    {

    }

    protected LeaderDowningNodeThatIsUnreachableSpec(LeaderDowningNodeThatIsUnreachableConfig config, Type type)
        : base(config, type)
    {
        _config = config;
        MuteMarkingAsUnreachable();
    }

    [MultiNodeFact]
    public async Task LeaderDowningNodeThatIsUnreachableSpecs()
    {
        await Leader_in_4_node_cluster_must_be_able_to_down_last_node_that_is_unreachable();
        await Leader_in_4_node_cluster_must_be_able_to_down_middle_node_that_is_unreachable();
    }

    public async Task Leader_in_4_node_cluster_must_be_able_to_down_last_node_that_is_unreachable()
    {
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third, _config.Fourth);

        var fourthAddress = GetAddress(_config.Fourth);

        await EnterBarrierAsync("before-exit-fourth-node");
        await RunOnAsync(async () =>
        {
            // kill 'fourth' node
            await TestConductor.ExitAsync(_config.Fourth, 0);
            await EnterBarrierAsync("down-fourth-node");

            // mark the node as unreachable in the failure detector
            MarkNodeAsUnavailable(fourthAddress);

            // --- HERE THE LEADER SHOULD DETECT FAILURE AND AUTO-DOWN THE UNREACHABLE NODE ---
            await AwaitMembersUpAsync(3, ImmutableHashSet.Create(fourthAddress), 30.Seconds());
        }, _config.First);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-fourth-node");
        }, _config.Fourth);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-fourth-node");
            await AwaitMembersUpAsync(3, ImmutableHashSet.Create(fourthAddress), 30.Seconds());
        }, _config.Second, _config.Third);

        await EnterBarrierAsync("await-completion-1");
    }

    public async Task Leader_in_4_node_cluster_must_be_able_to_down_middle_node_that_is_unreachable()
    {
        var secondAddress = GetAddress(_config.Second);

        await EnterBarrierAsync("before-down-second-node");
        await RunOnAsync(async () =>
        {
            // kill 'fourth' node
            await TestConductor.ExitAsync(_config.Second, 0);
            await EnterBarrierAsync("down-second-node");

            // mark the node as unreachable in the failure detector
            MarkNodeAsUnavailable(secondAddress);

            // --- HERE THE LEADER SHOULD DETECT FAILURE AND AUTO-DOWN THE UNREACHABLE NODE ---
            await AwaitMembersUpAsync(2, ImmutableHashSet.Create(secondAddress), 30.Seconds());
        }, _config.First);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-second-node");
        }, _config.Second);

        // Note: Only run on Third since Second has already been exited
        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-second-node");
            await AwaitMembersUpAsync(2, ImmutableHashSet.Create(secondAddress), 30.Seconds());
        }, _config.Third);

        await EnterBarrierAsync("await-completion-2");

    }
}