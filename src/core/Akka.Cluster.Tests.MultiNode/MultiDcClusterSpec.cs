#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiDcNormalConfig : MultiDcConfig
    {
        public MultiDcNormalConfig() : base(5) { }
    }

    public class MultiDcFewCrossDcConnectionsConfig : MultiDcConfig
    {
        public MultiDcFewCrossDcConnectionsConfig() : base(1) { }
    }

    public abstract class MultiDcConfig : MultiNodeConfig
    {
        protected MultiDcConfig(int crossDcConnections)
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString($@"
              # DEBUG On for issue #23864
              akka.loglevel = DEBUG
              #akka.cluster.debug.verbose-receive-gossip-logging = on
              #akka.cluster.debug.verbose-gossip-logging = on
              akka.cluster.multi-data-center.cross-data-center-connections = {crossDcConnections}")
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new[] { First, Second }, new[]
            {
                ConfigurationFactory.ParseString(@"akka.cluster.multi-data-center.self-data-center = dc1")
            });
            NodeConfig(new[] { Third, Fourth, Fifth }, new[]
            {
                ConfigurationFactory.ParseString(@"akka.cluster.multi-data-center.self-data-center = dc2")
            });

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }
    }

    public class MultiDcNormalClusterSpec : MultiDcClusterSpec
    {
        public MultiDcNormalClusterSpec() : this(new MultiDcNormalConfig()) { }
        protected MultiDcNormalClusterSpec(MultiDcNormalConfig config) : base(config) { }
    }

    public class MultiDcFewCrossDcConnectionsClusterSpec : MultiDcClusterSpec
    {
        public MultiDcFewCrossDcConnectionsClusterSpec() : this(new MultiDcFewCrossDcConnectionsConfig()) { }
        protected MultiDcFewCrossDcConnectionsClusterSpec(MultiDcFewCrossDcConnectionsConfig config) : base(config) { }
    }

    public abstract class MultiDcClusterSpec : MultiNodeClusterSpec
    {
        private readonly MultiDcConfig _config;

        protected MultiDcClusterSpec(MultiDcConfig config) : base(config, typeof(MultiDcClusterSpec))
        {
            _config = config;
        }

        public RoleName First => _config.First;
        public RoleName Second => _config.Second;
        public RoleName Third => _config.Third;
        public RoleName Fourth => _config.Fourth;
        public RoleName Fifth => _config.Fifth;

        [MultiNodeFact]
        public void Test()
        {
            Cluster_with_multiple_data_centers_must_be_able_to_form();
            Cluster_with_multiple_data_centers_must_have_a_leader_per_data_center();
            Cluster_with_multiple_data_centers_must_be_able_to_have_data_center_member_changes_while_there_is_inter_data_center_unreachability();
            Cluster_with_multiple_data_centers_must_be_able_to_have_data_center_member_changes_while_there_is_unreachability_in_another_data_center();
            Cluster_with_multiple_data_centers_must_be_able_to_down_a_member_of_another_data_center();
        }

        private void Cluster_with_multiple_data_centers_must_be_able_to_form()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(First));
            }, First);

            RunOn(() =>
            {
                Cluster.Join(GetAddress(First));
            }, Second, Third, Fourth);

            EnterBarrier("form-cluster-join-attempt");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() => ClusterView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(4));
                });
            }, First, Second, Third, Fourth);

            EnterBarrier("cluster-started");
        }

        private void Cluster_with_multiple_data_centers_must_have_a_leader_per_data_center()
        {
            RunOn(() =>
            {
                Cluster.Settings.SelfDataCenter.Should().Be("dc1");
                ClusterView.Leader.Should().NotBeNull();
                var dc1 = ImmutableHashSet.CreateRange(new[] { GetAddress(First), GetAddress(Second) });
                dc1.Should().Contain(ClusterView.Leader);
            }, First, Second);

            RunOn(() =>
            {
                Cluster.Settings.SelfDataCenter.Should().Be("dc2");
                ClusterView.Leader.Should().NotBeNull();
                var dc2 = ImmutableHashSet.CreateRange(new[] { GetAddress(Third), GetAddress(Fourth) });
                dc2.Should().Contain(ClusterView.Leader);
            }, Third, Fourth);

            EnterBarrier("leader per data center");
        }

        private void Cluster_with_multiple_data_centers_must_be_able_to_have_data_center_member_changes_while_there_is_inter_data_center_unreachability() =>
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() => TestConductor.Blackhole(First, Third, ThrottleTransportAdapter.Direction.Both).Wait(), First);
                EnterBarrier("inter-data-center unreachability");

                RunOn(() => Cluster.Join(GetAddress(Third)), Fifth);

                RunOn(() =>
                {
                    // should be able to join and become up since the
                    // unreachable is between dc1 and dc2,
                    Within(TimeSpan.FromSeconds(10), () =>
                    {
                        AwaitAssert(() => ClusterView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(5));
                    });
                }, Third, Fourth, Fifth);

                RunOn(() => TestConductor.PassThrough(First, Third, ThrottleTransportAdapter.Direction.Both).Wait(), First);

                // should be able to join and become up since the
                // unreachable is between dc1 and dc2,
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() => ClusterView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(5));
                });

                EnterBarrier("inter-data-center unreachability end");
            });

        private void Cluster_with_multiple_data_centers_must_be_able_to_have_data_center_member_changes_while_there_is_unreachability_in_another_data_center() =>
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() => TestConductor.Blackhole(First, Second, ThrottleTransportAdapter.Direction.Both).Wait(), First);
                EnterBarrier("other-data-center-internal-unreachable");

                RunOn(() =>
                {
                    //FIXME: This is already part of the cluster, is this intended? Joined on line 152
                    Cluster.Join(GetAddress(Fifth));

                    // should be able to join and leave
                    // since the unreachable nodes are inside of dc1
                    Cluster.Leave(GetAddress(Fourth));

                    AwaitAssert(() => ClusterView.Members.Select(m => m.Address).Should().NotContain(GetAddress(Fourth), "4th node should leave the cluster"));
                    AwaitAssert(() => ClusterView.Members.Where(m => m.Status == MemberStatus.Up).Select(m => m.Address).Should().Contain(GetAddress(Fifth)));
                }, Third);
                EnterBarrier("other-data-center-internal-unreachable changed");

                RunOn(() => TestConductor.PassThrough(First, Second, ThrottleTransportAdapter.Direction.Both).Wait(), First);
                EnterBarrier("other-data-center-internal-unreachable end");
            });

        private void Cluster_with_multiple_data_centers_must_be_able_to_down_a_member_of_another_data_center()
        {
            RunOn(() => Cluster.Down(GetAddress(Second)), Fifth);

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members.Select(m => m.Address).Should().NotContain(GetAddress(Second)));
            }, First, Third, Fifth);
            EnterBarrier("cross-data-center-downed");
        }
    }
}