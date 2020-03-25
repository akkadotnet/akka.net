using System;

namespace Akka.Coordination
{
    /// <summary>
    /// Lease timeout exception. Lease implementation should use it to report issues when lease is lost
    /// </summary>
    public sealed class LeaseTimeoutException : LeaseException
    {
        /// <summary>
        /// Creates a new <see cref="LeaseTimeoutException"/> instance.
        /// </summary>
        /// <param name="message">Exception message</param>
        public LeaseTimeoutException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new <see cref="LeaseTimeoutException"/> instance.
        /// </summary>
        /// <param name="message">Exception message</param>
        /// <param name="innerEx">Inner exception</param>
        public LeaseTimeoutException(string message, Exception innerEx)
            : base(message, innerEx)
        {
        }

#if SERIALIZATION
        protected LeaseTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {

        }
#endif
    }
}
