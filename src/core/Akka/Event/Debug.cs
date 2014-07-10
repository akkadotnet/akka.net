using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /// <summary>
    ///     Class Debug.
    /// </summary>
    public class Debug : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Debug" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Debug(string logSource, Type logClass, object message)
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
            return Event.LogLevel.DebugLevel;
        }
    }
}
