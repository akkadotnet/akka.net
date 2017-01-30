using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using ClusterClientExample.Shared;

namespace ClusterClientExample.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Cluster client";
            using (var system = ActorSystem.Create("remote-cluster-system"))
            {
                system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());
                var client = system.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(system)));

                var subscriber = system.ActorOf(Props.Create<TextMessageReceiver>(client));

                client.Subscribe<ThankYou>(subscriber, Topics.TextMessages.ToString());
                client.Subscribe<YouAreWelcome>(subscriber, Topics.TextMessages.ToString());
                
                //client.Tell(new ClusterClient.Send("/user/chat", new ThankYou("Client Tell to Service", client))); //Tell/Ask is supported if you register actor as service on node

                while (Console.ReadKey().Key != ConsoleKey.Escape)
                {
                    client.Publish(Topics.TextMessages.ToString(), new ThankYou("", client));
                }

                Console.ReadLine();
            }
        }
    }
}
