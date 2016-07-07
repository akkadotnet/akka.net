//-----------------------------------------------------------------------
// <copyright file="ClientDowningNodeThatIsUnreachableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

    class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode1 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode1()
            : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode2 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode2()
            : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode3 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode3()
            : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode4 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode4()
            : base(true)
        {
        }
    }


    class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode1 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode1()
            : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode2 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode2()
            : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode3 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode3()
            : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode4 : ClientDowningNodeThatIsUnreachableSpec
    {
        public ClientDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode4()
            : base(false)
        {
        }
    }

    public abstract class ClientDowningNodeThatIsUnreachableSpec : MultiNodeClusterSpec
    {
        private readonly ClientDowningNodeThatIsUnreachableMultiNodeConfig _config;

        protected ClientDowningNodeThatIsUnreachableSpec(bool failureDetectorPuppet)
            : this(new ClientDowningNodeThatIsUnreachableMultiNodeConfig(failureDetectorPuppet))
        {
        }

        protected ClientDowningNodeThatIsUnreachableSpec(ClientDowningNodeThatIsUnreachableMultiNodeConfig config)
            : base(config)
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
