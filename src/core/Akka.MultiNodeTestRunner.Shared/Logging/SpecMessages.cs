using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
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
            return string.Format("[NODE{0}][PASS] {1}", NodeIndex, TestDisplayName);
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
        public SpecFail(int nodeIndex, string testDisplayName, List<string> failureMessages, List<string> failureStackTraces, List<string> failureExceptionTypes) : base(nodeIndex, testDisplayName)
        {
            FailureMessages = failureMessages;
            FailureStackTraces = failureStackTraces;
            FailureExceptionTypes = failureExceptionTypes;
        }

        public IReadOnlyList<string> FailureMessages { get; private set; }
        public IReadOnlyList<string> FailureStackTraces { get; private set; }
        public IReadOnlyList<string> FailureExceptionTypes { get; private set; }

        /* TODO: this probably needs to be synchronized so FailureExceptionTypes[1] is printed
        *  immediately before FailureMessages[1] and then FailureStackTraces[1]
        */
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("[NODE{0}][FAIL] {1}", NodeIndex, TestDisplayName));
            foreach (var exception in FailureExceptionTypes)
            {
                sb.AppendFormat("[NODE{0}][FAIL-EXCEPTION] Type: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureMessages)
            {
                sb.AppendFormat("--> [NODE{0}][FAIL-EXCEPTION] Message: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureStackTraces)
            {
                sb.AppendFormat("--> [NODE{0}][FAIL-EXCEPTION] StackTrace: {1}", NodeIndex, exception);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
