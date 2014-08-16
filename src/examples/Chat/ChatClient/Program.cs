using System;
using System.Linq;
using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using ChatMessages;

namespace ChatClient
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Config fluentConfig = FluentConfig.Begin()
                .StartRemotingOn("localhost") //no port given = use any free port
                .Build();

            using (ActorSystem system = ActorSystem.Create("MyClient", fluentConfig))
            {
                InternalActorRef chatClient = system.ActorOf(Props.Create<ChatClientActor>());
                ActorSelection tmp = system.ActorSelection("akka.tcp://MyServer@localhost:8081/user/ChatServer");
                chatClient.Tell(new ConnectRequest
                {
                    Username = "Roggan",
                });

                while (true)
                {
                    string input = Console.ReadLine();
                    if (input.StartsWith("/"))
                    {
                        string[] parts = input.Split(' ');
                        string cmd = parts[0].ToLowerInvariant();
                        string rest = string.Join(" ", parts.Skip(1));

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
                        chatClient.Tell(new SayRequest
                        {
                            Text = input,
                        });
                    }
                }
            }
        }
    }

    internal class ChatClientActor : TypedActor,
        IHandle<ConnectRequest>,
        IHandle<ConnectResponse>,
        IHandle<NickRequest>,
        IHandle<NickResponse>,
        IHandle<SayRequest>,
        IHandle<SayResponse>, ILogReceive
    {
        private readonly ActorSelection _server =
            Context.ActorSelection("akka.tcp://MyServer@localhost:8081/user/ChatServer");

        private LoggingAdapter _log = Logging.GetLogger(Context);

        private string _nick = "Roggan";

        public void Handle(ConnectRequest message)
        {
            Console.WriteLine("Connecting....");
            _server.Tell(message);
        }

        public void Handle(ConnectResponse message)
        {
            Console.WriteLine("Connected!");
            Console.WriteLine(message.Message);
        }

        public void Handle(NickRequest message)
        {
            message.OldUsername = _nick;
            Console.WriteLine("Changing nick to {0}", message.NewUsername);
            _nick = message.NewUsername;
            _server.Tell(message);
        }

        public void Handle(NickResponse message)
        {
            Console.WriteLine("{0} is now known as {1}", message.OldUsername, message.NewUsername);
        }

        public void Handle(SayRequest message)
        {
            message.Username = _nick;
            _server.Tell(message);
        }

        public void Handle(SayResponse message)
        {
            Console.WriteLine("{0}: {1}", message.Username, message.Text);
        }
    }
}