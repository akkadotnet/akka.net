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
        public TcpTransport(ActorSystem system, Config config):base(system,config)
        {
        }

        public override Tuple<Address, object> Listen()
        {
            var address = new Address("apa", this.System.Name, "gapa", 1233);

            return Tuple.Create<Address,object>(address, "foo");
        }
    }
}
