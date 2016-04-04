//-----------------------------------------------------------------------
// <copyright file="Info.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// Represents an Info log event.
    /// </summary>
    public class Info : LogEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Info" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Info(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }
    }
}

