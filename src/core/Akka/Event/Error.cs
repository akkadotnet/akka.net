﻿//-----------------------------------------------------------------------
// <copyright file="Error.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a Error log event.
    /// </summary>
    public class Error : LogEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Error" /> class.
        /// </summary>
        /// <param name="cause">The exception that caused the log event.</param>
        /// <param name="logSource">The source that generated the log event.</param>
        /// <param name="logClass">The type of logger used to log the event.</param>
        /// <param name="message">The message that is being logged.</param>
        public Error(Exception cause, string logSource, Type logClass, object message)
        {
            Cause = cause;
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        /// Retrieves the <see cref="Akka.Event.LogLevel" /> used to classify this event.
        /// </summary>
        /// <returns>The <see cref="Akka.Event.LogLevel" /> used to classify this event.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }
    }
}
