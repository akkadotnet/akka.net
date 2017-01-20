using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using ClusterClientExample.Shared;

namespace ClusterClientExample.Node
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Seed node and receptionist";
            using (var system = ActorSystem.Create("remote-cluster-system"))
            {
                var receptionist = ClusterClientReceptionist.Get(system);
                var subscriber = system.ActorOf(Props.Create<PoliteResponder>(), "chat");
                receptionist.RegisterService(subscriber);
                receptionist.RegisterSubscriber(Topics.TextMessages.ToString(), subscriber);
                Console.ReadLine();
                receptionist.UnregisterSubscriber(Topics.TextMessages.ToString(), subscriber);
            }
        }
    }
}
