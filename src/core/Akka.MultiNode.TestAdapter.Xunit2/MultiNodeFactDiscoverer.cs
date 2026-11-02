using System.Collections.Generic;
using System.Linq;
using Akka.MultiNode.TestAdapter.Internal;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter
{
    public class MultiNodeFactDiscoverer : IXunitTestCaseDiscoverer
    { 
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeFactDiscoverer" /> class.
        /// </summary>
        /// <param name="diagnosticMessageSink">The message sink used to send diagnostic messages</param>
        public MultiNodeFactDiscoverer(IMessageSink diagnosticMessageSink) 
            => DiagnosticMessageSink = diagnosticMessageSink;

        /// <summary>
        /// Gets the message sink used to report <see cref="T:Xunit.Abstractions.IDiagnosticMessage" /> messages.
        /// </summary>
        protected IMessageSink DiagnosticMessageSink { get; }

        /// <summary>
        /// Creates a single <see cref="T:Xunit.Sdk.XunitTestCase" /> for the given test method.
        /// </summary>
        /// <param name="discoveryOptions">The discovery options to be used.</param>
        /// <param name="testMethod">The test method.</param>
        /// <param name="factAttribute">The attribute that decorates the test method.</param>
        /// <returns></returns>
        protected virtual IXunitTestCase CreateTestCase(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            return new MultiNodeTestCase(
                DiagnosticMessageSink, 
                discoveryOptions.MethodDisplayOrDefault(), 
                discoveryOptions.MethodDisplayOptionsOrDefault(), 
                testMethod);
        }

        /// <summary>
        /// Discover test cases from a test method. By default, if the method is generic, or
        /// it contains arguments, returns a single <see cref="T:Xunit.Sdk.ExecutionErrorTestCase" />;
        /// otherwise, it returns the result of calling <see cref="M:Xunit.Sdk.FactDiscoverer.CreateTestCase(Xunit.Abstractions.ITestFrameworkDiscoveryOptions,Xunit.Abstractions.ITestMethod,Xunit.Abstractions.IAttributeInfo)" />.
        /// </summary>
        /// <param name="discoveryOptions">The discovery options to be used.</param>
        /// <param name="testMethod">The test method the test cases belong to.</param>
        /// <param name="factAttribute">The fact attribute attached to the test method.</param>
        /// <returns>Returns zero or more test cases represented by the test method.</returns>
        public virtual IEnumerable<IXunitTestCase> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions, 
            ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var testCase = !testMethod.Method.GetParameters().Any()
                ? !testMethod.Method.IsGenericMethodDefinition
                    ? CreateTestCase(discoveryOptions, testMethod, factAttribute)
                    : new ExecutionErrorTestCase(
                        DiagnosticMessageSink,
                        discoveryOptions.MethodDisplayOrDefault(),
                        discoveryOptions.MethodDisplayOptionsOrDefault(),
                        testMethod, "[MultiNodeFact] methods are not allowed to be generic.")
                : new ExecutionErrorTestCase(
                    DiagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    "[MultiNodeFact] methods are not allowed to have parameters.");

            if (!(testCase is MultiNodeTestCase test)) 
                yield break;
            
            test.Load();
            yield return test.InitializationException == null 
                ? (IXunitTestCase) test 
                : new ExecutionErrorTestCase(
                    DiagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    test.InitializationException.ToString());
        }
    }
}