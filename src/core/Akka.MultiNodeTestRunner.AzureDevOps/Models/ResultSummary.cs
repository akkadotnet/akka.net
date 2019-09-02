// -----------------------------------------------------------------------
//  <copyright file="ResultSummary.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    public class ResultSummary : ITestEntity
    {
        public ResultSummary(IEnumerable<UnitTest> unitTests, Output output)
        {
            Output = output;

            var stats = unitTests
                .SelectMany(x => x.Results)
                .GroupBy(x => x.Outcome)
                .ToDictionary(k => k.Key, v => v.Count());

            Total = stats.Values.Sum();
            Executed = Total;

            int GetStats(TestOutcome outcome)
            {
                return stats.TryGetValue(outcome, out var value) ? value : 0;
            }

            Passed = GetStats(TestOutcome.Passed);
            Failed = GetStats(TestOutcome.Failed);
            Error = GetStats(TestOutcome.Error);
            Timeout = GetStats(TestOutcome.Timeout);
            Aborted = GetStats(TestOutcome.Aborted);
            Inconclusive = GetStats(TestOutcome.Inconclusive);
            PassedButRunAborted = GetStats(TestOutcome.PassedButRunAborted);
            NotRunnable = GetStats(TestOutcome.NotRunnable);
            NotExecuted = GetStats(TestOutcome.NotExecuted);
            Disconnected = GetStats(TestOutcome.Disconnected);
            Warning = GetStats(TestOutcome.Warning);
            Completed = GetStats(TestOutcome.Completed);
            InProgress = GetStats(TestOutcome.InProgress);
            Pending = GetStats(TestOutcome.Pending);

            Outcome = Total == 0
                ? TestOutcome.NotExecuted
                : Passed == Total
                    ? TestOutcome.Passed
                    : TestOutcome.Failed;
        }

        public TestOutcome Outcome { get; }

        public int Total { get; }
        public int Executed { get; }
        public int Passed { get; }
        public int Failed { get; }
        public int Error { get; }
        public int Timeout { get; }
        public int Aborted { get; }
        public int Inconclusive { get; }
        public int PassedButRunAborted { get; }
        public int NotRunnable { get; }
        public int NotExecuted { get; }
        public int Disconnected { get; }
        public int Warning { get; }
        public int Completed { get; }
        public int InProgress { get; }
        public int Pending { get; }

        public Output Output { get; }

        public XElement Serialize() => XmlHelper.Elem("ResultSummary",
            XmlHelper.Attr("outcome", Enum.GetName(typeof(TestOutcome), Outcome)),
            XmlHelper.Elem("Counters",
                XmlHelper.Attr("total", Total),
                XmlHelper.Attr("executed", Executed),
                XmlHelper.Attr("passed", Passed),
                XmlHelper.Attr("failed", Failed),
                XmlHelper.Attr("error", Error),
                XmlHelper.Attr("timeout", Timeout),
                XmlHelper.Attr("aborted", Aborted),
                XmlHelper.Attr("inconclusive", Inconclusive),
                XmlHelper.Attr("passedButRunAborted", PassedButRunAborted),
                XmlHelper.Attr("notRunnable", NotRunnable),
                XmlHelper.Attr("notExecuted", NotExecuted),
                XmlHelper.Attr("disconnected", Disconnected),
                XmlHelper.Attr("warning", Warning),
                XmlHelper.Attr("completed", Completed),
                XmlHelper.Attr("inProgress", InProgress),
                XmlHelper.Attr("pending", Pending)
            ),
            Output
        );
    }
}