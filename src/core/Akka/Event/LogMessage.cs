//-----------------------------------------------------------------------
// <copyright file="LogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Event
{
    /// <summary>
    /// Represents a log message which is composed of a format string and format args.
    /// </summary>
    public class LogMessage
    {
        private readonly ILogMessageFormatter _formatter;

        /// <summary>
        /// Gets the format string of this log message.
        /// </summary>
        public string Format { get; private set; }

        /// <summary>
        /// Gets the format args of this log message.
        /// </summary>
        public object[] Args { get; private set; }

        /// <summary>
        /// Initializes an instance of the LogMessage with the specified formatter, format and args.
        /// </summary>
        /// <param name="formatter">The formatter for the LogMessage.</param>
        /// <param name="format">The string format of the LogMessage.</param>
        /// <param name="args">The format args of the LogMessage.</param>
        public LogMessage(ILogMessageFormatter formatter, string format, params object[] args)
        {
            _formatter = formatter;
            Format = format;
            Args = args;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return _formatter.Format(Format, Args);
        }
    }
}

