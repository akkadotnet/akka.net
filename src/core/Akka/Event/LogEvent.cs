using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class LogEvent.
    /// </summary>
    public abstract class LogEvent : INoSerializationVerificationNeeded
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="LogEvent" /> class.
        /// </summary>
        public LogEvent()
        {
            Timestamp = DateTime.Now;
            Thread = Thread.CurrentThread;
        }

        /// <summary>
        ///     Gets the timestamp.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        ///     Gets the thread.
        /// </summary>
        /// <value>The thread.</value>
        public Thread Thread { get; private set; }

        /// <summary>
        ///     Gets or sets the log source.
        /// </summary>
        /// <value>The log source.</value>
        public string LogSource { get; protected set; }

        /// <summary>
        ///     Gets or sets the log class.
        /// </summary>
        /// <value>The log class.</value>
        public Type LogClass { get; protected set; }

        /// <summary>
        ///     Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; protected set; }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public abstract LogLevel LogLevel();

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return string.Format("[{0}][{1}][Thread {2}][{3}] {4}", LogLevel().ToString().Replace("Level", "").ToUpperInvariant(), Timestamp, Thread.ManagedThreadId.ToString().PadLeft(4, '0'), LogSource, Message);
        }
    }
}
