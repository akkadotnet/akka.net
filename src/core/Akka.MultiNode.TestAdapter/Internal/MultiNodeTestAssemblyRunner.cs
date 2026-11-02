using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class MultiNodeTestAssemblyRunner : XunitTestAssemblyRunner
    {
        public new static MultiNodeTestAssemblyRunner Instance { get; } = new();

        protected MultiNodeTestAssemblyRunner() { }

        /// <summary>
        /// Override to enforce sequential test collection execution (no parallelism for multi-node tests).
        /// </summary>
        protected override async ValueTask<RunSummary> RunTestCollections(
            XunitTestAssemblyRunnerContext ctxt,
            Exception? exception)
        {
            var summary = new RunSummary();

            foreach (var (testCollection, testCases) in OrderTestCollections(ctxt))
            {
                if (exception != null)
                    summary.Aggregate(await FailTestCollection(ctxt, testCollection, testCases, exception));
                else
                    summary.Aggregate(await RunTestCollection(ctxt, testCollection, testCases));

                if (ctxt.CancellationTokenSource.IsCancellationRequested)
                    break;
            }

            return summary;
        }
    }
}
