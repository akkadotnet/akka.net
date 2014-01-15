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
        private IActorContext Context;

        public RemoteActorRef(IActorContext context, ActorPath remoteActorPath)
        {
            this.Path = remoteActorPath;
            this.Context = context;
            var url = this.Path.ToString().Substring(0, this.Path.ToString().Length - this.Path.Name.Length);
            url = url.Substring("pigeon.".Length);
            var hubConnection = new HubConnection(url);
            this.actorName = this.Path.Name;
            hub = hubConnection.CreateHubProxy("ActorHub");
            hub.On("Reply", (string actorName, string data, string messageType) =>
            {
                var actor = context.System.Guardian.Cell.Child(actorName);
                var type = Type.GetType(messageType);
                var message = (object)JsonConvert.DeserializeObject(data, type);
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

        protected override void TellInternal(object message, ActorRef sender)
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
