//-----------------------------------------------------------------------
// <copyright file="ILoggingAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    public static class LoggingExtensions
    {
        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Debug(this ILoggingAdapter log, string format, params object[] args)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Debug(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Info(this ILoggingAdapter log, string format, params object[] args)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Info(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Warning(this ILoggingAdapter log, string format, params object[] args)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Warning(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Error(this ILoggingAdapter log, string format, params object[] args)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Error(this ILoggingAdapter log, Exception cause, string format, params object[] args)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, args);
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
        /// Determines whether a specific log level is enabled.
        /// </summary>
        /// <param name="logLevel">The log level that is being checked.</param>
        /// <returns><c>true</c> if the specified level is enabled; otherwise <c>false</c>.</returns>
        bool IsEnabled(LogLevel logLevel);

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Log(LogLevel logLevel, string format, params object[] args);

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="cause">The exception that caused this log message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Log(LogLevel logLevel, Exception cause, string format, params object[] args);

        void Log(LogLevel logLevel, string format, Exception cause = null);

        void Log<T1>(LogLevel logLevel, string format, T1 arg1, Exception cause = null);
        void Log<T1, T2>(LogLevel logLevel, string format, T1 arg1, T2 arg2, Exception cause = null);

        void Log<T1, T2, T3>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, Exception cause = null);

        void Log<T1, T2, T3, T4>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            Exception cause = null);

        void Log<T1, T2, T3, T4, T5>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5,
            Exception cause = null);

        void Log<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5,
            T6 arg6, Exception cause = null);
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

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Log(LogLevel logLevel, string format, params object[] args)
        {
        }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="cause">The exception that caused this log message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Log(LogLevel logLevel, Exception cause, string format, params object[] args)
        {
        }

        public void Log(LogLevel logLevel, string format, Exception cause = null)
        {
        }

        public void Log<T1>(LogLevel logLevel, string format, T1 arg1, Exception cause = null)
        {
        }

        public void Log<T1, T2>(LogLevel logLevel, string format, T1 arg1, T2 arg2, Exception cause = null)
        {
        }

        public void Log<T1, T2, T3>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, Exception cause = null)
        {
        }

        public void Log<T1, T2, T3, T4>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            Exception cause = null)
        {
        }

        public void Log<T1, T2, T3, T4, T5>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            T5 arg5,
            Exception cause = null)
        {
        }

        public void Log<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4,
            T5 arg5, T6 arg6,
            Exception cause = null)
        {
        }
    }
}