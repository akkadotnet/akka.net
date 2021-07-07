using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.IO;
using PubSub.Messages;
using PubSub.Server;
using Dns = System.Net.Dns;

namespace Simple.Distributed.PubSub
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString($@"
akka.loglevel = INFO
akka.actor.provider = cluster
akka.remote.dot-netty.tcp.port = {Configuration.Port}
akka.remote.dot-netty.tcp.hostname = {(Configuration.ServerIp == string.Empty ? "localhost" : Configuration.ServerIp)}
")
                .WithFallback(DistributedPubSub.DefaultConfig());
            
            var system = ActorSystem.Create(Configuration.SystemName, config);
            var joinAddress = Cluster.Get(system).SelfAddress;
            Cluster.Get(system).Join(joinAddress);
            Console.WriteLine($"Join address: {joinAddress}");

            system.ActorOf<MemberListener>("memberListener");

            Console.ReadKey();
        }
    }
}