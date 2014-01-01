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
        private ActorContext Context;

        public RemoteActorRef(ActorContext context, string remoteActorPath)
        {
            this.Path = new ActorPath(remoteActorPath);
            this.Context = context;
            var hubConnection = new HubConnection(this.Path.ToString().Substring(0,this.Path.ToString().Length-this.Path.Name.Length));
            this.actorName = this.Path.Name;
            hub = hubConnection.CreateHubProxy("ActorHub");
            hub.On("Reply", (string actorName, string data, string messageType) =>
            {
                var actor = context.System.Child(actorName);
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
                hub.Invoke("Post", sender.Path.ToString(), actorName, data, message.GetType().AssemblyQualifiedName);
            }
        }
    }
}
