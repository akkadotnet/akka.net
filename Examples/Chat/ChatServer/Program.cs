using ChatMessages;
using Pigeon;
using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Remote;
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
            var config = ConfigurationFactory.ParseString(@"
Pigeon {        
    Remote {
        Server {
            Host = ""127.0.0.1""
            Port = 8081
        }
    }
}
");
            using (var system = ActorSystem.Create("MyServer", config, new RemoteExtension())) 
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
        private BroadcastActorRef clients = new BroadcastActorRef();

        public void Handle(SayRequest message)
        {
            Console.WriteLine("User {0} said {1}",message.Username , message.Text);
            var response = new SayResponse
            {
                Username = message.Username,
                Text = message.Text,
            };
            clients.Tell(response, Self);
        }

        public void Handle(ConnectRequest message)
        {
            Console.WriteLine("User {0} has connected", message.Username);
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

            clients.Tell(response, Self);
        }

        public void Handle(Disconnect message)
        {
            
        }

        public void Handle(ChannelsRequest message)
        {
            
        }
    }
}
