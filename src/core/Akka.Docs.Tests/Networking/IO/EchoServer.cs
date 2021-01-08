//-----------------------------------------------------------------------
// <copyright file="EchoServer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using Akka.Actor;
using Akka.IO;

namespace DocsExamples.Networking.IO
{
    public class EchoServer : UntypedActor
    {
        public EchoServer(int port)
        {
            Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Any, port)));
        }

        protected override void OnReceive(object message)
        {
            if (message is Tcp.Bound)
            {
                var bound = message as Tcp.Bound;
                Console.WriteLine("Listening on {0}", bound.LocalAddress);
            }
            else if (message is Tcp.Connected)
            {
                var connection = Context.ActorOf(Props.Create(() => new EchoConnection(Sender)));
                Sender.Tell(new Tcp.Register(connection));
            }
            else Unhandled(message);
        }
    }
}
