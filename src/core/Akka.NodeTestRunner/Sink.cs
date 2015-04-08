using System;
using System.Collections.Generic;
using System.Text;
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
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var specPass = new SpecPass(_nodeIndex + 1, testPassed.TestCase.DisplayName);
                Console.WriteLine(specPass);
                Passed = true;
                return true;
            }
            var testFailed = message as ITestFailed;
            if (testFailed != null)
            {
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var specFail = new SpecFail(_nodeIndex + 1, testFailed.TestCase.DisplayName);
                foreach (var failedMessage in testFailed.Messages) specFail.FailureMessages.Add(failedMessage);
                foreach (var stackTrace in testFailed.StackTraces) specFail.FailureStackTraces.Add(stackTrace);
                foreach(var exceptionType in testFailed.ExceptionTypes) specFail.FailureExceptionTypes.Add(exceptionType);
                Console.Write(specFail);
                return true;
            }
            var errorMessage = message as ErrorMessage;
            if (errorMessage != null)
            {
                var specFail = new SpecFail(_nodeIndex + 1, "ERRORED");
                foreach (var failedMessage in errorMessage.Messages) specFail.FailureMessages.Add(failedMessage);
                foreach (var stackTrace in errorMessage.StackTraces) specFail.FailureStackTraces.Add(stackTrace);
                foreach (var exceptionType in errorMessage.ExceptionTypes) specFail.FailureExceptionTypes.Add(exceptionType);
                Console.Write(specFail);
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

    /// <summary>
    /// Message class used for reporting a test pass.
    /// 
    /// <remarks>
    /// The Akka.MultiNodeTestRunner.Shared.MessageSink depends on the format string
    /// that this class produces, so do not remove or refactor it.
    /// </remarks>
    /// </summary>
    public class SpecPass
    {
        public SpecPass(int nodeIndex, string testDisplayName)
        {
            TestDisplayName = testDisplayName;
            NodeIndex = nodeIndex;
        }

        public int NodeIndex { get; private set; }

        public string TestDisplayName { get; private set; }

        public override string ToString()
        {
            return string.Format("[Node{0}][PASS] {1}", NodeIndex, TestDisplayName);
        }
    }

    /// <summary>
    /// Message class used for reporting a test fail.
    /// 
    /// <remarks>
    /// The Akka.MultiNodeTestRunner.Shared.MessageSink depends on the format string
    /// that this class produces, so do not remove or refactor it.
    /// </remarks>
    /// </summary>
    public class SpecFail : SpecPass
    {
        public SpecFail(int nodeIndex, string testDisplayName) : base(nodeIndex, testDisplayName)
        {
            FailureMessages = new List<string>();
            FailureStackTraces = new List<string>();
            FailureExceptionTypes = new List<string>();
        }

        public IList<string> FailureMessages { get; private set; }
        public IList<string> FailureStackTraces { get; private set; }
        public IList<string> FailureExceptionTypes { get; private set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("[Node{0}][FAIL] {1}", NodeIndex, TestDisplayName));
            foreach (var exception in FailureExceptionTypes)
            {
                sb.AppendFormat("[Node{0}][FAIL-EXCEPTION] Type: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureMessages)
            {
                sb.AppendFormat("--> [Node{0}][FAIL-EXCEPTION] Message: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureStackTraces)
            {
                sb.AppendFormat("--> [Node{0}][FAIL-EXCEPTION] StackTrace: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}