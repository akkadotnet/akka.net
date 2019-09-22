namespace Akka.MultiNodeTestRunner.AzureDevOps.Tests
{
    using System.Collections.Generic;
    using System.Xml.Linq;
    using FluentAssertions;
    using Models;
    using Xunit;

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
