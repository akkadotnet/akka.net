using System;
using System.Runtime.Serialization;

namespace Akka.Actor
{
    /// <summary>
    /// Is thrown when the size of the Stash exceeds the capacity of the stash
    /// </summary>
    public class StashOverflowException : AkkaException
    {
        public StashOverflowException(string message, Exception cause = null) : base(message, cause) { }

        protected StashOverflowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}