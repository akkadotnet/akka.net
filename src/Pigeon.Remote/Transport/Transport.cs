using Pigeon.Actor;
using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote.Transport
{
    public abstract class Transport
    {
        public Transport(ActorSystem system, Config config)
        {
            this.System = system;
            this.Config = config;
        }

        public Config Config { get;private set; }

        public ActorSystem System { get; private set; }

        public abstract Tuple<Address, object> Listen();
    }
}
