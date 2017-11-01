//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolverDowningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Cluster.Tests.MultiNode
{
    public sealed class SplitBrainDowningSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public SplitBrainDowningSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            TestTransport = true;
            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                akka {
                    cluster {
                        down-removal-margin = 1s
                        downing-provider-class = ""Akka.Cluster.SplitBrainResolver, Akka.Cluster""
                        split-brain-resolver {
                            stable-after = 1s
                            active-strategy = keep-majority
                        }
                    }
                }"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class SplitBrainResolverDowningSpec : MultiNodeClusterSpec
    {
        private readonly SplitBrainDowningSpecConfig _config;

        public SplitBrainResolverDowningSpec() : this(new SplitBrainDowningSpecConfig())
        {
        }

        protected SplitBrainResolverDowningSpec(SplitBrainDowningSpecConfig config) : base(config, typeof(SplitBrainResolverDowningSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void SplitBrainKeepMajorityDowningSpec()
        {
            A_Cluster_of_5_nodes_must_reach_initial_convergence();
            A_Cluster_must_detect_network_partition_and_down_minor_part_of_the_cluster();
        }

        private void A_Cluster_of_5_nodes_must_reach_initial_convergence()
        {
            AwaitClusterUp(Roles.ToArray());
            EnterBarrier("after-1");
        }

        private void A_Cluster_must_detect_network_partition_and_down_minor_part_of_the_cluster()
        {
            var majority = new[] { _config.First, _config.Second, _config.Third };
            var minority = new[] { _config.Fourth, _config.Fifth };

            EnterBarrier("before-split");

            RunOn(() =>
            {
                foreach (var a in majority)
                    foreach (var b in minority)
                        TestConductor.Blackhole(a, b, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.First);

            EnterBarrier("after-split");

            RunOn(() =>
            {
                foreach (var role in minority)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }
                
                // side with majority of the nodes must stay up
                AwaitMembersUp(majority.Length, canNotBePartOfMemberRing: minority.Select(GetAddress).ToImmutableHashSet());
                AssertLeader(majority);
            }, majority);

            RunOn(() =>
            {
                foreach (var role in majority)
                {
                    MarkNodeAsUnavailable(GetAddress(role));
                }

                // side with majority of the nodes must stay up
                AwaitMembersUp(majority.Length, canNotBePartOfMemberRing: minority.Select(GetAddress).ToImmutableHashSet());
                AssertLeader(majority);
            }, minority);

            EnterBarrier("after-2");
        }
    }
}