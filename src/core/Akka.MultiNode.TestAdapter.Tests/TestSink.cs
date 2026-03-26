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
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using LongLivedMarshalByRefObject = Xunit.Sdk.LongLivedMarshalByRefObject;
using TestCaseStarting = Xunit.Sdk.TestCaseStarting;

namespace Akka.MultiNode.TestAdapter.Tests
{
    internal class TestSink : LongLivedMarshalByRefObject, IMessageSink, IDisposable
    {
        public ManualResetEvent Finished { get; }= new ManualResetEvent(false);

        public List<TestResult> TestResults { get; } = new List<TestResult>();

        public bool OnMessage(IMessageSinkMessage message)
        {
            switch (message)
            {
                case TestCaseStarting start:
                    TestResults.Add(new TestResult(start.TestCase));
                    return true;
                
                case ITestPassed testPassed:
                {
                    var result = TestResults.First(t => ReferenceEquals(t.TestCase, testPassed.TestCase));
                    result.RunSummary.Total++;
                    return true;
                }
                
                case ITestFailed testFailed:
                {
                    var result = TestResults.First(t => ReferenceEquals(t.TestCase, testFailed.TestCase));
                    result.RunSummary.Total++;
                    result.RunSummary.Failed++;
                    return true;
                }
                
                case ITestSkipped testSkipped:
                {
                    var result = TestResults.First(t => ReferenceEquals(t.TestCase, testSkipped.TestCase));
                    result.RunSummary.Total++;
                    result.RunSummary.Skipped++;
                    return true;
                }
                
                case ITestAssemblyFinished _:
                    Finished.Set();
                    return true;
                
                default:
                    return true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }

    internal class TestResult
    {
        public TestResult(ITestCase testCase)
        {
            TestCase = testCase;
        }

        public ITestCase TestCase { get; }
        public RunSummary RunSummary { get; } = new RunSummary();

        public bool Passed => RunSummary.Failed == 0 && RunSummary.Skipped == 0;
        public bool Failed => RunSummary.Failed > 0;
        public bool Skipped => RunSummary.Failed == 0 && RunSummary.Skipped == RunSummary.Total;
        public bool NotRun => RunSummary.Total == 0;
    }
}

