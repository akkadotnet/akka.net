#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcLastNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiDcLastNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public MultiDcLastNodeConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = MultiNodeClusterSpec.ClusterConfig();
            NodeConfig(new []{First,Second}, new []
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = dc1")
            });
            NodeConfig(new[] { Third }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = dc2")
            });
        }
    }

    public class MultiDcLastNodeSpec : MultiNodeClusterSpec
    {
        private readonly MultiDcLastNodeConfig _config;

        public MultiDcLastNodeSpec() : this(new MultiDcLastNodeConfig()) { }
        protected MultiDcLastNodeSpec(MultiDcLastNodeConfig config) : base(config, typeof(MultiDcLastNodeSpec))
        {
            _config = config;
        }

        public RoleName First => _config.First;
        public RoleName Second => _config.Second;
        public RoleName Third => _config.Third;

        [MultiNodeFact]
        public void Test()
        {
            Multi_DC_cluster_with_one_remaining_node_in_other_DC_must_join();
            Multi_DC_cluster_with_one_remaining_node_in_other_DC_must_be_able_to_leave();
        }

        private void Multi_DC_cluster_with_one_remaining_node_in_other_DC_must_join()
        {
            RunOn(() => Cluster.Join(GetAddress(First)), First);
            RunOn(() => Cluster.Join(GetAddress(First)), Second, Third);
            EnterBarrier("join-cluster");

            Within(TimeSpan.FromSeconds(20), () =>
            {
                AwaitAssert(() => ClusterView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3));
            });
            EnterBarrier("cluster-started");
        }

        private void Multi_DC_cluster_with_one_remaining_node_in_other_DC_must_be_able_to_leave()
        {
            RunOn(() =>
            {
                // this works in same way for down
                Cluster.Leave(GetAddress(Third));
            }, Third);

            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.Members.Select(m => m.Address).Should().NotContain(GetAddress(Third)));
            }, First, Second);
            EnterBarrier("cross-data-center-left");
        }
    }
}