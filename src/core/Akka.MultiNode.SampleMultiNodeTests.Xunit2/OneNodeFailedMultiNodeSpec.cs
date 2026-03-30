using System;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute;

namespace Akka.MultiNode.TestAdapter.SampleTests
{
    public class OneNodeFailedMultiNodeSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public OneNodeFailedMultiNodeSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            
            CommonConfig = DebugConfig(true)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    
    public class OneNodeFailedMultiNodeSpec : MultiNodeClusterSpec
    {
        private readonly FailedMultiNodeSpecConfig _config;

        public OneNodeFailedMultiNodeSpec() : this(new FailedMultiNodeSpecConfig())
        {
        }

        private OneNodeFailedMultiNodeSpec(FailedMultiNodeSpecConfig config) : base(config, typeof(FailedMultiNodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void One_node_failed_should_fail()
        {
            RunOn(() => throw new Exception("Spec should fail"), _config.First);
        }
    }
}