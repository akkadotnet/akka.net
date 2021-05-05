//-----------------------------------------------------------------------
// <copyright file="Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            Timestamp = DateTime.UtcNow;
        }

        public int NodeIndex { get; private set; }
        public string NodeRole { get; private set; }

        public string TestDisplayName { get; private set; }

        public DateTime Timestamp { get; }

        public override string ToString()
        {
            return $"[Node{NodeIndex}:{NodeRole}][{Timestamp}][PASS] {TestDisplayName}";
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
            sb.AppendLine($"[Node{NodeIndex}:{NodeRole}][{Timestamp}][FAIL] {TestDisplayName}");
            foreach (var exception in FailureExceptionTypes)
            {
                sb.Append($"[Node{NodeIndex}:{NodeRole}][{Timestamp}][FAIL-EXCEPTION] Type: {exception}");
                sb.AppendLine();
            }
            foreach (var exception in FailureMessages)
            {
                sb.Append($"--> [Node{NodeIndex}:{NodeRole}][{Timestamp}][FAIL-EXCEPTION] Message: {exception}");
                sb.AppendLine();
            }
            foreach (var exception in FailureStackTraces)
            {
                sb.Append($"--> [Node{NodeIndex}:{NodeRole}][{Timestamp}][FAIL-EXCEPTION] StackTrace: {exception}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
