//-----------------------------------------------------------------------
// <copyright file="ILoggingAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
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
        /// Logs a <see cref="LogLevel.DebugLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Debug(string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Debug(Exception cause, string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Info(string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Info(Exception cause, string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Warning(string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Warning(Exception cause, string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Error(string format, params object[] args);

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel"/> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        void Error(Exception cause, string format, params object[] args);

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
        private NoLogger() { }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
        /// </summary>
        public bool IsDebugEnabled { get { return false; } }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.InfoLevel" /> is enabled.
        /// </summary>
        public bool IsInfoEnabled { get { return false; } }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.WarningLevel" /> is enabled.
        /// </summary>
        public bool IsWarningEnabled { get { return false; } }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
        /// </summary>
        public bool IsErrorEnabled { get { return false; } }

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
        /// Logs a <see cref="LogLevel.DebugLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Debug(string format, params object[] args) { }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel" /> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Debug(Exception cause, string format, params object[] args){ }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Info(string format, params object[] args) { }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel" /> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Info(Exception cause, string format, params object[] args){ }

        /// <summary>
        /// Obsolete. Use <see cref="Warning(string, object[])" /> instead!
        /// </summary>
        /// <param name="format">N/A</param>
        /// <param name="args">N/A</param>
        public void Warn(string format, params object[] args) { }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Warning(string format, params object[] args) { }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel" /> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Warning(Exception cause, string format, params object[] args){ }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Error(string format, params object[] args) { }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel" /> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Error(Exception cause, string format, params object[] args) { }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Log(LogLevel logLevel, string format, params object[] args) { }

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
    }
}
