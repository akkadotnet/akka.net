using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actors
{
    public class RemoteActorRef: ActorRef
    {
        private IHubProxy hub;
        private string actorName;
        public RemoteActorRef(string url, string actorName)
        {
            Connect(url, actorName);
        }

        private void Connect(string url, string actorName)
        {
            var hubConnection = new HubConnection(url);
            this.actorName = actorName;
            hub = hubConnection.CreateHubProxy("ActorHub");
            hubConnection.StateChanged += hubConnection_StateChanged;
            hubConnection
                .Start()
                .Wait();
        }

        void hubConnection_StateChanged(StateChange obj)
        {
            if (obj.NewState == ConnectionState.Connected)
            {
                Console.WriteLine("Remote Actor ref {0} connected", actorName);
            }
        }
        public override void Tell(IMessage message)
        {
            var data = JsonConvert.SerializeObject(message);
            hub.Invoke("Post", actorName, data,message.GetType().AssemblyQualifiedName);
        }
    }
}
