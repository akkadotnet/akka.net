//-----------------------------------------------------------------------
// <copyright file="MultiNodeMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
        /// <dl>
        ///   <dt>Less than zero</dt>
        ///   <dd>This instance precedes <paramref name="other" /> in the sort order.</dd>
        ///   <dt>Zero</dt>
        ///   <dd>This instance occurs in the same position in the sort order as <paramref name="other" />.</dd>
        ///   <dt>Greater than zero</dt>
        ///   <dd>This instance follows <paramref name="other" /> in the sort order.</dd>
        /// </dl>
        /// </returns>
        public virtual int CompareTo(MultiNodeMessage other)
        {
            var tc = TimeStamp.CompareTo(other.TimeStamp);
            if(tc != 0) return tc;
            var m = String.Compare(Message, other.Message, StringComparison.Ordinal);
            if (m != 0) return m;
            var ni = NodeIndex.CompareTo(other.NodeIndex);
            if (ni != 0) return ni;
            var nr = String.Compare(NodeRole, other.NodeRole, StringComparison.Ordinal);
            if (nr != 0) return nr;
            return 0;
        }

        #endregion

        #region Equality

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

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            var msg = obj as MultiNodeMessage;
            return msg != null && Equals(msg);
        }

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

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ Passed.GetHashCode();
                return hashCode;
            }
        }

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
        public MultiNodeTestRunnerMessage(long timeStamp, string message, string actorPath, LogLevel logLevel)
            : base(timeStamp, message, -1, String.Empty)
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

