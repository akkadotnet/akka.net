using Pigeon.Actor;
using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote.Transport
{
    public class TcpTransport : Transport
    {
        private TcpServer server;
        public TcpTransport(ActorSystem system, Config config):base(system,config)
        {
            
            var protocol = config.GetString("transport-protocol");
            var host = config.GetString("host");
            var port = config.GetInt("port");
            this.Address = new Address(protocol, system.Name, host, port);
            server = new TcpServer(system, host, port);
        }

        public override Tuple<Address, object> Listen()
        {
            server.Start();
            return Tuple.Create<Address,object>(Address, "foo");
        }

        public Address Address { get;private set; }
    }
}
