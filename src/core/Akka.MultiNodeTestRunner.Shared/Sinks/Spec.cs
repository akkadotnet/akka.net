//-----------------------------------------------------------------------
// <copyright file="Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
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
        public SpecPass(int nodeIndex, string nodeRole, string testDisplayName)
        {
            TestDisplayName = testDisplayName;
            NodeIndex = nodeIndex;
            NodeRole = nodeRole;
        }

        public int NodeIndex { get; private set; }
        public string NodeRole { get; private set; }

        public string TestDisplayName { get; private set; }

        public override string ToString()
        {
            return string.Format("[Node{0}:{1}][PASS] {2}", NodeIndex, NodeRole, TestDisplayName);
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
        public SpecFail(int nodeIndex, string nodeRole, string testDisplayName) : base(nodeIndex, nodeRole, testDisplayName)
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
            sb.AppendLine(string.Format("[Node{0}:{1}][FAIL] {2}", NodeIndex, NodeRole, TestDisplayName));
            foreach (var exception in FailureExceptionTypes)
            {
                sb.AppendFormat("[Node{0}:{1}][FAIL-EXCEPTION] Type: {2}", NodeIndex, NodeRole, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureMessages)
            {
                sb.AppendFormat("--> [Node{0}:{1}][FAIL-EXCEPTION] Message: {2}", NodeIndex, NodeRole, exception);
                sb.AppendLine();
            }
            foreach (var exception in FailureStackTraces)
            {
                sb.AppendFormat("--> [Node{0}:{1}][FAIL-EXCEPTION] StackTrace: {2}", NodeIndex, NodeRole, exception);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
