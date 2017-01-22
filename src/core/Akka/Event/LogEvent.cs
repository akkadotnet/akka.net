//-----------------------------------------------------------------------
// <copyright file="LogEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a logging event in the system.
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
        /// The timestamp that this event occurred.
        /// </summary>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        /// The thread where this event occurred.
        /// </summary>
        public Thread Thread { get; private set; }

        /// <summary>
        /// The source that generated this event.
        /// </summary>
        public string LogSource { get; protected set; }

        /// <summary>
        /// The type that generated this event.
        /// </summary>
        public Type LogClass { get; protected set; }

        /// <summary>
        /// The message associated with this event.
        /// </summary>
        public object Message { get; protected set; }

        /// <summary>
        /// Retrieves the <see cref="Akka.Event.LogLevel" /> used to classify this event.
        /// </summary>
        /// <returns>The <see cref="Akka.Event.LogLevel" /> used to classify this event.</returns>
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
