//-----------------------------------------------------------------------
// <copyright file="DefaultLogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    /// <summary>
    /// Default implementation of the ILogMessageFormatter that uses string.Format to format a log message.
    /// </summary>
    public class DefaultLogMessageFormatter : ILogMessageFormatter
    {
        /// <summary>
        /// Formats the log message using string.Format providing the format and specified args.
        /// </summary>
        /// <param name="format">The format string of the message.</param>
        /// <param name="args">The arguments used to format the message.</param>
        /// <returns></returns>
        public string Format(string format, params object[] args)
        {
            return string.Format(format, args);
        }
    }
}

