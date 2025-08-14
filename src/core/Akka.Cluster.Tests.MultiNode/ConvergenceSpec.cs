//-----------------------------------------------------------------------
// <copyright file="ConvergenceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode;

public class ConvergenceSpecConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }

    public ConvergenceSpecConfig(bool failureDetectorPuppet)
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");
        Fourth = Role("fourth");

        CommonConfig = ConfigurationFactory.ParseString(@"akka.cluster.publish-stats-interval = 25s")
            .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
            .WithFallback(DebugConfig(true))
            .WithFallback(@"
                    akka.cluster.failure-detector.threshold = 4
                    akka.cluster.allow-weakly-up-members = off")
            .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
    }
}
    
public class ConvergenceWithFailureDetectorPuppetMultiNode : ConvergenceSpec
{
    public ConvergenceWithFailureDetectorPuppetMultiNode() : base(true, typeof(ConvergenceWithFailureDetectorPuppetMultiNode))
    {
    }
}

public class ConvergenceWithAccrualFailureDetectorMultiNode : ConvergenceSpec
{
    public ConvergenceWithAccrualFailureDetectorMultiNode()
        : base(false, typeof(ConvergenceWithAccrualFailureDetectorMultiNode))
    {
    }
}
    
public abstract class ConvergenceSpec : MultiNodeClusterSpec
{
    private readonly ConvergenceSpecConfig _config;

    protected ConvergenceSpec(bool failureDetectorPuppet, Type type)
        : this(new ConvergenceSpecConfig(failureDetectorPuppet), type)
    {
    }

    private ConvergenceSpec(ConvergenceSpecConfig config, Type type) : base(config, type)
    {
        _config = config;
        MuteMarkingAsUnreachable();
    }

    [MultiNodeFact]
    public async Task ConvergenceSpecTests()
    {
        //TODO: This better
        await A_cluster_of_3_members_must_reach_initial_convergence();
        await A_cluster_of_3_members_must_not_reach_convergence_while_any_nodes_are_unreachable();
        await A_cluster_of_3_members_must_not_move_a_new_joining_node_to_up_while_there_is_no_convergence();
    }

    public async Task A_cluster_of_3_members_must_reach_initial_convergence()
    {
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third);

        RunOn(() => { /*doesn't join immediately*/}, _config.Fourth);

        await EnterBarrierAsync("after-1");
    }

    public async Task A_cluster_of_3_members_must_not_reach_convergence_while_any_nodes_are_unreachable()
    {
        var thirdAddress = GetAddress(_config.Third);
        await EnterBarrierAsync("before-shutdown");

        await RunOnAsync(async () =>
        {
            //kill 'third' node
            await TestConductor.ExitAsync(_config.Third, 0);
            MarkNodeAsUnavailable(thirdAddress);
        }, _config.First);

        await RunOnAsync(async () =>
        {
            await WithinAsync(TimeSpan.FromSeconds(28), async () =>
            {
                //third becomes unreachable
                await AwaitAssertAsync(() => ClusterView.UnreachableMembers.Count.ShouldBe(1));
                await AwaitSeenSameStateAsync(CancellationToken.None, GetAddress(_config.First), GetAddress(_config.Second));
                // still one unreachable
                ClusterView.UnreachableMembers.Count.ShouldBe(1);
                ClusterView.UnreachableMembers.First().Address.ShouldBe(thirdAddress);
                ClusterView.Members.Count.ShouldBe(3);
            });
        }, _config.First, _config.Second);

        await EnterBarrierAsync("after-2");
    }

    public async Task A_cluster_of_3_members_must_not_move_a_new_joining_node_to_up_while_there_is_no_convergence()
    {
        RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Fourth);

        await EnterBarrierAsync("after-join");

        await RunOnAsync(async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                await AwaitAssertAsync(() => ClusterView.Members.Count.ShouldBe(4));
                await AwaitSeenSameStateAsync(CancellationToken.None, GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Fourth));
                MemberStatus(GetAddress(_config.First)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                MemberStatus(GetAddress(_config.Second)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                MemberStatus(GetAddress(_config.Fourth)).ShouldBe(Akka.Cluster.MemberStatus.Joining);
                // wait and then check again
                await Task.Delay(Dilated(TimeSpan.FromSeconds(1)));
            }
        }, _config.First, _config.Second, _config.Fourth);

        await EnterBarrierAsync("after-3");
    }

    private MemberStatus? MemberStatus(Address address)
    {
        var member = ClusterView.Members.FirstOrDefault(m => m.Address == address);
        if (member == null) return null;
        return member.Status;
    }
}