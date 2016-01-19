using System;

namespace Akka.Streams
{
    public class NoSuchElementException : Exception
    {
        public NoSuchElementException(string message) : base(message)
        {

        }
    }
}
