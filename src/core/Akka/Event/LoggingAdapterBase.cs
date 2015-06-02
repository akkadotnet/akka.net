//-----------------------------------------------------------------------
// <copyright file="LoggingAdapterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// Represents a base logging adapter implementation which can be used by logging adapter implementations.
    /// </summary>
    public abstract class LoggingAdapterBase : ILoggingAdapter
    {
        private readonly ILogMessageFormatter _logMessageFormatter;

        public abstract bool IsDebugEnabled { get; }
        public abstract bool IsErrorEnabled { get; }
        public abstract bool IsInfoEnabled { get; }
        public abstract bool IsWarningEnabled { get; }

        protected abstract void NotifyError(object message);
        protected abstract void NotifyError(Exception cause, object message);
        protected abstract void NotifyWarning(object message);
        protected abstract void NotifyInfo(object message);
        protected abstract void NotifyDebug(object message);

        /// <summary>
        /// Creates an instance of the LoggingAdapterBase.
        /// </summary>
        /// <param name="logMessageFormatter">The log message formatter used by this logging adapter.</param>
        /// <exception cref="ArgumentException"></exception>
        protected LoggingAdapterBase(ILogMessageFormatter logMessageFormatter)
        {
            if(logMessageFormatter == null)
                throw new ArgumentException("logMessageFormatter");

            _logMessageFormatter = logMessageFormatter;
        }
        
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
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }

        /// <summary>
        /// Handles logging a log event for a particular level if that level is enabled. 
        /// </summary>
        /// <param name="logLevel">The log level of the log event.</param>
        /// <param name="message">The log message of the log event.</param>
        /// <exception cref="NotSupportedException"></exception>
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
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }
        
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

        public void Warn(string format, params object[] args)
        {
            Warning(format, args);
        }

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

