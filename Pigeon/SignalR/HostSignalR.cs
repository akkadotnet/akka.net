using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Host.HttpListener;
using Microsoft.Owin.Hosting;
using Newtonsoft.Json;
using Owin;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.SignalR
{
    public class PigeonHostSignalR : IDisposable
    {
        private ActorSystem system;
        private IDisposable app;

        public static PigeonHostSignalR Start(ActorSystem system,string url)
        {
            var t = typeof(OwinHttpListener);

            var host = new PigeonHostSignalR
            {
                system = system,
                app = WebApp.Start(url, app =>
                {
                    app.UseCors(CorsOptions.AllowAll);
                    app.MapSignalR();
                    GlobalHost.DependencyResolver.Register(typeof(ActorHub), () => new ActorHub(system));
                    app.MapSignalR();

                })
            };
            return host;
        }

        public void Dispose()
        {
            app.Dispose();
        }
    }   

    public class ActorHub : Hub
    {
        private ActorSystem system;

        public ActorHub(ActorSystem system)
        {
            this.system = system;
        }
        public void Post(string remoteActorName, string actorName, string data,string typeName)
        {
            var type = Type.GetType(typeName);
            var message = (IMessage)JsonConvert.DeserializeObject(data, type);
            var actor = system.Child(actorName);
            var connectionId = this.Context.ConnectionId;
            var remoteActor = new SignalRResponseActorRef(remoteActorName, this, connectionId,actor);
            actor.Tell(message, remoteActor);
        }

        private class SignalRResponseActorRef : ActorRef
        {
            private string remoteActorName;
            private Hub hub;
            private string connectionId;
            public SignalRResponseActorRef(string remoteActorName, Hub hub, string connectionId,ActorRef owner)
            {
                this.remoteActorName = remoteActorName;
                this.hub = hub;
                this.connectionId = connectionId;
            }
            public override void Tell(IMessage message, ActorRef sender)
            {
                var data = JsonConvert.SerializeObject(message);
                hub.Clients.Client(connectionId).Reply(remoteActorName, data, message.GetType().AssemblyQualifiedName);
            }
        }
    }
}
