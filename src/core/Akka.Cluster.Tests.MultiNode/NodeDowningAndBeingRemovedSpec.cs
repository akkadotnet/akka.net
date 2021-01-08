//-----------------------------------------------------------------------
// <copyright file="NodeDowningAndBeingRemovedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

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
        public void NodeDowningAndBeingRemovedSpecs()
        {
            Node_that_is_downed_must_eventually_be_removed_from_membership();
        }

        public void Node_that_is_downed_must_eventually_be_removed_from_membership()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Cluster.Down(GetAddress(_config.Second));
                    Cluster.Down(GetAddress(_config.Third));
                }, _config.First);
                EnterBarrier("second-and-third-down");

                RunOn(() =>
                {
                    // verify that the node is shut down
                    AwaitCondition(() => Cluster.IsTerminated);
                }, _config.Second, _config.Third);
                EnterBarrier("second-and-third-shutdown");

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(GetAddress(_config.Second));
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(GetAddress(_config.Third));
                    });
                }, _config.First);

                EnterBarrier("finished");
            });

        }
    }
}
