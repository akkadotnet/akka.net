using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.Routing;

namespace Samples.Cluster.ClusterClient.Seed
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            // region is for DocFX targeting

            #region HOCON

            Config config = @"
                akka.actor.provider = cluster
                akka.extensions = [""Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools""]
                akka.actor.deployment{
                    /distributor{
                        router = round-robin-group
				        routees.paths = [""/user/work""]
                        cluster {
                            enabled = on
                            allow-local-routees = on
                            use-role = worker
                        }
                    }
                }
                akka.remote.dot-netty.tcp.hostname = localhost
                akka.remote.dot-netty.tcp.port = 13310
                akka.cluster{
                    seed-nodes = [""akka.tcp://clusterNodes@localhost:13310""]
                    roles = [seed]
                    pub-sub.role = seed
                    client.receptionist.role = seed # stops ClusterClient gossip from going to the worker nodes
                }
            ";

            #endregion

            #region seed-startup

            // need to include ClusterClientReceptionist.DefaultConfig() to load serializers
            var actorSystem = ActorSystem.Create("clusterNodes", config
                .WithFallback(ClusterClientReceptionist.DefaultConfig()));

            // create the router we can use to distribute work to the worker nodes
            var distributor = actorSystem.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "distributor");
            ClusterClientReceptionist.Get(actorSystem).RegisterService(distributor);

            #endregion

            actorSystem.WhenTerminated.Wait();
        }
    }
}