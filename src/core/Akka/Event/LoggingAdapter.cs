using System;

namespace Akka.Event
{
    /// <summary>
    ///     Class LoggingAdapter.
    /// </summary>
    public abstract class LoggingAdapter
    {
        private readonly ILogMessageFormatter _logMessageFormatter;

        /// <summary>
        ///     The is debug enabled
        /// </summary>
        public abstract bool IsDebugEnabled { get; }

        /// <summary>
        ///     The is error enabled
        /// </summary>
        public abstract bool IsErrorEnabled { get; }

        /// <summary>
        ///     The is information enabled
        /// </summary>
        public abstract bool IsInfoEnabled { get; }

        /// <summary>
        ///     The is warning enabled
        /// </summary>
        public abstract bool IsWarningEnabled { get; }

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(object message);

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(Exception cause, object message);

        /// <summary>
        ///     Notifies the warning.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyWarning(object message);

        /// <summary>
        ///     Notifies the information.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyInfo(object message);

        /// <summary>
        ///     Notifies the debug.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyDebug(object message);

        protected LoggingAdapter(ILogMessageFormatter logMessageFormatter)
        {
            if (logMessageFormatter == null)
                throw new ArgumentException("logMessageFormatter");

            _logMessageFormatter = logMessageFormatter;
        }

        /// <summary>
        ///     Determines whether the specified log level is enabled.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns><c>true</c> if the specified log level is enabled; otherwise, <c>false</c>.</returns>
        /// <exception cref="System.NotSupportedException">Unknown LogLevel  + logLevel</exception>
        protected bool IsEnabled(LogLevel logLevel)
        {
            switch (logLevel)
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
        ///     Notifies the log.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="message">The message.</param>
        /// <exception cref="System.NotSupportedException">Unknown LogLevel  + logLevel</exception>
        protected void NotifyLog(LogLevel logLevel, object message)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    if (IsDebugEnabled) NotifyDebug(message);
                    break;
                case LogLevel.InfoLevel:
                    if (IsInfoEnabled) NotifyInfo(message);
                    break;
                case LogLevel.WarningLevel:
                    if (IsWarningEnabled) NotifyWarning(message);
                    break;
                case LogLevel.ErrorLevel:
                    if (IsErrorEnabled) NotifyError(message);
                    break;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }

        /// <summary>
        ///     Debugs the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Debug(string message)
        {
            if (IsDebugEnabled)
                NotifyDebug(message);
        }

        /// <summary>
        ///    Logs a <see cref="Warning"/> message.
        /// </summary>
        /// <param name="message">The message.</param>
        [Obsolete("Use Warning instead")]
        public void Warn(string message)
        {
           Warning(message);
        }

        /// <summary>Logs a <see cref="Akka.Event"/> message.</summary>
        /// <param name="message">The message.</param>
        public void Warning(string message)
        {
            if(IsWarningEnabled)
                NotifyWarning(message);
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        public void Error(Exception cause, string message)
        {
            if (IsErrorEnabled)
                NotifyError(cause, message);
        }

        /// <summary>
        ///     Errors the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Error(string message)
        {
            if (IsErrorEnabled)
                NotifyError(message);
        }

        /// <summary>
        ///     Informations the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Info(string message)
        {
            if (IsInfoEnabled)
                NotifyInfo(message);
        }

        /// <summary>
        ///     Debugs the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Debug(string format, params object[] args)
        {
            if (IsDebugEnabled)
                NotifyDebug(new LogMessage(_logMessageFormatter, format, args));
        }

        /// <summary>
        ///     Warns the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        [Obsolete("Use Warning instead")]
        public void Warn(string format, params object[] args)
        {
            Warning(format, args);
        }

        /// <summary>Logs a <see cref="Akka.Event"/> message.</summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Warning(string format, params object[] args)
        {
            if(IsWarningEnabled)
                NotifyWarning(new LogMessage(_logMessageFormatter, format, args));
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(Exception cause, string format, params object[] args)
        {
            if (IsErrorEnabled)
                NotifyError(cause, new LogMessage(_logMessageFormatter, format, args));
        }

        /// <summary>
        ///     Errors the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(string format, params object[] args)
        {
            if (IsErrorEnabled)
                NotifyError(new LogMessage(_logMessageFormatter, format, args));
        }

        /// <summary>
        ///     Informations the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Info(string format, params object[] args)
        {
            if (IsInfoEnabled)
                NotifyInfo(new LogMessage(_logMessageFormatter, format, args));
        }

        /// <summary>
        ///     Logs the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="message">The message.</param>
        public void Log(LogLevel logLevel, string message)
        {
            NotifyLog(logLevel, message);
        }

        /// <summary>
        ///     Logs the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            NotifyLog(logLevel, new LogMessage(_logMessageFormatter, format, args));
        }
    }
}
