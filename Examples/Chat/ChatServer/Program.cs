using ChatMessages;
using Pigeon;
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatServer
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystemSignalR.Create("Chat Server", "http://localhost:8090"))
            {
                var server = system.GetActor<ChatServerActor>();

                Console.ReadLine();
            }
        }
    }

    class ChatServerActor : TypedActor , 
        IHandle<SayRequest>,
        IHandle<ConnectRequest>,
        IHandle<NickRequest>,
        IHandle<Disconnect>,
        IHandle<ChannelsRequest>

    {
        public ChatServerActor(ActorSystem system)
            : base(system)
        {
        }

        public void Handle(SayRequest message)
        {
            Console.WriteLine("User said {0}", message.Text);
        }

        public void Handle(ConnectRequest message)
        {
            Sender.Tell(ActorRef.NoSender, new ConnectResponse
            {
                Message = "Hello and welcome to Pigeon chat example",
            });
        }

        public void Handle(NickRequest message)
        {
            Sender.Tell(ActorRef.NoSender, new NickResponse
            {
                Username = message.Username,
            });
        }

        public void Handle(Disconnect message)
        {
            
        }

        public void Handle(ChannelsRequest message)
        {
            
        }
    }
}
