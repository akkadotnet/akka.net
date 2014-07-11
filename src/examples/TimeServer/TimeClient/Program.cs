using System;
using System.Diagnostics;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Helios.Concurrency;

namespace TimeClient
{
    internal class Program
    {
        static bool IsShutdown = false;

        private static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("TimeClient", Config))
            {
                var tmp = system.ActorSelection("akka.tcp://TimeServer@localhost:9391/user/time");
                Console.Title = string.Format("TimeClient {0}", Process.GetCurrentProcess().Id);
                var timeClient = system.ActorOf(Props.Create(() => new TimeClientActor(tmp)), "timeChecker");


                var fiber = FiberFactory.CreateFiber(3);

                while (!Program.IsShutdown)
                {
                    fiber.Add(() =>
                    {
                        Thread.Sleep(1);
                        timeClient.Tell(Time);
                    });
                }

                Console.WriteLine("Connection closed.");
                fiber.GracefulShutdown(TimeSpan.FromSeconds(1));

                Console.ReadLine();
                IsShutdown = true;
                Console.WriteLine("Shutting down...");
                Console.WriteLine("Terminated");
            }
        }

        public static CheckTime Time = new CheckTime();
        public class CheckTime { }

        public class TimeClientActor : TypedActor, IHandle<string>, IHandle<CheckTime>
        {
            private readonly ICanTell _timeServer;

            public TimeClientActor(ICanTell timeServer)
            {
                _timeServer = timeServer;
            }

            public void Handle(string message)
            {
                Console.WriteLine(message);
            }

            public void Handle(CheckTime message)
            {
                _timeServer.Tell("gettime", Self);
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
          receive = off 
          autoreceive = on
          lifecycle = on
          event-stream = on
          unhandled = on
        }
    }

    deployment{
        /user/timeChecker{
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
		    port = 0 #bind to any available port
		    hostname = 0.0.0.0 #listens on ALL ips for this machine
            public-hostname = localhost #but only accepts connections on localhost (usually 127.0.0.1)
        }
        log-remote-lifecycle-events = INFO
    }

}
");
            }
        }
    }
}
