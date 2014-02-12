using ChatMessages;
using Pigeon;
using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Dispatch.SysMsg;
using Pigeon.Event;
using Pigeon.Remote;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    actor {
        provider = ""Pigeon.Remote.RemoteActorRefProvider, Pigeon.Remote""
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
            using (var system = ActorSystem.Create("MyClient",config)) 
            {
                system.EventStream.Subscribe(e =>
                {
                    var debug = (Pigeon.Event.Debug)e;

                    Console.WriteLine("{0} {1} {2}",debug.LogSource,debug.LogClass,debug.Message);
                },typeof(Pigeon.Event.Debug));
                var chatClient = system.ActorOf(Props.Create<ChatClientActor>());
                var tmp = system.ActorSelection("akka.tcp://MyServer@localhost:8081/user/ChatServer");
                chatClient.Tell(new ConnectRequest()
                {
                    Username = "Roggan",
                });

                while (true)
                {
                    var input = Console.ReadLine();
                    if (input.StartsWith("*"))
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        for (int i = 0; i < 200; i++)
                        {
                            tmp.Tell(new Disconnect());
                        }
                        sw.Stop();
                        Console.WriteLine(sw.Elapsed);
                    }
                    else if (input.StartsWith("/"))
                    {
                        var parts = input.Split(' ');
                        var cmd = parts[0].ToLowerInvariant();
                        var rest = string.Join(" ",parts.Skip(1));

                        if (cmd == "/nick")
                        {
                            chatClient.Tell(new NickRequest
                            {
                                NewUsername = rest
                            });
                        }                        
                    }
                    else
                    {
                        chatClient.Tell(new SayRequest()
                        {
                            Text = input,
                        });
                    }
                }
            }
        }
    }

    class ChatClientActor : TypedActor,
        IHandle<ConnectRequest>,
        IHandle<ConnectResponse>,
        IHandle<NickRequest>,
        IHandle<NickResponse>,
        IHandle<SayRequest>,
        IHandle<SayResponse>
    {
        LoggingAdapter log = Logging.GetLogger(Context.System);

        private string nick = "Roggan";
        private ActorRef server = Context.ActorSelection("akka.tcp://MyServer@localhost:8081/user/ChatServer");
        
        public void Handle(ConnectResponse message)
        {
            Console.WriteLine("Connected!");
            Console.WriteLine(message.Message);         
        }

        public void Handle(NickRequest message)
        {
            message.OldUsername = this.nick;
            Console.WriteLine("Changing nick to {0}", message.NewUsername);
            this.nick = message.NewUsername;
            server.Tell(message);
        }

        public void Handle(NickResponse message)
        {
            Console.WriteLine("{0} is now known as {1}", message.OldUsername, message.NewUsername);
        }

        public void Handle(SayResponse message)
        {
            Console.WriteLine("{0}: {1}", message.Username, message.Text);
        }

        public void Handle(ConnectRequest message)
        {
            Console.WriteLine("Connecting....");
            server.Tell(message);
        }

        public void Handle(SayRequest message)
        {
            message.Username = this.nick;
            server.Tell(message);
        }     
    }
}