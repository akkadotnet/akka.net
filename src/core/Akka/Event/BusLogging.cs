using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /// <summary>
    ///     Class BusLogging.
    /// </summary>
    public class BusLogging : LoggingAdapter
    {
        /// <summary>
        ///     The bus
        /// </summary>
        private readonly LoggingBus bus;

        /// <summary>
        ///     The log class
        /// </summary>
        private readonly Type logClass;

        /// <summary>
        ///     The log source
        /// </summary>
        private readonly string logSource;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BusLogging" /> class.
        /// </summary>
        /// <param name="bus">The bus.</param>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="logMessageFormatter">The log message formatter.</param>
        public BusLogging(LoggingBus bus, string logSource, Type logClass, ILogMessageFormatter logMessageFormatter)
            : base(logMessageFormatter)
        {
            this.bus = bus;
            this.logSource = logSource;
            this.logClass = logClass;

            IsErrorEnabled = bus.LogLevel <= LogLevel.ErrorLevel;
            IsWarningEnabled = bus.LogLevel <= LogLevel.WarningLevel;
            IsInfoEnabled = bus.LogLevel <= LogLevel.InfoLevel;
            IsDebugEnabled = bus.LogLevel <= LogLevel.DebugLevel;
        }

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyError(object message)
        {
            bus.Publish(new Error(null, logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        protected override void NotifyError(Exception cause, object message)
        {
            bus.Publish(new Error(cause, logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the warning.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyWarning(object message)
        {
            bus.Publish(new Warning(logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the information.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyInfo(object message)
        {
            bus.Publish(new Info(logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the debug.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyDebug(object message)
        {
            bus.Publish(new Debug(logSource, logClass, message));
        }
    }
}
