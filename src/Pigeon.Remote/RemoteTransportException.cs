using System;

namespace Akka.Remote
{
    public class RemoteTransportException : Exception
    {
        public RemoteTransportException(string message)
            : base(message)
        {
        }

        public RemoteTransportException(string message, object foo)
            : base(message)
        {
        }
    }
}