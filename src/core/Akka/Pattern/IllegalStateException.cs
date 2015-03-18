using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Pattern
{
    public class IllegalStateException : AkkaException
    {

        public IllegalStateException(string message) : base(message)
        {

        }

        protected IllegalStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
