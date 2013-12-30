using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class RemoteActorRef: ActorRef
    {
        private IHubProxy hub;
        private string actorName;
        private ActorSystem system;

        public RemoteActorRef(ActorSystem system, string remoteUrl, string remoteActor)
        {
            this.Name = remoteActor;
            this.system = system;
            var hubConnection = new HubConnection(remoteUrl);
            this.actorName = remoteActor;
            hub = hubConnection.CreateHubProxy("ActorHub");
            hub.On("Reply", (string actorName, string data, string messageType) =>
            {
                var actor = system.ActorOf(actorName);
                var type = Type.GetType(messageType);
                var message = (IMessage)JsonConvert.DeserializeObject(data, type);
                actor.Tell(message, this);
            });
            hubConnection.StateChanged += hubConnection_StateChanged;
            hubConnection
                .Start()
                .Wait();
        }

        void hubConnection_StateChanged(StateChange obj)
        {           
        }

        public override void Tell(IMessage message, ActorRef sender)
        {
            var data = JsonConvert.SerializeObject(message);
            if (sender == ActorRef.NoSender)
            {
                hub.Invoke("Post", "", actorName, data, message.GetType().AssemblyQualifiedName);
            }
            else
            {
                hub.Invoke("Post", sender.Name, actorName, data, message.GetType().AssemblyQualifiedName);
            }
        }
    }
}
