using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Xunit.Runner.VisualStudio
{
    public class AssemblyRunInfo
    {
        public string AssemblyFileName;
        public TestAssemblyConfiguration Configuration;
        public IList<TestCase> TestCases;
    }
}
