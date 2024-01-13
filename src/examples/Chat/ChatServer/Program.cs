//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using ChatMessages;

namespace ChatServer
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    actor {
        provider = remote
    }
    remote {
        dot-netty.tcp {
            port = 8081
            hostname = 0.0.0.0
            public-hostname = localhost
        }
    }
}
");

            var system = ActorSystem.Create("MyServer", config);

            system.ActorOf(Props.Create(() => new ChatServerActor()), "ChatServer");

            Console.ReadLine();
            await system.Terminate();
        }
    }

    internal class ChatServerActor : ReceiveActor, ILogReceive
    {
        private readonly HashSet<IActorRef> _clients = new();

        public ChatServerActor()
        {
            Receive<SayRequest>(message =>
            {
                var response = new SayResponse(message.Username, message.Text);
                foreach (var client in _clients) client.Tell(response, Self);
            });

            Receive<ConnectRequest>(_ =>
            {
                _clients.Add(Sender);
                Sender.Tell(new ConnectResponse("Hello and welcome to Akka.NET chat example"), Self);
            });

            Receive<NickRequest>(message =>
            {
                var response = new NickResponse(message.OldUsername, message.NewUsername);

                foreach (var client in _clients) client.Tell(response, Self);
            });
        }
    }
}