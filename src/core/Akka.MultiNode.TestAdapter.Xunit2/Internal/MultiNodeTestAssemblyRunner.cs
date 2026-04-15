using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class MultiNodeTestAssemblyRunner : XunitTestAssemblyRunner
    {
        public MultiNodeTestAssemblyRunner(
            ITestAssembly testAssembly,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions) 
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
        }

        protected override async Task<RunSummary> RunTestCollectionsAsync(IMessageBus messageBus, CancellationTokenSource cancellationTokenSource)
        {
            var summary = new RunSummary();

            foreach (var (testCollection, testCases) in OrderTestCollections())
            {
                summary.Aggregate(await RunTestCollectionAsync(messageBus, testCollection, testCases, cancellationTokenSource));
                if (cancellationTokenSource.IsCancellationRequested)
                    break;
            }

            return summary;
        }
    }
}