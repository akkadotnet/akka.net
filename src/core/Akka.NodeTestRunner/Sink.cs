using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace Akka.NodeTestRunner
{
    class Sink : IMessageSink, IDisposable
    {
        public bool Passed { get; private set; }
        public ManualResetEvent Finished { get; private set; }
        readonly int _nodeIndex;

        public Sink(int nodeIndex)
        {
            _nodeIndex = nodeIndex;
            Finished = new ManualResetEvent(false);
        }

        public bool OnMessage(IMessageSinkMessage message)
        {
            var resultMessage = message as ITestResultMessage;
            if (resultMessage != null)
            {
                Console.WriteLine(resultMessage.Output);
            }
            var testPassed = message as ITestPassed;
            if (testPassed != null)
            {
                Console.WriteLine("[Node{0}] {1} passed.", _nodeIndex, testPassed.TestCase.DisplayName);
                Passed = true;
                return true;
            }
            var testFailed = message as ITestFailed;
            if (testFailed != null)
            {
                Console.WriteLine("[Node{0}] {1} failed.", _nodeIndex, testFailed.TestDisplayName);
                foreach(var failedMessage in testFailed.Messages) Console.WriteLine(failedMessage);
                foreach (var stackTrace in testFailed.StackTraces) Console.WriteLine(stackTrace);
                return true;
            }
            var errorMessage = message as ErrorMessage;
            if (errorMessage != null)
            {
                Console.WriteLine("[Node{0}] errored", _nodeIndex);
                foreach (var errorText in errorMessage.Messages) Console.WriteLine(errorText);
                foreach (var stackTrace in errorMessage.StackTraces) Console.WriteLine(stackTrace);
            }
            if (message is ITestAssemblyFinished)
            {
                Finished.Set();
            }

            return true;
        }

        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}