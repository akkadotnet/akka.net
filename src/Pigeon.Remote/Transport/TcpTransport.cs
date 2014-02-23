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
    }
}
