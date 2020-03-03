//-----------------------------------------------------------------------
// <copyright file="LogEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Avoids redundant parsing of log levels and other frequently-used log items
    /// </summary>
    internal static class LogFormats
    {
        public static readonly IReadOnlyDictionary<LogLevel, string> PrettyPrintedLogLevel;

        static LogFormats()
        {
            var dict = new Dictionary<LogLevel, string>();
            foreach(LogLevel i in Enum.GetValues(typeof(LogLevel)))
            {
                dict.Add(i, Enum.GetName(typeof(LogLevel), i).Replace("Level", "").ToUpperInvariant());
            }
            PrettyPrintedLogLevel = dict;
        }

        public static string PrettyNameFor(this LogLevel level)
        {
            return PrettyPrintedLogLevel[level];
        }
    }

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
        /// The exception that caused the log event. Can be <c>null</c>
        /// </summary>
        public Exception Cause { get; protected set; }

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
            if(Cause == null)
                return
                    $"[{LogLevel().PrettyNameFor()}][{Timestamp}][Thread {Thread.ManagedThreadId.ToString().PadLeft(4, '0')}][{LogSource}] {Message}";
            return
                $"[{LogLevel().PrettyNameFor()}][{Timestamp}][Thread {Thread.ManagedThreadId.ToString().PadLeft(4, '0')}][{LogSource}] {Message}{Environment.NewLine}Cause: {Cause}";
        }
    }
}
