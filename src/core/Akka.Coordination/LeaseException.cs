using System;

namespace Akka.Coordination
{
    public class LeaseException : Exception
    {
        public LeaseException(string message) : base(message)
        {
        }

        public LeaseException(string message, Exception innerEx)
            : base(message, innerEx)
        {
        }

#if SERIALIZATION
        protected LeaseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {

        }
#endif
    }
}
