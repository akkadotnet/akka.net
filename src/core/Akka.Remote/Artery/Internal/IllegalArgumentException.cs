using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Remote.Artery.Internal
{
    internal class IllegalArgumentException : AkkaException
    {
        public IllegalArgumentException()
        {}

        public IllegalArgumentException(string message):base(message)
        {}

        public IllegalArgumentException(string message, Exception cause):base(message, cause)
        {}
    }
}
