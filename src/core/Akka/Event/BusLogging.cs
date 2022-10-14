//-----------------------------------------------------------------------
// <copyright file="BusLogging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="BusLogging" /> class.
        /// </summary>
        /// <param name="bus">The logging bus instance that messages will be published to.</param>
        /// <param name="source"></param>
        public BusLogging(LoggingBus bus, LogSource source)
            : base(source)
        {
            _bus = bus;

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

        protected override void NotifyLog<TState>(in LogEntry<TState> c)
        {
            switch (c.LogLevel)
            {
                case LogLevel.DebugLevel:
                    _bus.Publish(new Debug(c));
                    break;
                case LogLevel.InfoLevel:
                    _bus.Publish(new Info(c));
                    break;
                case LogLevel.WarningLevel:
                    _bus.Publish(new Warning(c));
                    break;
                case LogLevel.ErrorLevel:
                    _bus.Publish(new Error(c));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(c), $"Unsupported LogLevel [{c.LogLevel}]");
            }
        }
    }
}
