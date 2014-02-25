using Akka.Actor;
using Akka.Configuration;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System1
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
        debug {
            lifecycle = on
            receive = on
        }
    }
    remote {
        server {
            host = ""127.0.0.1""
            port = 8091
        }
    }
}
");
            //testing connectivity
            Thread.Sleep(1000);
            using (var system = ActorSystem.Create("system1", config))
            {
                var local = system.ActorOf(Props.Create(typeof(SomeActor)).WithDeploy(Deploy.Local), "local");
                var remoteAddress = new Address("akka.tcp", "system2", "127.0.0.1", 8080);
                //TODO: this is not yet implemented so this example will fail - 2014-02-23 //Roger
                var remote = system.ActorOf(Props.Create(typeof(SomeActor)).WithDeploy(new Deploy(new RemoteScope(remoteAddress))), "remote");

            }
        }
    }
}
