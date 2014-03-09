using Akka.Actor;
using Akka.Configuration;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System2
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
    }
    remote {
        tcp-transport {
            transport-class = ""Akka.Remote.Transport.TcpTransport, Akka.Remote""
		    applied-adapters = []
		    transport-protocol = tcp
		    port = 8080
		    hostname = localhost
        }
    }
}
");
            //testing connectivity
            using (var system = ActorSystem.Create("system2", config))
            {
                system.ActorOf<SomeActor>("remotew1");
                system.ActorOf<SomeActor>("remotew2");
                system.ActorOf<SomeActor>("remotew3");
                Console.ReadLine();
            }
        }
    }
}
