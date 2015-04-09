//-----------------------------------------------------------------------
// <copyright file="Debug.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    ///     Class Debug.
    /// </summary>
    public class Debug : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Debug" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Debug(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }
    }
}

