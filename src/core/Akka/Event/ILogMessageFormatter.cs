//-----------------------------------------------------------------------
// <copyright file="ILogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    /// <summary>
    /// This interface describes the methods used to format log messages.
    /// </summary>
    public interface ILogMessageFormatter
    {
        /// <summary>
        /// Formats a specified composite string using an optional list of item substitutions.
        /// </summary>
        /// <param name="format">The string that is being formatted.</param>
        /// <param name="args">An optional list of items used to format the string.</param>
        /// <returns>The given string that has been correctly formatted.</returns>
        string Format(string format, params object[] args);
    }
}
