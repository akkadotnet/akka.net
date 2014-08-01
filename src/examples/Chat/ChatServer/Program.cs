using ChatMessages;
using Akka;
using Akka.Actor;
using System;
using System.Collections.Generic;
using Akka.Event;

namespace ChatServer
{
    class Program
    {
        static void Main(string[] args)
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
            var fluentConfig = FluentConfig.Begin()
                .StdOutLogLevel(LogLevel.DebugLevel)
                .LogConfigOnStart(true)
                .LogLevel(LogLevel.ErrorLevel)   
                .LogLocal(
                    receive: true,
                    autoReceive: true,
                    lifecycle: true,
                    eventStream: true,
                    unhandled: true
                )
                .LogRemote(
                    lifecycleEvents: LogLevel.DebugLevel,
                    receivedMessages: true,
                    sentMessages: true
                )
                .StartRemotingOn("localhost", 8081)
                .Build();

            using (var system = ActorSystem.Create("MyServer", fluentConfig))
            {
                var server = system.ActorOf<ChatServerActor>("ChatServer");

                Console.ReadLine();
            }
        }
    }

    class ChatServerActor : TypedActor , 
        IHandle<SayRequest>,
        IHandle<ConnectRequest>,
        IHandle<NickRequest>,
        IHandle<Disconnect>,
        IHandle<ChannelsRequest>,
        ILogReceive

    {
        private readonly HashSet<ActorRef> _clients = new HashSet<ActorRef>();

        public void Handle(SayRequest message)
        {
          //  Console.WriteLine("User {0} said {1}",message.Username , message.Text);
            var response = new SayResponse
            {
                Username = message.Username,
                Text = message.Text,
            };
            foreach (var client in _clients) client.Tell(response, Self);
        }

        public void Handle(ConnectRequest message)
        {
         //   Console.WriteLine("User {0} has connected", message.Username);
            _clients.Add(this.Sender);
            Sender.Tell(new ConnectResponse
            {
                Message = "Hello and welcome to Akka .NET chat example",
            }, Self);
        }

        public void Handle(NickRequest message)
        {
            var response = new NickResponse
            {
                OldUsername = message.OldUsername,
                NewUsername = message.NewUsername,
            };

            foreach (var client in _clients) client.Tell(response, Self);
        }

        public void Handle(Disconnect message)
        {
            
        }

        public void Handle(ChannelsRequest message)
        {
            
        }
    }
}
