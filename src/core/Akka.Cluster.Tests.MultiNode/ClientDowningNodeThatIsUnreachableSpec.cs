//-----------------------------------------------------------------------
// <copyright file="ClientDowningNodeThatIsUnreachableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.MultiNode;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode;

public class ClientDowningNodeThatIsUnreachableMultiNodeConfig : MultiNodeConfig
{
    public RoleName First { get; }

    public RoleName Second { get; }

    public RoleName Third { get; }

    public RoleName Fourth { get; }

    public ClientDowningNodeThatIsUnreachableMultiNodeConfig(bool failureDetectorPuppet)
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");
        Fourth = Role("fourth");

        CommonConfig= DebugConfig(false).WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
    }
}

public class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode : ClientDowningNodeThatIsUnreachableSpec
{
    public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode()
        : base(true, typeof(ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode))
    {
    }
}


public class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode : ClientDowningNodeThatIsUnreachableSpec
{
    public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode()
        : base(false, typeof(ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode))
    {
    }
}

public abstract class ClientDowningNodeThatIsUnreachableSpec : MultiNodeClusterSpec
{
    private readonly ClientDowningNodeThatIsUnreachableMultiNodeConfig _config;

    protected ClientDowningNodeThatIsUnreachableSpec(bool failureDetectorPuppet, Type type)
        : this(new ClientDowningNodeThatIsUnreachableMultiNodeConfig(failureDetectorPuppet), type)
    {
    }

    protected ClientDowningNodeThatIsUnreachableSpec(ClientDowningNodeThatIsUnreachableMultiNodeConfig config, Type type)
        : base(config, type)
    {
        _config = config;
    }

    [MultiNodeFact]
    public async Task Client_of_a_4_node_cluster_must_be_able_to_DOWN_a_node_that_is_UNREACHABLE()
    {
        var thirdAddress = GetAddress(_config.Third);
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third, _config.Fourth);

        await RunOnAsync(async () =>
        {
            // kill 'third' node
            await TestConductor.ExitAsync(_config.Third, 0);
            MarkNodeAsUnavailable(thirdAddress);

            // mark 'third' node as DOWN
            Cluster.Down(thirdAddress);
            await EnterBarrierAsync("down-third-node");

            await AwaitMembersUpAsync(3, ImmutableHashSet.Create(thirdAddress));
            ClusterView.Members.Any(x => x.Address == thirdAddress).ShouldBeFalse();
        }, _config.First);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-third-node");
        }, _config.Third);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("down-third-node");

            await AwaitMembersUpAsync(3, ImmutableHashSet.Create(thirdAddress));
        }, _config.Second, _config.Fourth);

        await EnterBarrierAsync("await-completion");
    }
}