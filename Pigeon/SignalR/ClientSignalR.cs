using Microsoft.AspNet.SignalR.Client;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.SignalR
{
    public class PigeonClientSignalR
    {
        private ActorSystem system;
        private string remoteUrl;
        private IHubProxy hub;
        public PigeonClientSignalR(ActorSystem system, string remoteUrl)
        {
            this.system = system;
            this.remoteUrl = remoteUrl;
            var hubConnection = new HubConnection(remoteUrl);
            hub = hubConnection.CreateHubProxy("ActorHub");
            hubConnection
                .Start()
                .Wait();
        }
    }
}
