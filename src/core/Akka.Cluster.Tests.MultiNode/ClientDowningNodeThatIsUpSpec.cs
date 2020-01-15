//-----------------------------------------------------------------------
// <copyright file="ClientDowningNodeThatIsUpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.Tests.MultiNode;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.TestKit
{
    public class ClientDowningNodeThatIsUpMultiNodeConfig : MultiNodeConfig
    {
        private readonly RoleName _first;
        public RoleName First { get { return _first; } }

        private readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        private readonly RoleName _third;
        public RoleName Third { get { return _third; } }

        private readonly RoleName _fourth;
        public RoleName Fourth { get { return _fourth; } }

        public ClientDowningNodeThatIsUpMultiNodeConfig(bool failureDetectorPuppet)
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");
            _fourth = Role("fourth");

            CommonConfig= DebugConfig(false).WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }

    class ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode() : base(true, typeof(ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode))
        {
        }
    }


    class ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode() : base(false, typeof(ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode))
        {
        }
    }

    public abstract class ClientDowningNodeThatIsUpSpec : MultiNodeClusterSpec
    {
        private readonly ClientDowningNodeThatIsUpMultiNodeConfig _config;

        protected ClientDowningNodeThatIsUpSpec(bool failureDetectorPuppet, Type type)
            : this(new ClientDowningNodeThatIsUpMultiNodeConfig(failureDetectorPuppet), type)
        {
        }

        protected ClientDowningNodeThatIsUpSpec(ClientDowningNodeThatIsUpMultiNodeConfig config, Type type)
            : base(config, type)
        {
            _config = config;
        }

        [MultiNodeFact()]
        public void Client_of_4_node_cluster_must_be_able_to_DOWN_a_node_that_is_UP()
        {
            var thirdAddress = GetAddress(_config.Third);
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);

            RunOn(() =>
            {
                Cluster.Down(thirdAddress);
                EnterBarrier("down-third-node");

                MarkNodeAsUnavailable(thirdAddress);

                AwaitMembersUp(3, ImmutableHashSet.Create(thirdAddress));

                ClusterView.Members.Any(x => x.Address == thirdAddress).ShouldBeFalse();
            }, _config.First);

            RunOn(() =>
            {
                EnterBarrier("down-third-node");
            }, _config.Third);

            RunOn(() =>
            {
                EnterBarrier("down-third-node");

                AwaitMembersUp(3, ImmutableHashSet.Create(thirdAddress));
            }, _config.Second, _config.Fourth);

            EnterBarrier("await-completion");
        }
    }
}
