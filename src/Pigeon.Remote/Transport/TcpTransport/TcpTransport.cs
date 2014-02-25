using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Transport
{
    public class TcpTransport : Transport
    {
        private TcpServer server;
        private LoggingAdapter log;
        public TcpTransport(ActorSystem system, Config config):base(system,config)
        {            
            var protocol = "akka."+config.GetString("transport-protocol");
            this.SchemeIdentifier = protocol;
            var host = config.GetString("hostname");
            var port = config.GetInt("port");
            this.Address = new Address(protocol, system.Name, host, port);
            this.log = Logging.GetLogger(system, this);
            server = new TcpServer(system, host, port);
        }

        public override Address Listen()
        {
            server.Start();
            log.Info("is listening @ {0}", Address);
            return Address;
        }

        public Address Address { get;private set; }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }
    }
}
