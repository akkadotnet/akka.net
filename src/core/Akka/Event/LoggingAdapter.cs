using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /// <summary>
    ///     Class LoggingAdapter.
    /// </summary>
    public abstract class LoggingAdapter
    {
        /// <summary>
        ///     The is debug enabled
        /// </summary>
        protected bool isDebugEnabled;

        /// <summary>
        ///     The is error enabled
        /// </summary>
        protected bool isErrorEnabled;

        /// <summary>
        ///     The is information enabled
        /// </summary>
        protected bool isInfoEnabled;

        /// <summary>
        ///     The is warning enabled
        /// </summary>
        protected bool isWarningEnabled;

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(FormattedLogMessage message);

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(Exception cause, FormattedLogMessage message);

        /// <summary>
        ///     Notifies the warning.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyWarning(FormattedLogMessage message);

        /// <summary>
        ///     Notifies the information.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyInfo(FormattedLogMessage message);

        /// <summary>
        ///     Notifies the debug.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyDebug(FormattedLogMessage message);

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
                    return isDebugEnabled;
                case LogLevel.InfoLevel:
                    return isInfoEnabled;
                case LogLevel.WarningLevel:
                    return isWarningEnabled;
                case LogLevel.ErrorLevel:
                    return isErrorEnabled;
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
        protected void NotifyLog(LogLevel logLevel, FormattedLogMessage message)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    if (isDebugEnabled) NotifyDebug(message);
                    break;
                case LogLevel.InfoLevel:
                    if (isInfoEnabled) NotifyInfo(message);
                    break;
                case LogLevel.WarningLevel:
                    if (isWarningEnabled) NotifyWarning(message);
                    break;
                case LogLevel.ErrorLevel:
                    if (isErrorEnabled) NotifyError(message);
                    break;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }

        /// <summary>
        ///     Debugs the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Debug(FormattedLogMessage message)
        {
            if (isDebugEnabled)
                NotifyDebug(message);
        }

        /// <summary>
        ///    Logs a <see cref="Warning"/> message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Warn(FormattedLogMessage message)
        {
           Warning(message);
        }

        /// <summary>Logs a <see cref="Akka.Event"/> message.</summary>
        /// <param name="message">The message.</param>
        public void Warning(FormattedLogMessage message)
        {
            if(isWarningEnabled)
                NotifyWarning(message);
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        public void Error(Exception cause, FormattedLogMessage message)
        {
            if (isErrorEnabled)
                NotifyError(cause, message);
        }

        /// <summary>
        ///     Errors the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Error(FormattedLogMessage message)
        {
            if (isErrorEnabled)
                NotifyError(message);
        }

        /// <summary>
        ///     Informations the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Info(FormattedLogMessage message)
        {
            if (isInfoEnabled)
                NotifyInfo(message);
        }


        /// <summary>
        ///     Debugs the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Debug(string format, params object[] args)
        {
            if (isDebugEnabled)
                NotifyDebug(new FormattedLogMessage(format, args));
        }

        /// <summary>
        ///     Warns the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Warn(string format, params object[] args)     //TODO: This should probably be obsolete and removed as Debug,Info,Error corresponds to LogEvent classes with the same name, so to be consistent Warning should be used instead.
        {
            Warning(format, args);
        }

        /// <summary>Logs a <see cref="Akka.Event"/> message.</summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Warning(string format, params object[] args)     //TODO: This should probably be obsolete and removed as Debug,Info,Error corresponds to LogEvent classes with the same name, so to be consistent Warning should be used instead.
        {
            if(isWarningEnabled)
                NotifyWarning(new FormattedLogMessage(format, args));
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(Exception cause, string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(cause, new FormattedLogMessage(format, args));
        }

        /// <summary>
        ///     Errors the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(new FormattedLogMessage(format, args));
        }

        /// <summary>
        ///     Informations the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Info(string format, params object[] args)
        {
            if (isInfoEnabled)
                NotifyInfo(new FormattedLogMessage(format, args));
        }

        /// <summary>
        ///     Logs the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="message">The message.</param>
        public void Log(LogLevel logLevel, FormattedLogMessage message)
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
            NotifyLog(logLevel, new FormattedLogMessage(format, args));
        }
    }
}
