using System;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute;

namespace Akka.MultiNode.TestAdapter.SampleTests
{
    public class BadMultiNodeSpecConfig : MultiNodeConfig
    {
        public BadMultiNodeSpecConfig()
        {
           throw new Exception("Some config creation exception");
        }
    }
    
    // This spec should be skipped because failed to build it's config
    public class BadConfigMultiNodeSpec : MultiNodeClusterSpec
    {
        public BadConfigMultiNodeSpec() : this(new BadMultiNodeSpecConfig())
        {
        }

        private BadConfigMultiNodeSpec(BadMultiNodeSpecConfig config) : base(config, typeof(BadConfigMultiNodeSpec))
        {
        }

        [MultiNodeFact]
        public void Should_not_be_started()
        {
        }
    }
}