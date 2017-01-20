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
                
                var subscriber = system.ActorOf(Props.Create<TextMessageReceiver>());

                client.Tell(new ClusterClient.Subscribe(Topics.TextMessages.ToString(), typeof(TextMessage), subscriber));

                while (Console.ReadKey().Key != ConsoleKey.Escape)
                {
                    client.Tell(new ClusterClient.Publish(Topics.TextMessages.ToString(), new ThankYou("ThankYou from client", client.Path.ToStringWithoutAddress())));
                    //var result = client.Ask<YouAreWelcome>(new ClusterClient.Send("/user/chat", new ThankYou("ThankYou from client", client.Path.ToStringWithoutAddress()))).Result;
                    //subscriber.Tell(result);
                    //client.Tell(new ClusterClient.Send("/user/chat", new ThankYou("ThankYou from client", client.Path.ToStringWithoutAddress())));
                }

                Console.ReadLine();
            }
        }
    }
}
