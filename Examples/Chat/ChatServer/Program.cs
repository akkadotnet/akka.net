using ChatMessages;
using Pigeon;
using Pigeon.Actor;
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
        IHandle<ChannelsRequest>

    {
        private List<ActorRef> clients = new List<ActorRef>();
        public ChatServerActor(ActorContext start)
            : base(start)
        {
        }

        public void Handle(SayRequest message)
        {
            Console.WriteLine("User {0} said {1}",message.Username , message.Text);
            var response = new SayResponse
            {
                Username = message.Username,
                Text = message.Text,
            };
            clients.ForEach(c => c.Tell(response, Self));
        }

        public void Handle(ConnectRequest message)
        {
            clients.Add(this.Sender);
            Sender.Tell(new ConnectResponse
            {
                Message = "Hello and welcome to Pigeon chat example",
            }, Self);
        }

        public void Handle(NickRequest message)
        {
            var response = new NickResponse
            {
                OldUsername = message.OldUsername,
                NewUsername = message.NewUsername,
            };

            clients.ForEach(c => c.Tell(response, Self));            
        }

        public void Handle(Disconnect message)
        {
            
        }

        public void Handle(ChannelsRequest message)
        {
            
        }
    }
}
