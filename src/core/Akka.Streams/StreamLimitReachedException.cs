using System;
using System.Runtime.Serialization;

namespace Akka.Streams
{
    public class StreamLimitReachedException : Exception
    {
        public StreamLimitReachedException(long max) : base(string.Format("Limit of {0} reached", max))
        {
        }

        protected StreamLimitReachedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}