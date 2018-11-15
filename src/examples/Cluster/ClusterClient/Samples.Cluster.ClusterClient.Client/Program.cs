using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;

namespace Samples.Cluster.ClusterClient.Client
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Config config = @"
                akka.actor.provider = remote
                akka.remote.dot-netty.tcp.hostname = localhost
                akka.remote.dot-netty.tcp.port = 0 #random port
            ";

            var actorSystem = ActorSystem.Create("ClusterClient", config
                .WithFallback(ClusterClientReceptionist.DefaultConfig()));
            var receptionistAddress = Address.Parse("akka.tcp://clusterNodes@localhost:13310");
            var pinger = actorSystem.ActorOf(Props.Create(() => new PingerActor(receptionistAddress)), "pinger");

            actorSystem.WhenTerminated.Wait();
        }
    }
}