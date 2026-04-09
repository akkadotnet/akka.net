using System;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute;

namespace Akka.MultiNode.TestAdapter.SampleTests
{
    public class FailedMultiNodeSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public FailedMultiNodeSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            
            CommonConfig = DebugConfig(true)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    
    public class FailedMultiNodeSpec : MultiNodeClusterSpec
    {
        public FailedMultiNodeSpec() : this(new FailedMultiNodeSpecConfig())
        {
        }

        private FailedMultiNodeSpec(FailedMultiNodeSpecConfig config) : base(config, typeof(FailedMultiNodeSpec))
        {
        }

        [MultiNodeFact]
        public void Should_fail()
        {
            throw new Exception("Spec should fail");
        }
    }
}