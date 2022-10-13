//-----------------------------------------------------------------------
// <copyright file="LogEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        protected LogEvent(ILogContents contents)
        {
            Contents = contents;
        }

        protected readonly ILogContents Contents;

        /// <summary>
        /// The exception that caused the log event. Can be <c>null</c>
        /// </summary>
        public Exception Cause => Contents.Exception;

        /// <summary>
        /// The timestamp that this event occurred.
        /// </summary>
        public DateTime Timestamp => Contents.Timestamp;

        /// <summary>
        /// The id of the thread where this event occurred.
        /// </summary>
        public int ThreadId => Contents.ThreadId;

        /// <summary>
        /// The source that generated this event.
        /// </summary>
        public LogSource LogSource => Contents.LogSource;

        public LogLevel LogLevel => Contents.LogLevel;

        private string _msg;

        public string Message
        {
            get
            {
                if (_msg == null)
                {
                    _msg = Contents.Format();
                }

                return _msg;
            }
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this LogEvent.
        /// </summary>
        /// <returns>A <see cref="string" /> that represents this LogEvent.</returns>
        public override string ToString()
        {
            return
                $"[{LogLevel.PrettyNameFor()}][{Timestamp:MM/dd/yyyy hh:mm:ss.fff}][Thread {ThreadId.ToString().PadLeft(4, '0')}][{LogSource}] {Message}";
        }
    }
}
