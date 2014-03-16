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
                router = round-robin-pool
                nr-of-instances = 5
            }
            /remote {
                router = round-robin-pool
                nr-of-instances = 5
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
                //create a local group router (see config)
                var local = system.ActorOf(Props.Create(() =>  new SomeActor("hello",123)),  "local");

                //create a remote deployed actor
                var remote = system.ActorOf(Props.Create(() => new SomeActor(null, 123)), "remote");

                //these messages should reach the workers via the routed local ref
                local.Tell("Local message 1");
                local.Tell("Local message 2");
                local.Tell("Local message 3");
                local.Tell("Local message 4");
                local.Tell("Local message 5");

                //this should reach the remote deployed ref
                remote.Tell("Remote message 1");
                remote.Tell("Remote message 2");
                remote.Tell("Remote message 3");
                remote.Tell("Remote message 4");
                remote.Tell("Remote message 5");
                Console.ReadLine();
                for (int i = 0; i < 10000; i++)
                {
                    remote.Tell(i);
                }

                Console.ReadLine();
            }            
        }
    }
}
