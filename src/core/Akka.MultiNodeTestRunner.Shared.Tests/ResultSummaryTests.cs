//-----------------------------------------------------------------------
// <copyright file="ResultSummaryTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.MultiNodeTestRunner.TrxReporter.Models;
using FluentAssertions;
using Xunit;

namespace Akka.MultiNodeTestRunner.TrxReporter.Tests
{
    public class ResultSummaryTests
    {
        public static IEnumerable<object[]> ResultsSummaryOutcomeData
        {
            get
            {
                yield return new object[]
                {
                    new UnitTest[] { },
                    TestOutcome.NotExecuted
                };

                yield return new object[]
                {
                    new[]
                    {
                        new UnitTest("", "", Identifier.Empty, "")
                        {
                            Results =
                            {
                                new UnitTestResult(Identifier.Empty, Identifier.Empty, Identifier.Empty, "", "")
                                {
                                    Outcome = TestOutcome.Passed
                                }
                            }
                        }, 
                    },
                    TestOutcome.Passed
                };

                yield return new object[]
                {
                    new[]
                    {
                        new UnitTest("", "", Identifier.Empty, "")
                        {
                            Results =
                            {
                                new UnitTestResult(Identifier.Empty, Identifier.Empty, Identifier.Empty, "", "")
                                {
                                    Outcome = TestOutcome.Passed
                                }
                            }
                        }, 
                        new UnitTest("", "", Identifier.Empty, "")
                        {
                            Results =
                            {
                                new UnitTestResult(Identifier.Empty, Identifier.Empty, Identifier.Empty, "", "")
                                {
                                    Outcome = TestOutcome.Failed
                                }
                            }
                        }, 
                    },
                    TestOutcome.Failed
                };
            }
        }

        [Theory]
        [MemberData(nameof(ResultsSummaryOutcomeData))]
        public void ResultsSummaryOutcome(UnitTest[] tests, TestOutcome outcome)
        {
            var summary = new ResultSummary(tests, new Output());

            summary.Outcome.ShouldBeEquivalentTo(outcome);
        }
    }
}
