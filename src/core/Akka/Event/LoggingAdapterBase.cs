//-----------------------------------------------------------------------
// <copyright file="LoggingAdapterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// Notifies all subscribers that an <see cref="LogLevel.ErrorLevel" /> log event occurred.
        /// </summary>
        /// <param name="message">The message related to the log event.</param>
        protected abstract void NotifyError(object message);

        /// <summary>
        /// Notifies all subscribers that an <see cref="LogLevel.ErrorLevel" /> log event occurred.
        /// </summary>
        /// <param name="cause">The exception that caused the log event.</param>
        /// <param name="message">The message related to the log event.</param>
        protected abstract void NotifyError(Exception cause, object message);

        /// <summary>
        /// Notifies all subscribers that an <see cref="LogLevel.WarningLevel" /> log event occurred.
        /// </summary>
        /// <param name="message">The message related to the log event.</param>
        protected abstract void NotifyWarning(object message);

        /// <summary>
        /// Notifies all subscribers that an <see cref="LogLevel.InfoLevel" /> log event occurred.
        /// </summary>
        /// <param name="message">The message related to the log event.</param>
        protected abstract void NotifyInfo(object message);

        /// <summary>
        /// Notifies all subscribers that an <see cref="LogLevel.DebugLevel" /> log event occurred.
        /// </summary>
        /// <param name="message">The message related to the log event.</param>
        protected abstract void NotifyDebug(object message);

        /// <summary>
        /// Creates an instance of the LoggingAdapterBase.
        /// </summary>
        /// <param name="logMessageFormatter">The log message formatter used by this logging adapter.</param>
        /// <exception cref="ArgumentNullException">This exception is thrown when the given <paramref name="logMessageFormatter"/> is undefined.</exception>
        protected LoggingAdapterBase(ILogMessageFormatter logMessageFormatter)
        {
            if(logMessageFormatter == null)
                throw new ArgumentNullException(nameof(logMessageFormatter), "The message formatter must not be null.");

            _logMessageFormatter = logMessageFormatter;
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
        /// <exception cref="NotSupportedException">This exception is thrown when the given <paramref name="logLevel"/> is unknown.</exception>
        protected void NotifyLog(LogLevel logLevel, object message)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    if(IsDebugEnabled) NotifyDebug(message);
                    break;
                case LogLevel.InfoLevel:
                    if(IsInfoEnabled) NotifyInfo(message);
                    break;
                case LogLevel.WarningLevel:
                    if(IsWarningEnabled) NotifyWarning(message);
                    break;
                case LogLevel.ErrorLevel:
                    if(IsErrorEnabled) NotifyError(message);
                    break;
                default:
                    throw new NotSupportedException($"Unknown LogLevel {logLevel}");
            }
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.DebugLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Debug(string format, params object[] args)
        {
            if (!IsDebugEnabled) 
                return;

            if (args == null || args.Length == 0)
            {
                NotifyDebug(format);
            }
            else
            {
                NotifyDebug(new LogMessage(_logMessageFormatter, format, args));
            }
        }

        /// <summary>
        /// Obsolete. Use <see cref="Warning" /> instead!
        /// </summary>
        /// <param name="format">N/A</param>
        /// <param name="args">N/A</param>
        public void Warn(string format, params object[] args)
        {
            Warning(format, args);
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.WarningLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Warning(string format, params object[] args)
        {
            if (!IsWarningEnabled) 
                return;

            if (args == null || args.Length == 0)
            {
                NotifyWarning(format);
            }
            else
            {
                NotifyWarning(new LogMessage(_logMessageFormatter, format, args));
            }
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel" /> message and associated exception.
        /// </summary>
        /// <param name="cause">The exception associated with this message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Error(Exception cause, string format, params object[] args)
        {
            if (!IsErrorEnabled) 
                return;

            if (args == null || args.Length == 0)
            {
                NotifyError(cause, format);
            }
            else
            {
                NotifyError(cause, new LogMessage(_logMessageFormatter, format, args));
            }
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.ErrorLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Error(string format, params object[] args)
        {
            if (!IsErrorEnabled) 
                return;

            if (args == null || args.Length == 0)
            {
                NotifyError(format);
            }
            else
            {
                NotifyError(new LogMessage(_logMessageFormatter, format, args));
            }
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.InfoLevel" /> message.
        /// </summary>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Info(string format, params object[] args)
        {
            if (!IsInfoEnabled) 
                return;

            if (args == null || args.Length == 0)
            {
                NotifyInfo(format);
            }
            else
            {
                NotifyInfo(new LogMessage(_logMessageFormatter, format, args)); 
            }
        }

        /// <summary>
        /// Logs a message with a specified level.
        /// </summary>
        /// <param name="logLevel">The level used to log the message.</param>
        /// <param name="format">The message that is being logged.</param>
        /// <param name="args">An optional list of items used to format the message.</param>
        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                NotifyLog(logLevel, format);
            }
            else
            {
                NotifyLog(logLevel, new LogMessage(_logMessageFormatter, format, args));
            }
        }
    }
}
