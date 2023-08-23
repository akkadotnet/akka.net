//-----------------------------------------------------------------------
// <copyright file="BusLogging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// A logging adapter implementation publishing log events to the event stream.
    /// </summary>
    public sealed class BusLogging : LoggingAdapterBase
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

            IsErrorEnabled = bus.LogLevel <= LogLevel.ErrorLevel;
            IsWarningEnabled = bus.LogLevel <= LogLevel.WarningLevel;
            IsInfoEnabled = bus.LogLevel <= LogLevel.InfoLevel;
            IsDebugEnabled = bus.LogLevel <= LogLevel.DebugLevel;
        }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.DebugLevel" /> is enabled.
        /// </summary>
        public override bool IsDebugEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.ErrorLevel" /> is enabled.
        /// </summary>
        public override bool IsErrorEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.InfoLevel" /> is enabled.
        /// </summary>
        public override bool IsInfoEnabled { get; }

        /// <summary>
        /// Check to determine whether the <see cref="LogLevel.WarningLevel" /> is enabled.
        /// </summary>
        public override bool IsWarningEnabled { get; }
        
        private LogEvent CreateLogEvent(LogLevel logLevel, object message, Exception cause = null)
        {
            return logLevel switch
            {
                LogLevel.DebugLevel => new Debug(cause, _logSource, _logClass, message),
                LogLevel.InfoLevel => new Info(cause, _logSource, _logClass, message),
                LogLevel.WarningLevel => new Warning(cause, _logSource, _logClass, message),
                LogLevel.ErrorLevel => new Error(cause, _logSource, _logClass, message),
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
            };
        }

        protected override void NotifyLog(LogLevel logLevel, object message, Exception cause = null)
        {
            var logEvent = CreateLogEvent(logLevel, message, cause);
            _bus.Publish(logEvent);
        }
    }
}
