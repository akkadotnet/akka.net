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
                router = round-robin-group
                routees.paths = [
                    /user/w1
                    /user/w2
                    /user/w3
                ]
            }
            /remote {
                remote = ""akka.tcp://system2@localhost:8080""
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
            using (var system = ActorSystem.Create("system1", config))
            {
                var worker1 = system.ActorOf<SomeActor>("w1");
                var worker2 = system.ActorOf<SomeActor>("w2");
                var worker3 = system.ActorOf<SomeActor>("w3");

                var local = system.ActorOf<SomeActor>("local");
                var remote = system.ActorOf<SomeActor> ("remote");

                local.Tell("Local message");
                remote.Tell("Remote message");
                Console.ReadLine();
            }            
        }
    }
}
