#region sender
// See https://aka.ms/new-console-template for more information
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using SampleSender;

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

var sender = actorSystem.ActorOf(Props.Create<Sender>(), "sender");

sender.Tell("Hello from Akka-Verse");

actorSystem.WhenTerminated.Wait();
#endregion