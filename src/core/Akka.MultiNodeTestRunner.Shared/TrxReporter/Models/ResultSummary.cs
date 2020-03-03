//-----------------------------------------------------------------------
// <copyright file="ResultSummary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
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

        public XElement Serialize() => Elem("ResultSummary",
            Attr("outcome", Enum.GetName(typeof(TestOutcome), Outcome)),
            Elem("Counters",
                Attr("total", Total),
                Attr("executed", Executed),
                Attr("passed", Passed),
                Attr("failed", Failed),
                Attr("error", Error),
                Attr("timeout", Timeout),
                Attr("aborted", Aborted),
                Attr("inconclusive", Inconclusive),
                Attr("passedButRunAborted", PassedButRunAborted),
                Attr("notRunnable", NotRunnable),
                Attr("notExecuted", NotExecuted),
                Attr("disconnected", Disconnected),
                Attr("warning", Warning),
                Attr("completed", Completed),
                Attr("inProgress", InProgress),
                Attr("pending", Pending)
            ),
            Output
        );
    }
}
