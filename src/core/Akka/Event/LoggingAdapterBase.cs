//-----------------------------------------------------------------------
// <copyright file="LoggingAdapterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Event
{
    /// <summary>
    /// This class represents the base logging adapter implementation used to log events within the system.
    /// </summary>
    public abstract class LoggingAdapterBase : ILoggingAdapter
    {
        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
        /// </summary>
        public abstract bool IsDebugEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
        /// </summary>
        public abstract bool IsErrorEnabled { get; }

        public LogSource Source { get; }

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
        /// <param name="source">The producer of log events for this <see cref="ILoggingAdapter"/>.</param>
        protected LoggingAdapterBase(LogSource source)
        {
            Source = source;
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

        public void Log<TState>(LogLevel logLevel, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var logEvent = new LogEntry<TState>(logLevel, state, formatter, Source,
                Thread.CurrentThread.ManagedThreadId, DateTime.UtcNow, exception);
            
            NotifyLog(logEvent);
        }

        /// <summary>
        /// Notifies all subscribers that a log event has occurred.
        /// </summary>
        /// <param name="entry">The <see cref="LogEntry{TState}"/></param>
        /// <typeparam name="TState">The type of log state</typeparam>
        protected abstract void NotifyLog<TState>(in LogEntry<TState> entry);
    }
}
