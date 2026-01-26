//-----------------------------------------------------------------------
// <copyright file="NodeDowningAndBeingRemovedSpec.cs" company="Akka.NET Project">
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
    public class NodeDowningAndBeingRemovedSpecSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }
        public RoleName Third { get; set; }

        public NodeDowningAndBeingRemovedSpecSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString("akka.cluster.auto-down-unreachable-after = off"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class NodeDowningAndBeingRemovedSpec : MultiNodeClusterSpec
    {
        private readonly NodeDowningAndBeingRemovedSpecSpecConfig _config;

        public NodeDowningAndBeingRemovedSpec() : this(new NodeDowningAndBeingRemovedSpecSpecConfig())
        {
        }

        protected NodeDowningAndBeingRemovedSpec(NodeDowningAndBeingRemovedSpecSpecConfig config) : base(config, typeof(NodeDowningAndBeingRemovedSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task NodeDowningAndBeingRemovedSpecs()
        {
            await Node_that_is_downed_must_eventually_be_removed_from_membership();
        }

        public async Task Node_that_is_downed_must_eventually_be_removed_from_membership()
        {
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third);

            var secondAddress = GetAddress(_config.Second);
            var thirdAddress = GetAddress(_config.Third);

            await WithinAsync(45.Seconds(), async () =>
            {
                await RunOnAsync(() =>
                {
                    Cluster.Down(secondAddress);
                    Cluster.Down(thirdAddress);
                    return Task.CompletedTask;
                }, _config.First);
                await EnterBarrierAsync("second-and-third-down");

                await RunOnAsync(async () =>
                {
                    // verify that the node is shut down
                    await AwaitConditionAsync(() => Task.FromResult(Cluster.IsTerminated));
                }, _config.Second, _config.Third);
                await EnterBarrierAsync("second-and-third-shutdown");

                await RunOnAsync(async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(secondAddress);
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(thirdAddress);
                    });
                }, _config.First);

                await EnterBarrierAsync("finished");
            });

        }
    }
}
