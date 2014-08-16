using System;
using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using ChatMessages;

namespace ChatServer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            //            var config = ConfigurationFactory.ParseString(@"
            //akka {  
            //    log-config-on-start = on
            //    stdout-loglevel = DEBUG
            //    loglevel = ERROR
            //    actor {
            //        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            //        
            //        debug {  
            //          receive = on 
            //          autoreceive = on
            //          lifecycle = on
            //          event-stream = on
            //          unhandled = on
            //        }
            //    }
            //
            //    remote {
            //		log-received-messages = on
            //		log-sent-messages = on
            //        #log-remote-lifecycle-events = on
            //
            //        #this is the new upcoming remoting support, which enables multiple transports
            //       helios.tcp {
            //            transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
            //		    applied-adapters = []
            //		    transport-protocol = tcp
            //		    port = 8081
            //		    hostname = 0.0.0.0 #listens on ALL ips for this machine
            //            public-hostname = localhost #but only accepts connections on localhost (usually 127.0.0.1)
            //        }
            //        log-remote-lifecycle-events = INFO
            //    }
            //
            //}
            //");
            Config fluentConfig = FluentConfig.Begin()
                .StdOutLogLevel(LogLevel.DebugLevel)
                .LogConfigOnStart(true)
                .LogLevel(LogLevel.ErrorLevel)
                .LogLocal(true, true, true, true, true
                )
                .LogRemote(LogLevel.DebugLevel, true, true
                )
                .StartRemotingOn("localhost", 8081)
                .Build();

            using (ActorSystem system = ActorSystem.Create("MyServer", fluentConfig))
            {
                InternalActorRef server = system.ActorOf<ChatServerActor>("ChatServer");

                Console.ReadLine();
            }
        }
    }

    internal class ChatServerActor : TypedActor,
        IHandle<SayRequest>,
        IHandle<ConnectRequest>,
        IHandle<NickRequest>,
        IHandle<Disconnect>,
        IHandle<ChannelsRequest>,
        ILogReceive

    {
        private readonly BroadcastActorRef _clients = new BroadcastActorRef();

        public void Handle(ChannelsRequest message)
        {
        }

        public void Handle(ConnectRequest message)
        {
            //   Console.WriteLine("User {0} has connected", message.Username);
            _clients.TryAdd(Sender);
            Sender.Tell(new ConnectResponse
            {
                Message = "Hello and welcome to Akka .NET chat example",
            }, Self);
        }

        public void Handle(Disconnect message)
        {
        }

        public void Handle(NickRequest message)
        {
            var response = new NickResponse
            {
                OldUsername = message.OldUsername,
                NewUsername = message.NewUsername,
            };

            _clients.Tell(response, Self);
        }

        public void Handle(SayRequest message)
        {
            //  Console.WriteLine("User {0} said {1}",message.Username , message.Text);
            var response = new SayResponse
            {
                Username = message.Username,
                Text = message.Text,
            };
            _clients.Tell(response, Self);
        }
    }
}
