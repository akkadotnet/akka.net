using System.Reflection;
using Akka.MultiNode.TestAdapter.Internal;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter
{
    public class MultiNodeTestFramework : TestFramework
    {
        public override string TestFrameworkDisplayName => "Akka.NET Multi-Node Test Framework";

        protected override ITestFrameworkDiscoverer CreateDiscoverer(Assembly assembly)
        {
            var testAssembly = new XunitTestAssembly(assembly);
            return new XunitTestFrameworkDiscoverer(
                testAssembly,
                new CollectionPerSessionTestCollectionFactory(testAssembly));
        }

        protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
        {
            var testAssembly = new XunitTestAssembly(assembly);
            return new MultiNodeTestFrameworkExecutor(testAssembly);
        }
    }
}
