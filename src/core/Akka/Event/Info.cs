using System;

namespace Akka.Event
{
    /// <summary>
    ///     Class Info.
    /// </summary>
    public class Info : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Info" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Info(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }
    }
}
