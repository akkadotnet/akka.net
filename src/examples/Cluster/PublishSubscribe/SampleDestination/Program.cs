#region destination
// See https://aka.ms/new-console-template for more information
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using SampleDestination;
var config = ConfigurationFactory.ParseString(@"
akka {
   actor.provider = cluster
   extensions = [""Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider,Akka.Cluster.Tools""]
   remote {
       dot-netty.tcp {
           port = 0
           hostname = localhost
       }
   }
   cluster {
       seed-nodes = [""akka.tcp://cluster@localhost:5800""]
   }
}");
var actorSystem = ActorSystem.Create("cluster", config);

DistributedPubSub.Get(actorSystem);

actorSystem.ActorOf(Props.Create<Destination>(), "destination");

actorSystem.WhenTerminated.Wait();
#endregion