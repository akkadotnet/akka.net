using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Remote
{
    public class RemoteTransportException : Exception
    {
        public RemoteTransportException(string message)
            : base(message)
        {
        }

        public RemoteTransportException(string message,object foo)
            : base(message)
        {
        }
    }
}
