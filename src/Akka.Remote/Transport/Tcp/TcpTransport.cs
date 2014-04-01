using System;
using Akka.Configuration;

namespace Akka.Remote.Transport.Tcp
{
    class TcpTransportException : RemoteTransportException
    {
        public TcpTransportException(string message, Exception cause = null) : base(message, cause)
        {
        }
    }

    class TcpTransportSettings
    {
        private Config _config;

        public TcpTransportSettings(Config config)
        {
            _config = config;
        }
    }
}