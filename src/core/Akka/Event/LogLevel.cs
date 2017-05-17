//-----------------------------------------------------------------------
// <copyright file="LogLevel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    /// <summary>
    /// Enumeration representing the various log levels in the system.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// The debug log level.
        /// </summary>
        DebugLevel,

        /// <summary>
        /// The information log level.
        /// </summary>
        InfoLevel,

        /// <summary>
        /// The warning log level.
        /// </summary>
        WarningLevel,

        /// <summary>
        /// The error log level.
        /// </summary>
        ErrorLevel,
    }
}

