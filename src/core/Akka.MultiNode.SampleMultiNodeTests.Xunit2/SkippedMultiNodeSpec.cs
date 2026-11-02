using System;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute;

namespace Akka.MultiNode.TestAdapter.SampleTests
{
    public class SkippedMultiNodeSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public SkippedMultiNodeSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            
            CommonConfig = DebugConfig(true)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    
    public class SkippedMultiNodeSpec : MultiNodeClusterSpec
    {
        public SkippedMultiNodeSpec() : this(new SkippedMultiNodeSpecConfig())
        {
        }

        private SkippedMultiNodeSpec(SkippedMultiNodeSpecConfig config) : base(config, typeof(SkippedMultiNodeSpec))
        {
        }

        [MultiNodeFact(Skip = "This spec should be skipped")]
        public void Should_skip()
        {
            throw new NotImplementedException("This spec should be skipped");
        }
    }
}