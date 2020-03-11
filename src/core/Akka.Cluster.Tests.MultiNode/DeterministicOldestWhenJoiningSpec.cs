//-----------------------------------------------------------------------
// <copyright file="DeterministicOldestWhenJoiningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class DeterministicOldestWhenJoiningConfig : MultiNodeConfig
    {
        public RoleName Seed1 { get; }
        public RoleName Seed2 { get; }
        public RoleName Seed3 { get; }

        public DeterministicOldestWhenJoiningConfig()
        {
            Seed1 = Role("seed1");
            Seed2 = Role("seed2");
            Seed3 = Role("seed3");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    # not too quick to trigger problematic scenario more often
                    akka.cluster.leader-actions-interval = 2000 ms
                    akka.cluster.gossip-interval = 500 ms
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class DeterministicOldestWhenJoiningSpec : MultiNodeClusterSpec
    {
        private readonly DeterministicOldestWhenJoiningConfig _config;

        public DeterministicOldestWhenJoiningSpec() : this(new DeterministicOldestWhenJoiningConfig())
        {
        }

        protected DeterministicOldestWhenJoiningSpec(DeterministicOldestWhenJoiningConfig config) : base(config, typeof(DeterministicOldestWhenJoiningSpec))
        {
            _config = config;
        }

        public ImmutableList<Address> SeedNodes
        {
            get
            {
                return ImmutableList.Create(
                    GetAddress(_config.Seed1),
                    GetAddress(_config.Seed2),
                    GetAddress(_config.Seed3)).Sort(Member.AddressOrdering).Reverse();
            }
        }

        public Dictionary<Address, RoleName> RoleByAddress
        {
            get
            {
                return new Dictionary<Address, RoleName>()
                {
                    [GetAddress(_config.Seed1)] = _config.Seed1,
                    [GetAddress(_config.Seed2)] = _config.Seed2,
                    [GetAddress(_config.Seed3)] = _config.Seed3
                };
            }
        }

        [MultiNodeFact]
        public void DeterministicOldestWhenJoiningSpecs()
        {
            Joining_cluster_must_result_in_deterministic_oldest_node();
        }

        public void Joining_cluster_must_result_in_deterministic_oldest_node()
        {
            Cluster.Subscribe(TestActor, new [] { typeof(ClusterEvent.MemberUp) });
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            RunOn(() =>
            {
                Cluster.JoinSeedNodes(SeedNodes);
            }, RoleByAddress[SeedNodes.First()]);
            EnterBarrier("first-seed-joined");

            RunOn(() =>
            {
                Cluster.JoinSeedNodes(SeedNodes);
            }, RoleByAddress[SeedNodes[1]], RoleByAddress[SeedNodes[2]]);

            Within(10.Seconds(), () =>
            {
                var ups = ImmutableList.Create(
                    ExpectMsg<ClusterEvent.MemberUp>(),
                    ExpectMsg<ClusterEvent.MemberUp>(),
                    ExpectMsg<ClusterEvent.MemberUp>());

                ups.Select(c => c.Member)
                    .ToImmutableList()
                    .Sort(Member.AgeOrdering)
                    .First()
                    .Address.Should()
                    .Be(SeedNodes.First());
            });
            EnterBarrier("after-1");
        }
    }
}
