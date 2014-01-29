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
            var host = system.Settings.Config.GetString("Pigeon.Remote.Server.Host");
            var port = system.Settings.Config.GetInt("Pigeon.Remote.Server.Port");
            this.System = system;
            system.ActorRefFactory = (cell,actorPath) => new RemoteActorRef(cell, actorPath, port);
            system.Address = new Address("akka.tcp", system.Name,host,port);
            RemoteHost.StartHost(system, port);
        }

        public ActorSystem System { get;private set; }
    }
}
