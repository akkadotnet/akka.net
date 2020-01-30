using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NSubstitute;

namespace Xunit.Runner.VisualStudio
{
    public class TestCaseFilterTests
    {
        readonly HashSet<string> dummyKnownTraits = new HashSet<string>(new string[2] { "Platform", "Product" });

        static IEnumerable<TestCase> GetDummyTestCases()
        {
            var testCaseList = new List<TestCase>();

            for (var i = 0; i < 10; i++)
                testCaseList.Add(new TestCase("Test" + i, new Uri(Constants.ExecutorUri), "DummyTestSource"));

            return testCaseList;
        }

        static LoggerHelper GetLoggerHelper(IMessageLogger messageLogger = null)
        {
            return new LoggerHelper(messageLogger ?? Substitute.For<IMessageLogger>(), new Stopwatch());
        }

        [Fact]
        public void TestCaseFilter_SingleMatch()
        {
            var dummyTestCaseList = GetDummyTestCases();
            var dummyTestCaseDisplayNamefilterString = "Test4";
            var context = Substitute.For<IRunContext>();
            var filterExpression = Substitute.For<ITestCaseFilterExpression>();
            // The matching should return a single testcase
            filterExpression.MatchTestCase(null, null).ReturnsForAnyArgs(x => ((TestCase)x[0]).FullyQualifiedName.Equals(dummyTestCaseDisplayNamefilterString));
            context.GetTestCaseFilter(null, null).ReturnsForAnyArgs(filterExpression);
            var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);

            var results = dummyTestCaseList.Where(testCase => filter.MatchTestCase(testCase));

            var result = Assert.Single(results);
            Assert.Equal("Test4", result.FullyQualifiedName);
        }

        [Fact]
        public void TestCaseFilter_NoFilterString()
        {
            var dummyTestCaseList = GetDummyTestCases();
            var context = Substitute.For<IRunContext>();
            context.GetTestCaseFilter(null, null).ReturnsForAnyArgs((ITestCaseFilterExpression)null);
            var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);

            var results = dummyTestCaseList.Where(testCase => filter.MatchTestCase(testCase));

            // Make sure we run the whole set since there is not filtering string specified
            Assert.Equal(dummyTestCaseList.Count(), results.Count());
        }

        [Fact]
        public void TestCaseFilter_ErrorParsingFilterString()
        {
            var messageLogger = Substitute.For<IMessageLogger>();
            var dummyTestCaseList = GetDummyTestCases();
            var context = Substitute.For<IRunContext>();
            context.GetTestCaseFilter(null, null).ReturnsForAnyArgs(x => { throw new TestPlatformFormatException("Hello from the exception"); });
            var filter = new TestCaseFilter(context, GetLoggerHelper(messageLogger), "dummyTestAssembly", dummyKnownTraits);

            var results = dummyTestCaseList.Where(testCase => filter.MatchTestCase(testCase));

            // Make sure we don't run anything due to the filtering string parse error
            Assert.Empty(results);
            var args = messageLogger.ReceivedCalls().Single().GetArguments();
            Assert.Collection(args,
                arg => Assert.Equal(TestMessageLevel.Warning, arg),
                arg => Assert.EndsWith("dummyTestAssembly: Exception filtering tests: Hello from the exception", (string)arg)
            );
        }
    }
}
