using Akka.MultiNode.TestAdapter;
using Xunit;

[assembly: TestFramework(typeof(MultiNodeTestFramework))]
namespace Akka.MultiNode.TestAdapter.SampleTests.Metadata
{
    /// <summary>
    /// SampleTestsMetadata
    /// </summary>
    public static class SampleTestsMetadata
    {
        /// <summary>
        /// Sample tests assembly path
        /// </summary>
        public static string AssemblyPath => typeof(SampleTestsMetadata).Assembly.Location;
        /// <summary>
        /// Gets assembly file name
        /// </summary>
        public static string AssemblyFileName => typeof(SampleTestsMetadata).Assembly.GetName().Name + ".dll";
    }
}