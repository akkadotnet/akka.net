using System;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.NodeTestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var nodeIndex = CommandLine.GetInt32("multinode.index");
            var assemblyName = CommandLine.GetProperty("multinode.test-assembly");
            var typeName = CommandLine.GetProperty("multinode.test-class");
            var testName = CommandLine.GetProperty("multinode.test-method");
            var displayName = testName;

            using (var controller = new XunitFrontController(assemblyName))
            {
                using (var sink = new Sink(nodeIndex))
                {
                    controller.RunTests(new[] { new Xunit1TestCase(assemblyName, null, typeName, testName, displayName) }, sink, new TestFrameworkOptions());
                    sink.Finished.WaitOne();
                    Environment.Exit(sink.Passed ? 0 : 1);
                }
            }
        }
    }
}
