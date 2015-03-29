using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class InitializeLogger.
    /// </summary> 
    public class InitializeLogger : NoSerializationVerificationNeeded
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="InitializeLogger" /> class.
        /// </summary>
        /// <param name="loggingBus">The logging bus.</param>
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        /// <summary>
        ///     Gets the logging bus.
        /// </summary>
        /// <value>The logging bus.</value>
        public LoggingBus LoggingBus { get; private set; }
    }
}
