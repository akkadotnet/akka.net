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
    log-config-on-start = on
    stdout-loglevel = DEBUG
    loglevel = ERROR
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
        
        debug {  
          receive = on 
          autoreceive = on
          lifecycle = on
          event-stream = on
          unhandled = on
        }

        deployment {
            /local {
                dispatcher = """"
                mailbox = """"
                router = from-code
            }
        }
    }
    remote {
        tcp-transport {
            transport-class = ""Akka.Remote.Transport.TcpTransport, Akka.Remote""
		    applied-adapters = []
		    transport-protocol = tcp
		    port = 8090
		    hostname = localhost
        }
    }
}
");
            //testing connectivity
            Thread.Sleep(1000);
            using (var system = ActorSystem.Create("system1", config))
            {
                var local = system.ActorOf(Props.Create(typeof(SomeActor)).WithDeploy(Deploy.Local), "local");

                var remoteAddress = new Address("akka.tcp", "system2", "localhost", 8080);
                var remote = system.ActorOf(Props.Create(typeof(SomeActor)).WithDeploy(new Deploy(new RemoteScope(remoteAddress))), "remote");
                Console.ReadLine();
            }
            
        }
    }
}
