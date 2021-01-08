//-----------------------------------------------------------------------
// <copyright file="BusLogging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// A logging adapter implementation publishing log events to the event stream.
    /// </summary>
    public class BusLogging : LoggingAdapterBase
    {
        private readonly LoggingBus _bus;
        private readonly Type _logClass;
        private readonly string _logSource;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="BusLogging" /> class.
        /// </summary>
        /// <param name="bus">The logging bus instance that messages will be published to.</param>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="logMessageFormatter">The log message formatter.</param>
        public BusLogging(LoggingBus bus, string logSource, Type logClass, ILogMessageFormatter logMessageFormatter)
            : base(logMessageFormatter)
        {
            _bus = bus;
            _logSource = logSource;
            _logClass = logClass;

            _isErrorEnabled = bus.LogLevel <= LogLevel.ErrorLevel;
            _isWarningEnabled = bus.LogLevel <= LogLevel.WarningLevel;
            _isInfoEnabled = bus.LogLevel <= LogLevel.InfoLevel;
            _isDebugEnabled = bus.LogLevel <= LogLevel.DebugLevel;
        }

        private readonly bool _isDebugEnabled;
        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
        /// </summary>
        public override bool IsDebugEnabled { get { return _isDebugEnabled; } }

        private readonly bool _isErrorEnabled;
        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
        /// </summary>
        public override bool IsErrorEnabled { get { return _isErrorEnabled; } }

        private readonly bool _isInfoEnabled;
        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.InfoLevel" /> is enabled.
        /// </summary>
        public override bool IsInfoEnabled { get { return _isInfoEnabled; } }

        private readonly bool _isWarningEnabled;
        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.WarningLevel" /> is enabled.
        /// </summary>
        public override bool IsWarningEnabled { get { return _isWarningEnabled; } }

        /// <summary>
        /// Publishes the error message onto the LoggingBus.
        /// </summary>
        /// <param name="message">The error message.</param>
        protected override void NotifyError(object message)
        {
            _bus.Publish(new Error(null, _logSource, _logClass, message));
        }

        /// <summary>
        /// Publishes the error message and exception onto the LoggingBus.
        /// </summary>
        /// <param name="cause">The exception that caused this error.</param>
        /// <param name="message">The error message.</param>
        protected override void NotifyError(Exception cause, object message)
        {
            _bus.Publish(new Error(cause, _logSource, _logClass, message));
        }

        /// <summary>
        /// Publishes the warning message onto the LoggingBus.
        /// </summary>
        /// <param name="message">The warning message.</param>
        protected override void NotifyWarning(object message)
        {
            _bus.Publish(new Warning(_logSource, _logClass, message));
        }

        protected override void NotifyWarning(Exception cause, object message)
        {
            _bus.Publish(new Warning(cause, _logSource, _logClass, message));
        }

        /// <summary>
        /// Publishes the info message onto the LoggingBus.
        /// </summary>
        /// <param name="message">The info message.</param>
        protected override void NotifyInfo(object message)
        {
            _bus.Publish(new Info(_logSource, _logClass, message));
        }

        protected override void NotifyInfo(Exception cause, object message)
        {
            _bus.Publish(new Info(cause, _logSource, _logClass, message));
        }

        /// <summary>
        /// Publishes the debug message onto the LoggingBus.
        /// </summary>
        /// <param name="message">The debug message.</param>
        protected override void NotifyDebug(object message)
        {
            _bus.Publish(new Debug(_logSource, _logClass, message));
        }

        protected override void NotifyDebug(Exception cause, object message)
        {
            _bus.Publish(new Debug(cause, _logSource, _logClass, message));
        }
    }
}
