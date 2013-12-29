using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Host.HttpListener;
using Microsoft.Owin.Hosting;
using Newtonsoft.Json;
using Owin;
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
            //Console.WriteLine("Got message {0}", message);
            var actor = system.GetActor(actorName);
            var remoteActor = system.GetRemoteActor(remoteActorName);
            actor.Tell(message, remoteActor);
        }
    }
}
