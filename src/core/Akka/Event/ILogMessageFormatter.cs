//-----------------------------------------------------------------------
// <copyright file="ILogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    /// <summary>
    /// Represents a log message formatter, these are used to format log messages based on a string format and an array of format args.
    /// </summary>
    public interface ILogMessageFormatter
    {
        /// <summary>
        /// Format the specified format string using the format args.
        /// </summary>
        /// <param name="format">The format string of the message.</param>
        /// <param name="args">The format args used to format the message.</param>
        /// <returns></returns>
        string Format(string format, params object[] args);
    }
}

