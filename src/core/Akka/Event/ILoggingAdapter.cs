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
        public static void Log(this ILoggingAdapter log, LogLevel level, string format)
        {
            log.Log(level, null, format);
        }
        
        public static void Debug(this ILoggingAdapter log, string format)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, null, format);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        public static void Debug<T1>(this ILoggingAdapter log, string format, T1 arg1)
        {
            log.Debug<T1>(null, format, arg1);
        }

        public static void Debug<T1>(this ILoggingAdapter log, Exception cause, string format, T1 arg1)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1);
        }

        public static void Debug<T1, T2>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2)
        {
            log.Debug<T1, T2>(null, format, arg1, arg2);
        }

        public static void Debug<T1, T2>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1, arg2);
        }

        public static void Debug<T1, T2, T3>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            log.Debug<T1, T2, T3>(null, format, arg1, arg2, arg3);
        }

        public static void Debug<T1, T2, T3>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1, arg2, arg3);
        }

        public static void Debug<T1, T2, T3, T4>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4)
        {
            log.Debug<T1, T2, T3, T4>(null, format, arg1, arg2, arg3, arg4);
        }

        public static void Debug<T1, T2, T3, T4>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1, arg2, arg3, arg4);
        }

        public static void Debug<T1, T2, T3, T4, T5>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5)
        {
            log.Debug<T1, T2, T3, T4, T5>(null, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Debug<T1, T2, T3, T4, T5>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Debug<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            log.Debug<T1, T2, T3, T4, T5, T6>(null, format, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        public static void Debug<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, Exception cause, string format,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (!log.IsDebugEnabled)
                return;

            log.Log(LogLevel.DebugLevel, cause, format, arg1, arg2, arg3, arg4, arg5, arg6);
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

        /* END DEBUG */

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        public static void Info(this ILoggingAdapter log, string format)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, null, format);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        public static void Info<T1>(this ILoggingAdapter log, string format, T1 arg1)
        {
            log.Info<T1>(null, format, arg1);
        }

        public static void Info<T1>(this ILoggingAdapter log, Exception cause, string format, T1 arg1)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1);
        }

        public static void Info<T1, T2>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2)
        {
            log.Info<T1, T2>(null, format, arg1, arg2);
        }

        public static void Info<T1, T2>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1, arg2);
        }

        public static void Info<T1, T2, T3>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            log.Info<T1, T2, T3>(null, format, arg1, arg2, arg3);
        }

        public static void Info<T1, T2, T3>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1, arg2, arg3);
        }

        public static void Info<T1, T2, T3, T4>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4)
        {
            log.Info<T1, T2, T3, T4>(null, format, arg1, arg2, arg3, arg4);
        }

        public static void Info<T1, T2, T3, T4>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1, arg2, arg3, arg4);
        }

        public static void Info<T1, T2, T3, T4, T5>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5)
        {
            log.Info<T1, T2, T3, T4, T5>(null, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Info<T1, T2, T3, T4, T5>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Info<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            log.Info<T1, T2, T3, T4, T5, T6>(null, format, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        public static void Info<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, Exception cause, string format,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.InfoLevel, cause, format, arg1, arg2, arg3, arg4, arg5, arg6);
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
        
        /* BEGIN WARNING */

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Warning(this ILoggingAdapter log, string format)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, null, format);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        public static void Warning<T1>(this ILoggingAdapter log, string format, T1 arg1)
        {
            log.Warning<T1>(null, format, arg1);
        }

        public static void Warning<T1>(this ILoggingAdapter log, Exception cause, string format, T1 arg1)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1);
        }

        public static void Warning<T1, T2>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2)
        {
            log.Warning<T1, T2>(null, format, arg1, arg2);
        }

        public static void Warning<T1, T2>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1, arg2);
        }

        public static void Warning<T1, T2, T3>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            log.Warning<T1, T2, T3>(null, format, arg1, arg2, arg3);
        }

        public static void Warning<T1, T2, T3>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1, arg2, arg3);
        }

        public static void Warning<T1, T2, T3, T4>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4)
        {
            log.Warning<T1, T2, T3, T4>(null, format, arg1, arg2, arg3, arg4);
        }

        public static void Warning<T1, T2, T3, T4>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1, arg2, arg3, arg4);
        }

        public static void Warning<T1, T2, T3, T4, T5>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5)
        {
            log.Warning<T1, T2, T3, T4, T5>(null, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Warning<T1, T2, T3, T4, T5>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Warning<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            log.Warning<T1, T2, T3, T4, T5, T6>(null, format, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        public static void Warning<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, Exception cause, string format,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (!log.IsWarningEnabled)
                return;

            log.Log(LogLevel.WarningLevel, cause, format, arg1, arg2, arg3, arg4, arg5, arg6);
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
        
        /* BEGIN ERROR */

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public static void Error(this ILoggingAdapter log, string format)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, null, format);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        public static void Error<T1>(this ILoggingAdapter log, string format, T1 arg1)
        {
            log.Error<T1>(null, format, arg1);
        }

        public static void Error<T1>(this ILoggingAdapter log, Exception cause, string format, T1 arg1)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1);
        }

        public static void Error<T1, T2>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2)
        {
            log.Error<T1, T2>(null, format, arg1, arg2);
        }

        public static void Error<T1, T2>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1, arg2);
        }

        public static void Error<T1, T2, T3>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            log.Error<T1, T2, T3>(null, format, arg1, arg2, arg3);
        }

        public static void Error<T1, T2, T3>(this ILoggingAdapter log, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1, arg2, arg3);
        }

        public static void Error<T1, T2, T3, T4>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4)
        {
            log.Error<T1, T2, T3, T4>(null, format, arg1, arg2, arg3, arg4);
        }

        public static void Error<T1, T2, T3, T4>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4)
        {
            if (!log.IsInfoEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1, arg2, arg3, arg4);
        }

        public static void Error<T1, T2, T3, T4, T5>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5)
        {
            log.Error<T1, T2, T3, T4, T5>(null, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Error<T1, T2, T3, T4, T5>(this ILoggingAdapter log, Exception cause, string format, T1 arg1,
            T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1, arg2, arg3, arg4, arg5);
        }

        public static void Error<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            log.Error<T1, T2, T3, T4, T5, T6>(null, format, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        public static void Error<T1, T2, T3, T4, T5, T6>(this ILoggingAdapter log, Exception cause, string format,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (!log.IsErrorEnabled)
                return;

            log.Log(LogLevel.ErrorLevel, cause, format, arg1, arg2, arg3, arg4, arg5, arg6);
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

        // /// <summary>
        // /// Logs a message with a specified level.
        // /// </summary>
        // /// <param name="logLevel">The level used to log the message.</param>
        // /// <param name="format">The message that is being logged.</param>
        // /// <param name="args">An optional list of items used to format the message.</param>
        // void Log(LogLevel logLevel, string format, params object[] args);

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="cause">The exception that caused this log message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Log(LogLevel logLevel, Exception cause, string format);
        
        /// <summary>
        /// For backwards compatibility.
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="cause"></param>
        /// <param name="format"></param>
        /// <param name="args"></param>
        void Log(LogLevel logLevel, Exception cause, string format, params object[] args);
        
        /// <summary>
        /// For backwards compatibility.
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="format"></param>
        /// <param name="args"></param>
        void Log(LogLevel logLevel, string format, params object[] args);

        void Log<T1>(LogLevel logLevel, Exception cause, string format, T1 arg1);
        void Log<T1, T2>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2);

        void Log<T1, T2, T3>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3);

        void Log<T1, T2, T3, T4>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4);

        void Log<T1, T2, T3, T4, T5>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5);

        void Log<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4, T5 arg5,
            T6 arg6);
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

        public void Log(LogLevel logLevel, Exception cause, string format)
        {
        }

        public void Log(LogLevel logLevel, Exception cause, string format, params object[] args)
        {
            
        }

        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            
        }

        public void Log<T1>(LogLevel logLevel, Exception cause, string format, T1 arg1)
        {
        }

        public void Log<T1, T2>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2)
        {
        }

        public void Log<T1, T2, T3>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3)
        {
        }

        public void Log<T1, T2, T3, T4>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2, T3 arg3,
            T4 arg4)
        {
        }

        public void Log<T1, T2, T3, T4, T5>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4,
            T5 arg5)
        {
        }

        public void Log<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, Exception cause, string format, T1 arg1, T2 arg2,
            T3 arg3, T4 arg4,
            T5 arg5, T6 arg6)
        {
        }
    }
}