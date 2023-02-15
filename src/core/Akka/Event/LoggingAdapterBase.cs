//-----------------------------------------------------------------------
// <copyright file="LoggingAdapterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly ILogMessageFormatter _logMessageFormatter;

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
            _logMessageFormatter = logMessageFormatter ?? throw new ArgumentNullException(nameof(logMessageFormatter), "The message formatter must not be null.");
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

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public virtual void Log(LogLevel logLevel, string format, params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                NotifyLog(logLevel, format);
            }
            else
            {
                NotifyLog(logLevel, new DefaultLogMessage(_logMessageFormatter, format, args));
            }
        }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public virtual void Log(LogLevel logLevel, Exception cause, string format, params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                NotifyLog(logLevel, format, cause);
            }
            else
            {
                NotifyLog(logLevel, new DefaultLogMessage(_logMessageFormatter, format, args), cause);
            }
        }

        public void Log(LogLevel logLevel, string format, Exception cause = null)
        {
            NotifyLog(logLevel, format, cause);
        }

        public void Log<T1>(LogLevel logLevel, string format, T1 arg1, Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1>>(_logMessageFormatter, format, new LogValues<T1>(arg1)), cause);
        }

        public void Log<T1, T2>(LogLevel logLevel, string format, T1 arg1, T2 arg2, Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1, T2>>(_logMessageFormatter, format, new LogValues<T1, T2>(arg1, arg2)), cause);
        }

        public void Log<T1, T2, T3>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1, T2, T3>>(_logMessageFormatter, format, new LogValues<T1, T2, T3>(arg1, arg2, arg3)), cause);
        }

        public void Log<T1, T2, T3, T4>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1, T2, T3, T4>>(_logMessageFormatter, format, new LogValues<T1, T2, T3, T4>(arg1, arg2, arg3, arg4)), cause);
        }

        public void Log<T1, T2, T3, T4, T5>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5,
            Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1, T2, T3, T4, T5>>(_logMessageFormatter, format, new LogValues<T1, T2, T3, T4, T5>(arg1, arg2, arg3, arg4, arg5)), cause);
        }

        public void Log<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6,
            Exception cause = null)
        {
            NotifyLog(logLevel, new LogMessage<LogValues<T1, T2, T3, T4, T5, T6>>(_logMessageFormatter, format, new LogValues<T1, T2, T3, T4, T5, T6>(arg1, arg2, arg3, arg4, arg5, arg6)), cause);
        }
    }
}
