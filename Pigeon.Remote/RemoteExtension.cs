using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{

    public class RemoteExtension : ActorSystemExtension
    {
        public override void Start(ActorSystem system)
        {
            var host = system.Settings.GetOrDefault<string>(s => s.Pigeon.Remote.Server.Host, null);
            var port = system.Settings.GetOrDefault(s => s.Pigeon.Remote.Server.Port, 0);
            this.System = system;
            system.ActorRefFactory = (cell,actorPath) => new RemoteActorRef(cell, actorPath, port);
            system.Address = new Address("akka.tcp", system.Name,host,port);
            RemoteHost.StartHost(system, port);
        }

        public ActorSystem System { get;private set; }
    }
}
