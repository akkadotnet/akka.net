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

    class ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode1 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode1() : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode2 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode2() : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode3 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode3() : base(true)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode4 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithFailureDetectorPuppetMultiNode4() : base(true)
        {
        }
    }


    class ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode1 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode1() : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode2 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode2() : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode3 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode3() : base(false)
        {
        }
    }

    class ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode4 : ClientDowningNodeThatIsUpSpec
    {
        public ClientDowningNodeThatIsUpWithAccrualFailureDetectorMultiNode4() : base(false)
        {
        }
    }

    public abstract class ClientDowningNodeThatIsUpSpec : MultiNodeClusterSpec
    {
        private readonly ClientDowningNodeThatIsUpMultiNodeConfig _config;

        protected ClientDowningNodeThatIsUpSpec(bool failureDetectorPuppet)
            : this(new ClientDowningNodeThatIsUpMultiNodeConfig(failureDetectorPuppet))
        {
        }

        protected ClientDowningNodeThatIsUpSpec(ClientDowningNodeThatIsUpMultiNodeConfig config)
            : base(config)
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
