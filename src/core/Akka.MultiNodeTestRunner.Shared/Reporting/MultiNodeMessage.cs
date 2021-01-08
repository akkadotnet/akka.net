//-----------------------------------------------------------------------
// <copyright file="MultiNodeMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    /// <summary>
    /// Message from an individual node
    /// </summary>
    public abstract class MultiNodeMessage : IComparable<MultiNodeMessage>, IEquatable<MultiNodeMessage>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeMessage"/> class.
        /// </summary>
        /// <param name="timeStamp">The time that the message occurred</param>
        /// <param name="message">The contents of the log message</param>
        /// <param name="nodeIndex">The index of the node where the message occurred</param>
        /// <param name="nodeRole">The role of the node where the message occurred</param>
        protected MultiNodeMessage(long timeStamp, string message, int nodeIndex, string nodeRole)
        {
            NodeIndex = nodeIndex;
            NodeRole = nodeRole;
            Message = message;
            TimeStamp = timeStamp;
        }


        /// <summary>
        /// The absolute time this message occurred represented as <see cref="DateTime.Ticks"/>
        /// </summary>
        public long TimeStamp { get; private set; }

        /// <summary>
        /// The contents of the log message.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// The index of the node in question.
        /// </summary>
        public int NodeIndex { get; private set; }

        /// <summary>
        /// The Role of the node in question.
        /// </summary>
        public string NodeRole { get; private set; }

        #region Comparisons

        /// <inheritdoc/>
        public virtual int CompareTo(MultiNodeMessage other)
        {
            var tc = TimeStamp.CompareTo(other.TimeStamp);
            if(tc != 0) return tc;
            var m = string.Compare(Message, other.Message, StringComparison.Ordinal);
            if (m != 0) return m;
            var ni = NodeIndex.CompareTo(other.NodeIndex);
            if (ni != 0) return ni;
            var nr = string.Compare(NodeRole, other.NodeRole, StringComparison.Ordinal);
            if (nr != 0) return nr;
            return 0;
        }

        #endregion

        #region Equality

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 13;
                hashCode = (hashCode * 397) ^ TimeStamp.GetHashCode();
                hashCode = (hashCode * 397) ^ NodeIndex;
                hashCode = (hashCode * 397) ^ Message.GetHashCode();
                hashCode = (hashCode * 397) ^ NodeRole.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            var msg = obj as MultiNodeMessage;
            return msg != null && Equals(msg);
        }

        /// <inheritdoc/>
        public virtual bool Equals(MultiNodeMessage other)
        {
            return other != null &&
                   NodeIndex == other.NodeIndex &&
                   string.Equals(NodeRole, other.NodeRole, StringComparison.Ordinal) &&
                   TimeStamp == other.TimeStamp &&
                   string.Equals(Message, other.Message);

        }

        #endregion
    }

    /// <summary>
    /// Message used to contain the PASS / FAIL results for a specific test
    /// </summary>
    public class MultiNodeResultMessage : MultiNodeMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeResultMessage"/> class.
        /// </summary>
        /// <param name="timeStamp">The time that the message occurred</param>
        /// <param name="message">The contents of the log message</param>
        /// <param name="nodeIndex">The index of the node where the message occurred</param>
        /// <param name="nodeRole">The role of the node where the message occurred</param>
        /// <param name="passed">The flag used to determine if the test passed. <c>true</c> if successful; otherwise <c>false</c>.</param>
        public MultiNodeResultMessage(long timeStamp, string message, int nodeIndex, string nodeRole, bool passed)
            : base(timeStamp, message, nodeIndex, nodeRole)
        {
            Passed = passed;
        }

        /// <summary>
        /// Flag to determine whether or not this <see cref="MultiNodeMessage.NodeIndex"/> passed its test or not.
        /// </summary>
        public bool Passed { get; private set; }

        #region Equality

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ Passed.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(MultiNodeMessage other)
        {
            var otherResultMessage = other as MultiNodeResultMessage;
            return otherResultMessage != null &&
                   base.Equals(other) &&
                   Passed == otherResultMessage.Passed;
        }

        #endregion
    }

    /// <summary>
    /// Messages emitted directly by the test runner itself for an individual spec
    /// </summary>
    public class MultiNodeTestRunnerMessage : MultiNodeMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeTestRunnerMessage"/> class.
        /// </summary>
        /// <param name="timeStamp">The time that the message occurred</param>
        /// <param name="message">The contents of the log message</param>
        /// <param name="actorPath">The path to the remote node where the message occurred</param>
        /// <param name="logLevel">The log level of the message</param>
        public MultiNodeTestRunnerMessage(long timeStamp, string message, string actorPath, LogLevel logLevel)
            : base(timeStamp, message, -1, string.Empty)
        {
            ActorPath = actorPath;
            LogLevel = logLevel;
        }

        /// <summary>
        /// The path of the actor on the remote node who generated this message.
        /// 
        /// CAN BE NULL.
        /// </summary>
        public string ActorPath { get; private set; }

        /// <summary>
        /// The log level for this message.
        /// </summary>
        public LogLevel LogLevel { get; private set; }

        #region Equality

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LogLevel;
                hashCode = (hashCode * 397) ^ ActorPath.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(MultiNodeMessage other)
        {
            var otherLogMessage = other as MultiNodeTestRunnerMessage;
            return otherLogMessage != null &&
                    base.Equals(other) &&
                    LogLevel == otherLogMessage.LogLevel &&
                    string.Equals(ActorPath, otherLogMessage.ActorPath);
        }

        #endregion
    }

    /// <summary>
    /// Used in cases where a log message was broken up across multiple lines and this fragment has to be appended
    /// to a previous message in the timeline
    /// </summary>
    public class MultiNodeLogMessageFragment : MultiNodeMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeLogMessageFragment"/> class.
        /// </summary>
        /// <param name="timeStamp">The time that the message occurred</param>
        /// <param name="message">The contents of the log message</param>
        /// <param name="nodeIndex">The index of the node where the message occurred</param>
        /// <param name="nodeRole">The role of the node where the message occurred</param>
        public MultiNodeLogMessageFragment(long timeStamp, string message, int nodeIndex, string nodeRole) 
            : base(timeStamp, message, nodeIndex, nodeRole)
        {
        }
    }

    /// <summary>
    /// Message from a node containing log information
    /// </summary>
    public class MultiNodeLogMessage : MultiNodeMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiNodeLogMessage"/> class.
        /// </summary>
        /// <param name="timeStamp">The time that the message occurred</param>
        /// <param name="message">The contents of the log message.</param>
        /// <param name="nodeIndex">The index of the node where the message occurred</param>
        /// <param name="nodeRole">The role of the node where the message occurred</param>
        /// <param name="actorPath">The path to the remote node where the message occurred</param>
        /// <param name="logLevel">The log level of the message</param>
        public MultiNodeLogMessage(long timeStamp, string message, int nodeIndex, string nodeRole, string actorPath, LogLevel logLevel)
            : base(timeStamp, message, nodeIndex, nodeRole)
        {
            ActorPath = actorPath;
            LogLevel = logLevel;
        }

        /// <summary>
        /// The path of the actor on the remote node who generated this message.
        /// 
        /// CAN BE NULL.
        /// </summary>
        public string ActorPath { get; private set; }

        /// <summary>
        /// The log level for this message.
        /// </summary>
        public LogLevel LogLevel { get; private set; }

        #region Equality

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LogLevel;
                hashCode = (hashCode * 397) ^ ActorPath.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(MultiNodeMessage other)
        {
            var otherLogMessage = other as MultiNodeLogMessage;
            return otherLogMessage != null &&
                    base.Equals(other) &&
                    LogLevel == otherLogMessage.LogLevel &&
                    string.Equals(ActorPath, otherLogMessage.ActorPath);
        }

        #endregion
    }
}

