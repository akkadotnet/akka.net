//-----------------------------------------------------------------------
// <copyright file="Error.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// Represents an Error log event.
    /// </summary>
    public class Error : LogEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Error" /> class.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Error(Exception cause, string logSource, Type logClass, object message)
        {
            Cause = cause;
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        /// Gets the cause of the error.
        /// </summary>
        /// <value>The cause.</value>
        public Exception Cause { get; private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        /// <summary>
        /// Modifies the <see cref="LogEvent"/> printable error stream to also include
        /// the details of the <see cref="Cause"/> object itself.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ToString("[{LogLevel}][{Timestamp}][Thread {ThreadId}][{LogSource}] {Message}\r\n{Cause}");
        }

        public string ToString(string template)
        {
            var format = GetOrAddTemplate(template, t => template
                .Replace("{LogLevel", "{0")
                .Replace("{Timestamp", "{1")
                .Replace("{ThreadId", "{2")
                .Replace("{LogSource", "{3")
                .Replace("{LogClass", "{4")
                .Replace("{Message", "{5")
                .Replace("{Cause","{6"));            

            var logLevel = LogLevel().ToString().Replace("Level", "").ToUpperInvariant();
            var threadId = Thread.ManagedThreadId.ToString().PadLeft(4, '0');
            var causeStr = Cause == null ? "Unknown" : Cause.ToString();
            return String.Format(format, logLevel, Timestamp, threadId, LogSource, LogClass, Message,causeStr);
        }
    }
}

