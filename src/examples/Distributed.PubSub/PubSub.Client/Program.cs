using System;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using PubSub.Messages;

namespace PubSub.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("PubSub.Client [fake-username]");
                return;
            }
            
            var config = ConfigurationFactory.ParseString(@"
akka.loglevel = INFO
akka.actor.provider = cluster
akka.remote.dot-netty.tcp.port = 0
akka.remote.dot-netty.tcp.hostname = localhost
")
                .WithFallback(DistributedPubSub.DefaultConfig());
            
            var system = ActorSystem.Create(Configuration.SystemName, config);
            var address = new Address(
                "akka.tcp", 
                Configuration.SystemName, 
                Configuration.ServerIp == string.Empty ? "localhost" : Configuration.ServerIp, 
                Configuration.Port);
            Cluster.Get(system).Join(address);
            system.ActorOf<RandomUser>(args[0]);
            
            Console.ReadKey();
        }
    }
}