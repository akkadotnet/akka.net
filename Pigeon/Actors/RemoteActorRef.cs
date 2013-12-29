using Microsoft.AspNet.SignalR.Client;
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

        private async void Connect(string url, string actorName)
        {
            var hubConnection = new HubConnection(url);
            this.actorName = actorName;
            hub = hubConnection.CreateHubProxy("ActorHub");

            await hubConnection
                .Start();
        }
        public override void Tell(IMessage message)
        {
            hub.Invoke("Post",actorName, message);
        }
    }
}
