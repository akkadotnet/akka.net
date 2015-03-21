using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

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
