using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace TimeServer
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("TimeServer", Config))
            {
                Console.Title = "Server";
                var server = system.ActorOf<TimeServerActor>("time");
                Console.ReadLine();
                Console.WriteLine("Shutting down...");
                Console.WriteLine("Terminated");
            }
        }

        public static Config Config
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
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

    deployment{
        /user/time{
            router = round-robin-pool
            nr-of-instances = 10
        }
    }

    remote {
		log-received-messages = off
		log-sent-messages = off

        #this is the new upcoming remoting support, which enables multiple transports
       helios.tcp {
            transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
		    applied-adapters = []
		    transport-protocol = tcp
		    port = 9391
		    hostname = 0.0.0.0 #listens on ALL ips for this machine
            public-hostname = localhost #but only accepts connections on localhost (usually 127.0.0.1)
        }
        log-remote-lifecycle-events = INFO
    }

}
");
            }
        }

        public class TimeServerActor : TypedActor, IHandle<string>
        {
            private readonly LoggingAdapter _log = Context.GetLogger();

            public void Handle(string message)
            {
                if (message.ToLowerInvariant() == "gettime")
                {
                    var time =DateTime.Now.ToLongTimeString();
                    Sender.Tell(time, Self);
                }
                else
                {

                    _log.Error("Invalid command: {0}", message);
                    var invalid = "Unrecognized command";
                    Sender.Tell(invalid, Self);
                }
            }
        }
    }
}
