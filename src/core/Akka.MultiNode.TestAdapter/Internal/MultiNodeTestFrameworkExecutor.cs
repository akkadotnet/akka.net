using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class MultiNodeTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        public MultiNodeTestFrameworkExecutor(IXunitTestAssembly testAssembly)
            : base(testAssembly)
        {
        }

        public override async ValueTask RunTestCases(
            IReadOnlyCollection<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions,
            CancellationToken cancellationToken)
        {
            await MultiNodeTestAssemblyRunner.Instance.Run(
                TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
        }
    }
}
