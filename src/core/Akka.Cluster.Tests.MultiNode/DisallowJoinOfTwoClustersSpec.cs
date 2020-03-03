//-----------------------------------------------------------------------
// <copyright file="DisallowJoinOfTwoClustersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class DisallowJoinOfTwoClusterSpecConfig : MultiNodeConfig
    {
        public readonly RoleName a1;
        public readonly RoleName a2;
        public readonly RoleName b1;
        public readonly RoleName b2;
        public readonly RoleName c1;

        public DisallowJoinOfTwoClusterSpecConfig()
        {
            a1 = Role("a1");
            a2 = Role("a2");
            b1 = Role("b1");
            b2 = Role("b2");
            c1 = Role("c1");

            CommonConfig = MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet();
        }
    }

    public class DisallowJoinOfTwoClusterSpec : MultiNodeClusterSpec
    {
        private readonly DisallowJoinOfTwoClusterSpecConfig _config;

        public DisallowJoinOfTwoClusterSpec() : this(new DisallowJoinOfTwoClusterSpecConfig())
        {
        }

        protected DisallowJoinOfTwoClusterSpec(DisallowJoinOfTwoClusterSpecConfig config) : base(config, typeof(DisallowJoinOfTwoClusterSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void Three_different_clusters_must_not_be_able_to_join()
        {
            RunOn(() =>
            {
                StartClusterNode();
            }, _config.a1, _config.b1, _config.c1);
            EnterBarrier("first-started");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.a1));
            }, _config.a1, _config.a2);

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.b1));
            }, _config.b1, _config.b2);

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.c1));
            }, _config.c1);

            int expectedSize = Myself == _config.c1 ? 1 : 2;
            AwaitMembersUp(expectedSize);

            EnterBarrier("two-members");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.a1));
            }, _config.b1);

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.c1));
            }, _config.b2);

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.a2));
            }, _config.c1);

            foreach (var _ in Enumerable.Range(1, 5))
            {
                ClusterView.Members.Count.ShouldBe(expectedSize);
                Thread.Sleep(1000);
            }

            EnterBarrier("after-1");
        }
    }
}
