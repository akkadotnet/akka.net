using System;
using System.Threading;
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

            Thread.Sleep(TimeSpan.FromSeconds(10));

            using (var controller = new XunitFrontController(assemblyName))
            {
                using (var sink = new Sink(nodeIndex))
                {
                    Thread.Sleep(10000);
                    controller.RunTests(new[] { new Xunit1TestCase(assemblyName, null, typeName, testName, displayName, null, "MultiNodeTest") }, sink, new TestFrameworkOptions());
                    sink.Finished.WaitOne();
                    Environment.Exit(sink.Passed ? 0 : 1);
                }
            }
        }
    }
}
