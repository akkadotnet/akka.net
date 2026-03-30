using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.MultiNode.TestAdapter.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter
{
    public class MultiNodeFactDiscoverer : IXunitTestCaseDiscoverer
    {
        public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            IFactAttribute factAttribute)
        {
            var details = TestIntrospectionHelper.GetTestCaseDetails(
                discoveryOptions, testMethod, factAttribute,
                testMethodArguments: null, timeout: null, baseDisplayName: null, label: null);

            if (testMethod.Parameters.Count > 0)
            {
                return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new IXunitTestCase[]
                {
                    new ExecutionErrorTestCase(
                        testMethod,
                        details.TestCaseDisplayName,
                        details.UniqueID,
                        details.SourceFilePath,
                        details.SourceLineNumber,
                        "[MultiNodeFact] methods are not allowed to have parameters.")
                });
            }

            if (testMethod.Method.IsGenericMethodDefinition)
            {
                return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new IXunitTestCase[]
                {
                    new ExecutionErrorTestCase(
                        testMethod,
                        details.TestCaseDisplayName,
                        details.UniqueID,
                        details.SourceFilePath,
                        details.SourceLineNumber,
                        "[MultiNodeFact] methods are not allowed to be generic.")
                });
            }

            var testCase = new MultiNodeTestCase(
                testMethod,
                details.TestCaseDisplayName,
                details.UniqueID,
                details.Explicit,
                skipReason: details.SkipReason,
                skipExceptions: details.SkipExceptions,
                skipType: details.SkipType,
                skipUnless: details.SkipUnless,
                skipWhen: details.SkipWhen,
                sourceFilePath: details.SourceFilePath,
                sourceLineNumber: details.SourceLineNumber,
                timeout: details.Timeout);

            testCase.Load();

            if (testCase.InitializationException != null)
            {
                return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new IXunitTestCase[]
                {
                    new ExecutionErrorTestCase(
                        testMethod,
                        details.TestCaseDisplayName,
                        details.UniqueID,
                        details.SourceFilePath,
                        details.SourceLineNumber,
                        testCase.InitializationException.ToString())
                });
            }

            return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new IXunitTestCase[] { testCase });
        }
    }
}
