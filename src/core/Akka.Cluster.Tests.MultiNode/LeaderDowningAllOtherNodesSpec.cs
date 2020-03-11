//-----------------------------------------------------------------------
// <copyright file="LeaderDowningAllOtherNodesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class LeaderDowningAllOtherNodesConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }
        public RoleName Sixth { get; }

        public LeaderDowningAllOtherNodesConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.cluster.failure-detector.monitored-by-nr-of-members = 2
                    akka.cluster.auto-down-unreachable-after = 1s"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class LeaderDowningAllOtherNodesSpec : MultiNodeClusterSpec
    {
        private readonly LeaderDowningAllOtherNodesConfig _config;

        public LeaderDowningAllOtherNodesSpec() : this(new LeaderDowningAllOtherNodesConfig())
        {
        }

        protected LeaderDowningAllOtherNodesSpec(LeaderDowningAllOtherNodesConfig config) : base(config, typeof(LeaderDowningAllOtherNodesSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void LeaderDowningAllOtherNodesSpecs()
        {
            A_Cluster_of_6_nodes_with_monitored_by_nr_of_members_2_must_setup();
            A_Cluster_of_6_nodes_with_monitored_by_nr_of_members_2_must_remove_all_shutdown_nodes();
        }

        public void A_Cluster_of_6_nodes_with_monitored_by_nr_of_members_2_must_setup()
        {
            // start some
            AwaitClusterUp(Roles.ToArray());
            EnterBarrier("after-1");
        }

        public void A_Cluster_of_6_nodes_with_monitored_by_nr_of_members_2_must_remove_all_shutdown_nodes()
        {
            var others = Roles.Drop(1).ToList();
            var shutdownAddresses = others.Select(c => GetAddress(c)).ToImmutableHashSet();
            EnterBarrier("before-all-other-shutdown");

            RunOn(() =>
            {
                foreach (var node in others)
                {
                    TestConductor.Exit(node, 0).Wait();
                }
            }, _config.First);
            EnterBarrier("all-other-shutdown");
            AwaitMembersUp(numbersOfMembers: 1, canNotBePartOfMemberRing: shutdownAddresses, timeout: 30.Seconds());
        }
    }
}

