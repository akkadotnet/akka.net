//-----------------------------------------------------------------------
// <copyright file="LogEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Represents a LogEvent in the system.
    /// </summary>
    public abstract class LogEvent : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LogEvent" /> class.
        /// </summary>
        protected LogEvent()
        {
            Timestamp = DateTime.UtcNow;
            Thread = Thread.CurrentThread;
        }

        /// <summary>
        /// Gets the timestamp of this LogEvent.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        /// Gets the thread of this LogEvent.
        /// </summary>
        /// <value>The thread.</value>
        public Thread Thread { get; private set; }

        /// <summary>
        /// Gets the log source of this LogEvent.
        /// </summary>
        /// <value>The log source.</value>
        public string LogSource { get; protected set; }

        /// <summary>
        /// Gets the log class of this LogEvent.
        /// </summary>
        /// <value>The log class.</value>
        public Type LogClass { get; protected set; }

        /// <summary>
        /// Gets the message of this LogEvent.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; protected set; }

        /// <summary>
        /// Gets the specified LogLevel for this LogEvent.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public abstract LogLevel LogLevel();

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this LogEvent.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this LogEvent.</returns>
        public override string ToString()
        {
            return string.Format("[{0}][{1}][Thread {2}][{3}] {4}", LogLevel().ToString().Replace("Level", "").ToUpperInvariant(), Timestamp, Thread.ManagedThreadId.ToString().PadLeft(4, '0'), LogSource, Message);
        }
    }
}

