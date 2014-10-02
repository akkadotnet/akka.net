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

            _isErrorEnabled = bus.LogLevel <= LogLevel.ErrorLevel;
            _isWarningEnabled = bus.LogLevel <= LogLevel.WarningLevel;
            _isInfoEnabled = bus.LogLevel <= LogLevel.InfoLevel;
            _isDebugEnabled = bus.LogLevel <= LogLevel.DebugLevel;
        }

        private readonly bool _isDebugEnabled;
        public override bool IsDebugEnabled { get { return _isDebugEnabled; }}

        private readonly bool _isErrorEnabled;
        public override bool IsErrorEnabled { get { return _isErrorEnabled; }}

        private readonly bool _isInfoEnabled;
        public override bool IsInfoEnabled{ get { return _isInfoEnabled; }}

        private readonly bool _isWarningEnabled;
        public override bool IsWarningEnabled { get { return _isWarningEnabled; }}

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
