//-----------------------------------------------------------------------
// <copyright file="ILoggingAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    public static class LoggingAdapterExtensions
    {
        internal static readonly Func<UntypedLogEntryState, Exception, string> ParamsFormatter = (state, exception) =>
            exception == null
                ? string.Format(state.Format, state.Args)
                : string.Format(state.Format, state.Args) + $"{Environment.NewLine}Cause: {exception}";

        internal static readonly Func<string, Exception, string> StringOnlyFormatter = (str, ex) => ex == null
            ? str
            : str + $"{Environment.NewLine}Cause: {ex}";

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message.
        /// </summary>
        /// <param name="log">The <see cref="ILoggingAdapter"/>.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Debug(this ILoggingAdapter log, string format, params object[] args)
        {
            Debug(log, null, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message and associated exception.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Debug(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            Log(log, LogLevel.DebugLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Info(this ILoggingAdapter log, string format, params object[] args)
        {
            Info(log, null, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Info(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            Log(log, LogLevel.InfoLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Warning(this ILoggingAdapter log, string format, params object[] args)
        {
            Warning(log, null, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Warning(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            Log(log, LogLevel.WarningLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Error(this ILoggingAdapter log, string format, params object[] args)
        {
            Error(log, null, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Error(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            Log(log, LogLevel.ErrorLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Log(this ILoggingAdapter log, LogLevel logLevel, string format, params object[] args)
        {
            Log(log, logLevel, null, format, args);
        }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="cause">The exception that caused this log message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Log(this ILoggingAdapter log, LogLevel logLevel, Exception cause, string format,
            params object[] args)
        {
            if (!log.IsEnabled(logLevel))
                return;
            
            log.Log(logLevel, new UntypedLogEntryState(format, args), cause, ParamsFormatter);
        }
    }
    
    /// <summary>
        /// This interface describes the methods used to log events within the system.
        /// </summary>
        public interface ILoggingAdapter
        {
            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.DebugLevel"/> is enabled.
            /// </summary>
            bool IsDebugEnabled { get; }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.InfoLevel"/> is enabled.
            /// </summary>
            bool IsInfoEnabled { get; }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.WarningLevel"/> is enabled.
            /// </summary>
            bool IsWarningEnabled { get; }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.ErrorLevel"/> is enabled.
            /// </summary>
            bool IsErrorEnabled { get; }

            /// <summary>
            /// The source of the logs produced by this <see cref="ILoggingAdapter"/>.
            /// </summary>
            LogSource Source { get; }

            /// <summary>
            /// Determines whether a specific log level is enabled.
            /// </summary>
            /// <param name="logLevel">The log level that is being checked.</param>
            /// <returns><c>true</c> if the specified level is enabled; otherwise <c>false</c>.</returns>
            bool IsEnabled(LogLevel logLevel);

            /// <summary>
            /// Creates a single log event to the logging infrastructure.
            /// </summary>
            /// <param name="logLevel">The level for this log.</param>
            /// <param name="state">The state used to structure this log.</param>
            /// <param name="exception">Optional. The exception included in this log.</param>
            /// <param name="formatter">Formatting function to render this <see cref="LogEntry{TState}"/> as a <c>string</c>.</param>
            /// <typeparam name="TState">The type of state.</typeparam>
            void Log<TState>(LogLevel logLevel, TState state, Exception exception,
                Func<TState, Exception, string> formatter);
        }

        /// <summary>
        /// This class represents an <see cref="ILoggingAdapter"/> implementation used when messages are to be dropped instead of logged.
        /// </summary>
        public sealed class NoLogger : ILoggingAdapter
        {
            /// <summary>
            /// Retrieves a singleton instance of the <see cref="NoLogger"/> class.
            /// </summary>
            public static readonly ILoggingAdapter Instance = new NoLogger();

            private NoLogger()
            {
            }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
            /// </summary>
            public bool IsDebugEnabled
            {
                get { return false; }
            }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.InfoLevel" /> is enabled.
            /// </summary>
            public bool IsInfoEnabled
            {
                get { return false; }
            }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.WarningLevel" /> is enabled.
            /// </summary>
            public bool IsWarningEnabled
            {
                get { return false; }
            }

            /// <summary>
            /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
            /// </summary>
            public bool IsErrorEnabled
            {
                get { return false; }
            }

            public LogSource Source { get; } = LogSource.Create(typeof(NoLogger));

            /// <summary>
            /// Determines whether a specific log level is enabled.
            /// </summary>
            /// <param name="logLevel">The log level that is being checked.</param>
            /// <returns>
            ///   <c>true</c> if the specified level is enabled; otherwise <c>false</c>.
            /// </returns>
            public bool IsEnabled(LogLevel logLevel)
            {
                return false;
            }

            /// <inheritdoc cref="ILoggingAdapter.Log{TState}"/>
            public void Log<TState>(LogLevel logLevel, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                // no-op
            }
        }
}
