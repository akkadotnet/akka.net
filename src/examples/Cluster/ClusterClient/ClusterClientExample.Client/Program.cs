﻿using System;
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
                client.Tell(new ClusterClient.SetUnhandledMessagesMediator(subscriber));

                var subscribe = new ClusterClient.Subscribe(Topics.TextMessages.ToString());
                client.Tell(subscribe);

                //client.Tell(new ClusterClient.Send("/user/chat", new ThankYou("Client Tell to Service", client))); //Tell/Ask is supported if you register actor as service on node

                while (Console.ReadKey().Key != ConsoleKey.Escape)
                {
                    client.Tell(new ClusterClient.Publish(Topics.TextMessages.ToString(), new ThankYou("", client)));
                }

                client.Tell(new ClusterClient.Unsubscribe(subscribe));
                client.Tell(new ClusterClient.SetUnhandledMessagesMediator(ActorRefs.Nobody));

                Console.ReadLine();
            }
        }
    }
}
