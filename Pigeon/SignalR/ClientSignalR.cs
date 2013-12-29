using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.SignalR
{
    internal static class PigeonClientSignalR
    {
        internal static void Start(string name, string url)
        {
            var hubConnection = new HubConnection("http://localhost:8080/");

            var hub = hubConnection.CreateHubProxy("MyHub");

            hub.On("Pong", system =>
            {
                Console.WriteLine("Pong from {0}", system);
            });

            hubConnection.StateChanged += args =>
            {
            };

            hubConnection
                .Start()
                .Wait();

            hub.Invoke("Ping", name);
        }
    }
}
