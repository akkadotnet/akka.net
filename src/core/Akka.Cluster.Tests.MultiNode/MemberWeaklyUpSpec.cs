//-----------------------------------------------------------------------
// <copyright file="MemberWeaklyUpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MemberWeaklyUpConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public MemberWeaklyUpConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = DebugConfig(on: false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.remote.retry-gate-closed-for = 3s
                    akka.cluster.allow-weakly-up-members = on"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class MemberWeaklyUpSpec : MultiNodeClusterSpec
    {
        private readonly MemberWeaklyUpConfig _config;
        private readonly ImmutableArray<RoleName> _side1;
        private readonly ImmutableArray<RoleName> _side2;

        public MemberWeaklyUpSpec() : this(new MemberWeaklyUpConfig())
        {
        }

        private MemberWeaklyUpSpec(MemberWeaklyUpConfig config) : base(config, typeof(MemberWeaklyUpSpec))
        {
            _config = config;
            _side1 = ImmutableArray.CreateRange(new[] { config.First, config.Second });
            _side2 = ImmutableArray.CreateRange(new[] { config.Third, config.Fourth, config.Fifth });
            MuteMarkingAsUnreachable();
        }

        [MultiNodeFact]
        public void Spec()
        {
            A_cluster_of_3_members_should_reach_initial_convergence();
            A_cluster_of_3_members_should_detect_network_partition_and_mark_nodes_on_the_other_side_as_unreachable();
            A_cluster_of_3_members_should_accept_joining_on_each_side_and_set_status_to_WeaklyUp();
            A_cluster_of_3_members_should_change_status_to_Up_after_healed_network_partition();
        }

        public void A_cluster_of_3_members_should_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Third, _config.Fourth);
            EnterBarrier("after-1");
        }

        public void A_cluster_of_3_members_should_detect_network_partition_and_mark_nodes_on_the_other_side_as_unreachable()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    // split the cluster in two parts (first, second) / (third, fourth, fifth)
                    foreach (var role1 in _side1)
                        foreach (var role2 in _side2)
                            TestConductor.Blackhole(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));
                }, _config.First);

                EnterBarrier("after-split");

                RunOn(() =>
                {
                    AwaitAssert(() =>
                        ClusterView.UnreachableMembers
                            .Select(m => m.Address).ToImmutableHashSet()
                            .ShouldBe(ImmutableHashSet.CreateRange(new[] { GetAddress(_config.Third), GetAddress(_config.Fourth) })));
                }, _config.First);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                        ClusterView.UnreachableMembers
                            .Select(m => m.Address).ToImmutableHashSet()
                            .ShouldBe(ImmutableHashSet.CreateRange(new[] { GetAddress(_config.First) })));
                }, _config.Third, _config.Fourth);

                EnterBarrier("after-2");
            });
        }

        public void A_cluster_of_3_members_should_accept_joining_on_each_side_and_set_status_to_WeaklyUp()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() => Cluster.Get(Sys).Join(GetAddress(_config.First)), _config.Second);
                RunOn(() => Cluster.Get(Sys).Join(GetAddress(_config.Fourth)), _config.Fifth);

                EnterBarrier("joined");

                RunOn(() => AwaitAssert(() =>
                {
                    ClusterView.Members.Count.ShouldBe(4);
                    ClusterView.Members.Any(m => m.Address == GetAddress(_config.Second) && m.Status == MemberStatus.WeaklyUp).ShouldBe(true);

                }), _side1.ToArray());

                RunOn(() => AwaitAssert(() =>
                {
                    ClusterView.Members.Count.ShouldBe(4);
                    ClusterView.Members.Any(m => m.Address == GetAddress(_config.Fifth) && m.Status == MemberStatus.WeaklyUp).ShouldBe(true);

                }), _side2.ToArray());

                EnterBarrier("after-3");
            });
        }

        public void A_cluster_of_3_members_should_change_status_to_Up_after_healed_network_partition()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    foreach (var role1 in _side1)
                        foreach (var role2 in _side2)
                        {
                            TestConductor.PassThrough(role1, role2, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));
                        }
                }, _config.First);

                EnterBarrier("after-passThrough");

                AwaitAllReachable();
                AwaitMembersUp(5);

                EnterBarrier("after-4");
            });
        }
    }
}
