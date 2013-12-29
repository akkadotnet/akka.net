using ChatMessages;
using Pigeon;
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatClient
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystemSignalR.Create("Chat client", "http://localhost:8080"))
            {
                var chatClient = system.GetActor<ChatClientActor>();
                chatClient.Tell(new ConnectRequest()
                {
                    Username = "Roggan",
                }, ActorRef.NoSender);

                while (true)
                {
                    var input = Console.ReadLine();
                    chatClient.Tell(new SayRequest()
                    {
                        Text = input,
                    }, ActorRef.NoSender);
                }
            }
        }
    }

    class ChatClientActor : TypedActor,
        IHandle<ConnectRequest>,
        IHandle<ConnectResponse>,
        IHandle<NickResponse>,
        IHandle<SayRequest>,
        IHandle<SayResponse>
    {
        private string nick = "User";
        private ActorRef server;

        public ChatClientActor(ActorStart start)
            : base(start)
        {
            server = Context.GetRemoteActor("http://localhost:8090", "ChatServer", this);
        }        
        
        public void Handle(ConnectResponse message)
        {
            Console.WriteLine("Connected");
            Console.WriteLine(message.Message);
        }

        public void Handle(NickResponse message)
        {
            if (message.Username == nick)
            {
            }
            else
            {
                Console.WriteLine("Your nick is now : {0}", message.Username);
            }
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
            server.Tell(message);
        }
    }
}
