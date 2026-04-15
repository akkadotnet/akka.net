//-----------------------------------------------------------------------
// <copyright file="Sink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.MultiNode.TestAdapter.Internal;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter.Tests
{
    internal class TestSink : Xunit.Sdk.IMessageSink, IDisposable
    {
        public ManualResetEvent Finished { get; } = new ManualResetEvent(false);

        public List<TestResult> TestResults { get; } = new List<TestResult>();

        // Map TestCaseUniqueID -> MultiNodeTestCase for tracking
        private readonly Dictionary<string, MultiNodeTestCase> _testCaseMap = new Dictionary<string, MultiNodeTestCase>();

        public bool OnMessage(IMessageSinkMessage message)
        {

            switch (message)
            {
                case ITestMethodStarting methodStart:
                    // In v3 with ISelfExecutingXunitTestCase, TestCaseStarting may not be sent.
                    // Use TestMethodStarting as the trigger to create a test result entry.
                    // The TestCaseUniqueID isn't available here, so we'll track by method.
                    return true;

                case ITestStarting testStart:
                {
                    // Create a result entry on first TestStarting per TestCaseUniqueID
                    var existing = TestResults.FirstOrDefault(t => t.TestCaseUniqueID == testStart.TestCaseUniqueID);
                    if (existing == null)
                    {
                        // Use the registered test case display name if available,
                        // otherwise fall back to the test-level display name
                        var displayName = testStart.TestDisplayName;
                        if (_testCaseMap.TryGetValue(testStart.TestCaseUniqueID, out var tc))
                            displayName = tc.TestCaseDisplayName;
                        TestResults.Add(new TestResult(testStart.TestCaseUniqueID, displayName));
                    }
                    return true;
                }

                case ITestPassed testPassed:
                {
                    var result = TestResults.FirstOrDefault(t => t.TestCaseUniqueID == testPassed.TestCaseUniqueID);
                    if (result != null)
                        result.Total++;
                    return true;
                }

                case ITestFailed testFailed:
                {
                    var result = TestResults.FirstOrDefault(t => t.TestCaseUniqueID == testFailed.TestCaseUniqueID);
                    if (result != null)
                    {
                        result.Total++;
                        result.FailedCount++;
                    }
                    return true;
                }

                case ITestSkipped testSkipped:
                {
                    var result = TestResults.FirstOrDefault(t => t.TestCaseUniqueID == testSkipped.TestCaseUniqueID);
                    if (result != null)
                    {
                        result.Total++;
                        result.SkippedCount++;
                    }
                    return true;
                }

                case ITestAssemblyFinished _:
                    Finished.Set();
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Registers a MultiNodeTestCase so that it can be looked up by UniqueID later.
        /// </summary>
        public void RegisterTestCase(MultiNodeTestCase testCase)
        {
            _testCaseMap[testCase.UniqueID] = testCase;
        }

        /// <summary>
        /// Gets the MultiNodeTestCase for a test result (if registered).
        /// </summary>
        public MultiNodeTestCase GetTestCase(TestResult result)
        {
            _testCaseMap.TryGetValue(result.TestCaseUniqueID, out var testCase);
            return testCase;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }

    internal class TestResult
    {
        public TestResult(string testCaseUniqueID, string displayName)
        {
            TestCaseUniqueID = testCaseUniqueID;
            DisplayName = displayName;
        }

        public string TestCaseUniqueID { get; }
        public string DisplayName { get; }

        // RunSummary is a struct in xUnit v3, so we track counts directly
        public int Total { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }

        // Keep a RunSummary-like API for test assertions
        public TestResultSummary RunSummary => new TestResultSummary(Total, FailedCount, SkippedCount);

        public bool Passed => FailedCount == 0 && SkippedCount == 0;
        public bool Failed => FailedCount > 0;
        public bool Skipped => FailedCount == 0 && SkippedCount == Total;
        public bool NotRun => Total == 0;
    }

    internal class TestResultSummary
    {
        public TestResultSummary(int total, int failed, int skipped)
        {
            Total = total;
            Failed = failed;
            Skipped = skipped;
        }

        public int Total { get; }
        public int Failed { get; }
        public int Skipped { get; }
    }
}
