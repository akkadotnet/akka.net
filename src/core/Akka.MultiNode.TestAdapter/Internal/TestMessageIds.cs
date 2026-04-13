using Xunit.Sdk;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    /// <summary>
    /// Helper struct to carry the UniqueID chain required by all xUnit v3 messages.
    /// </summary>
    internal readonly struct TestMessageIds
    {
        public string AssemblyUniqueID { get; }
        public string TestCollectionUniqueID { get; }
        public string? TestClassUniqueID { get; }
        public string? TestMethodUniqueID { get; }
        public string TestCaseUniqueID { get; }

        public TestMessageIds(
            string assemblyUniqueID,
            string testCollectionUniqueID,
            string? testClassUniqueID,
            string? testMethodUniqueID,
            string testCaseUniqueID)
        {
            AssemblyUniqueID = assemblyUniqueID;
            TestCollectionUniqueID = testCollectionUniqueID;
            TestClassUniqueID = testClassUniqueID;
            TestMethodUniqueID = testMethodUniqueID;
            TestCaseUniqueID = testCaseUniqueID;
        }

        public static TestMessageIds From(MultiNodeTestCase testCase) => new TestMessageIds(
            assemblyUniqueID: ((IAssemblyMetadata)testCase.TestMethod.TestClass.TestCollection.TestAssembly).UniqueID,
            testCollectionUniqueID: ((ITestCollectionMetadata)testCase.TestMethod.TestClass.TestCollection).UniqueID,
            testClassUniqueID: ((ITestClassMetadata)testCase.TestMethod.TestClass).UniqueID,
            testMethodUniqueID: ((ITestMethodMetadata)testCase.TestMethod).UniqueID,
            testCaseUniqueID: testCase.UniqueID
        );
    }
}
