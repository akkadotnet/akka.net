//-----------------------------------------------------------------------
// <copyright file="Error.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            var cause = Cause;
            var causeStr = cause == null ? "Unknown" : cause.ToString();
            var errorStr = string.Format("[{0}][{1}][Thread {2}][{3}] {4}{5}Cause: {6}",
                LogLevel().ToString().Replace("Level", "").ToUpperInvariant(), Timestamp,
                Thread.ManagedThreadId.ToString().PadLeft(4, '0'), LogSource, Message, Environment.NewLine, causeStr);
            return errorStr;
        }
    }
}

