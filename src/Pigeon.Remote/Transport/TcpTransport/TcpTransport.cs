using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Remote.Transport
{
    public class TcpTransport : Transport
    {
        private readonly LoggingAdapter log;
        private readonly TcpServer server;

        public TcpTransport(ActorSystem system, Config config) : base(system, config)
        {
            string protocol = "akka." + config.GetString("transport-protocol");
            SchemeIdentifier = protocol;
            string host = config.GetString("hostname");
            int port = config.GetInt("port");
            Address = new Address(protocol, system.Name, host, port);
            log = Logging.GetLogger(system, this);
            server = new TcpServer(system, host, port);
        }

        public Address Address { get; private set; }

        public override Address Listen()
        {
            server.Start();
            log.Info("is listening @ {0}", Address);
            return Address;
        }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }
    }
}