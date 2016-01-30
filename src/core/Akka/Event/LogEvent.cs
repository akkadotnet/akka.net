//-----------------------------------------------------------------------
// <copyright file="LogEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            return ToString("[{LogLevel}][{Timestamp}][Thread {ThreadId}][{LogSource}] {Message}");
        }

        private static readonly ConcurrentDictionary<string,string> TemplateLookup = new ConcurrentDictionary<string, string>();

        protected static string GetOrAddTemplate(string template, Func<string,string> body)
        {
            return TemplateLookup.GetOrAdd(template, body);
        }
        public string ToString(string template)
        {
            var format = GetOrAddTemplate(template, t => template
                .Replace("{LogLevel", "{0")
                .Replace("{Timestamp", "{1")
                .Replace("{ThreadId", "{2")
                .Replace("{LogSource", "{3")
                .Replace("{LogClass", "{4")
                .Replace("{Message", "{5"));            

            var logLevel = LogLevel().ToString().Replace("Level", "").ToUpperInvariant();
            var threadId = Thread.ManagedThreadId.ToString().PadLeft(4, '0');
            return String.Format(format, logLevel, Timestamp, threadId, LogSource, LogClass, Message);
        }
    }
}

