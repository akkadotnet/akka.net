//-----------------------------------------------------------------------
// <copyright file="LoggingAdapterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// This class represents the base logging adapter implementation used to log events within the system.
    /// </summary>
    public abstract class LoggingAdapterBase : ILoggingAdapter
    {
        public ILogMessageFormatter Formatter { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
        /// </summary>
        public abstract bool IsDebugEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
        /// </summary>
        public abstract bool IsErrorEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.InfoLevel" /> is enabled.
        /// </summary>
        public abstract bool IsInfoEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.WarningLevel" /> is enabled.
        /// </summary>
        public abstract bool IsWarningEnabled { get; }


        /// <summary>
        /// Creates an instance of the LoggingAdapterBase.
        /// </summary>
        /// <param name="logMessageFormatter">The log message formatter used by this logging adapter.</param>
        /// <exception cref="ArgumentNullException">This exception is thrown when the given <paramref name="logMessageFormatter"/> is undefined.</exception>
        protected LoggingAdapterBase(ILogMessageFormatter logMessageFormatter)
        {
            Formatter = logMessageFormatter ?? throw new ArgumentNullException(nameof(logMessageFormatter), "The message formatter must not be null.");
        }

        /// <summary>
        /// Checks the logging adapter to see if the supplied <paramref name="logLevel"/> is enabled.
        /// </summary>
        /// <param name="logLevel">The log level to check if it is enabled in this logging adapter.</param>
        /// <exception cref="NotSupportedException">This exception is thrown when the given <paramref name="logLevel"/> is unknown.</exception>
        /// <returns><c>true</c> if the supplied log level is enabled; otherwise <c>false</c></returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    return IsDebugEnabled;
                case LogLevel.InfoLevel:
                    return IsInfoEnabled;
                case LogLevel.WarningLevel:
                    return IsWarningEnabled;
                case LogLevel.ErrorLevel:
                    return IsErrorEnabled;
                default:
                    throw new NotSupportedException($"Unknown LogLevel {logLevel}");
            }
        }

        /// <summary>
        /// Notifies all subscribers that a log event occurred for a particular level.
        /// </summary>
        /// <param name="logLevel">The log level associated with the log event.</param>
        /// <param name="message">The message related to the log event.</param>
        /// <param name="cause">The exception that caused the log event.</param>
        /// <exception cref="NotSupportedException">This exception is thrown when the given <paramref name="logLevel"/> is unknown.</exception>
        protected abstract void NotifyLog(LogLevel logLevel, object message, Exception cause = null);

        public void Log(LogLevel logLevel, Exception cause, LogMessage message)
        {
            NotifyLog(logLevel, message, cause);
        }

        public void Log(LogLevel logLevel, Exception cause, string format)
        {
            NotifyLog(logLevel, format, cause);
        }
    }
}
