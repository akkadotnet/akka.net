using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Engine;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace test.harness.uwp
{
    class MockTestDiscoveryEventHandler : ITestDiscoveryEventsHandler2
    {
        public void HandleDiscoveredTests(IEnumerable<TestCase> discoveredTestCases)
        {
            TestCases.AddRange(discoveredTestCases);
        }

        public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs discoveryCompleteEventArgs, IEnumerable<TestCase> lastChunk)
        {
            TestCases.AddRange(lastChunk);
            OnDiscoveryCompleted?.Invoke(this, EventArgs.Empty); 
        }

        public void HandleLogMessage(TestMessageLevel level, string message)
        {
            
        }

        public void HandleRawMessage(string rawMessage)
        {
            
        }

        public List<TestCase> TestCases { get; } = new List<TestCase>();

        public event EventHandler OnDiscoveryCompleted; 
    }

    class MockTestCaseEventsHandler : ITestCaseEventsHandler
    {
        public void SendSessionEnd()
        {
        }

        public void SendSessionStart()
        {
        }

        public void SendTestCaseEnd(TestCase testCase, TestOutcome outcome)
        {
        }

        public void SendTestCaseStart(TestCase testCase)
        {
        }

        public void SendTestResult(TestResult result)
        {
        }
    }

    class MockRunEventsHandler : ITestRunEventsHandler
    {
        public void HandleLogMessage(TestMessageLevel level, string message)
        {
            
        }

        public void HandleRawMessage(string rawMessage)
        {
            
        }

        public void HandleTestRunComplete(TestRunCompleteEventArgs testRunCompleteArgs, TestRunChangedEventArgs lastChunkArgs, ICollection<AttachmentSet> runContextAttachments, ICollection<string> executorUris)
        {
            
        }

        public void HandleTestRunStatsChange(TestRunChangedEventArgs testRunChangedArgs)
        {
            
        }

        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo)
        {
            return 0;   
        }
    }
}
