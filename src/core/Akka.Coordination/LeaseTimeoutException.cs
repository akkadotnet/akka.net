using System;

namespace Akka.Coordination
{
    public class LeaseTimeoutException : LeaseException
    {
        public LeaseTimeoutException(string message) : base(message)
        {
        }

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
