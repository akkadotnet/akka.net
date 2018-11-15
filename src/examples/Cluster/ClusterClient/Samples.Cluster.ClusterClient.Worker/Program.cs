using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;

namespace Samples.Cluster.ClusterClient.Worker
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            // region is for DocFX targeting

            #region HOCON

            Config config = @"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.hostname = localhost
                akka.remote.dot-netty.tcp.port = 0 #random port
                akka.cluster{
                    seed-nodes = [""akka.tcp://clusterNodes@localhost:13310""]
                    roles = [worker]
                }
            ";

            #endregion

            #region worker-startup

            // need to include ClusterClientReceptionist.DefaultConfig() to load serializers
            var actorSystem = ActorSystem.Create("clusterNodes", config
                .WithFallback(ClusterClientReceptionist.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig()));

            var worker = actorSystem.ActorOf(Props.Create(() => new WorkerActor()), "work");

            #endregion

            actorSystem.WhenTerminated.Wait();
        }
    }
}