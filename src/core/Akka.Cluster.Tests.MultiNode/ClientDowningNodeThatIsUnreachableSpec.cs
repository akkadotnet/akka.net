//-----------------------------------------------------------------------
// <copyright file="ClientDowningNodeThatIsUnreachableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.MultiNode;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class ClientDowningNodeThatIsUnreachableMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public RoleName Third { get; private set; }

        public RoleName Fourth { get; private set; }

        public ClientDowningNodeThatIsUnreachableMultiNodeConfig(bool failureDetectorPuppet)
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig= DebugConfig(false).WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }

    class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode()
            : base(true, typeof(ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode))
        {
        }
    }


    class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode()
            : base(false, typeof(ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode))
        {
        }
    }

    public abstract class ClientDowningNodeThatIsUnreachableSpec : MultiNodeClusterSpec
    {
        private readonly ClientDowningNodeThatIsUnreachableMultiNodeConfig _config;

        protected ClientDowningNodeThatIsUnreachableSpec(bool failureDetectorPuppet, Type type)
            : this(new ClientDowningNodeThatIsUnreachableMultiNodeConfig(failureDetectorPuppet), type)
        {
        }

        protected ClientDowningNodeThatIsUnreachableSpec(ClientDowningNodeThatIsUnreachableMultiNodeConfig config, Type type)
            : base(config, type)
        {
            _config = config;
        }

        [MultiNodeFact()]
        public void Client_of_a_4_node_cluster_must_be_able_to_DOWN_a_node_that_is_UNREACHABLE()
        {
            var thirdAddress = GetAddress(_config.Third);
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);

            RunOn(() =>
            {
                // kill 'third' node
                TestConductor.Exit(_config.Third, 0).Wait();
                MarkNodeAsUnavailable(thirdAddress);

                // mark 'third' node as DOWN
                Cluster.Down(thirdAddress);
                EnterBarrier("down-third-node");

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
