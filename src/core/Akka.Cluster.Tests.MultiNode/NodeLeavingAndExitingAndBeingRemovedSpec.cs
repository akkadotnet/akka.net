//-----------------------------------------------------------------------
// <copyright file="NodeLeavingAndExitingAndBeingRemovedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeLeavingAndExitingAndBeingRemovedSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }
        public RoleName Third { get; set; }

        public NodeLeavingAndExitingAndBeingRemovedSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class NodeLeavingAndExitingAndBeingRemovedSpec : MultiNodeClusterSpec
    {
        private readonly NodeLeavingAndExitingAndBeingRemovedSpecConfig _config;

        public NodeLeavingAndExitingAndBeingRemovedSpec() : this(new NodeLeavingAndExitingAndBeingRemovedSpecConfig())
        {
        }

        protected NodeLeavingAndExitingAndBeingRemovedSpec(NodeLeavingAndExitingAndBeingRemovedSpecConfig config) : base(config, typeof(NodeLeavingAndExitingAndBeingRemovedSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task NodeLeavingAndExitingAndBeingRemovedSpecs()
        {
            await Node_that_is_leaving_non_singleton_cluster_eventually_set_to_removed_and_removed_from_membership_ring_and_seen_table();
        }

        public async Task Node_that_is_leaving_non_singleton_cluster_eventually_set_to_removed_and_removed_from_membership_ring_and_seen_table()
        {
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third);

            var secondAddress = GetAddress(_config.Second);

            // Increased timeout from 15s to 45s for CI variability
            await WithinAsync(45.Seconds(), async () =>
            {
                await RunOnAsync(() =>
                {
                    Cluster.Leave(secondAddress);
                    return Task.CompletedTask;
                }, _config.First);
                await EnterBarrierAsync("second-left");

                await RunOnAsync(async () =>
                {
                    await EnterBarrierAsync("second-shutdown");
                    // this test verifies that the removal is performed via the ExitingCompleted message,
                    // otherwise we would have `MarkNodeAsUnavailable(second)` to trigger the FailureDetectorPuppet

                    // verify that the 'second' node is no longer part of the 'members'/'unreachable' set
                    await AwaitAssertAsync(() =>
                    {
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(secondAddress);
                    });
                    await AwaitAssertAsync(() =>
                    {
                        ClusterView.UnreachableMembers.Select(c => c.Address).Should().NotContain(secondAddress);
                    });
                }, _config.First, _config.Third);

                await RunOnAsync(async () =>
                {
                    // verify that the second node is shut down
                    await AwaitConditionAsync(() => Task.FromResult(Cluster.IsTerminated));
                    await EnterBarrierAsync("second-shutdown");
                }, _config.Second);

                await EnterBarrierAsync("finished");
            });

        }
    }
}
