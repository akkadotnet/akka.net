//-----------------------------------------------------------------------
// <copyright file="Actors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using Akka.Actor;
using Akka.IO;

namespace TcpEchoService.Server
{
    public class EchoService : ReceiveActor
    {
        private readonly IActorRef _manager = Context.System.Tcp();
        private IActorRef _tcpListener;
        private IActorRef _stopCommander;

        public EchoService(EndPoint endpoint)
        {
            _manager.Tell(new Tcp.Bind(Self, endpoint));

            // Store TcpListener in case if we will want to Unbing
            Receive<Tcp.Bound>(_ => _tcpListener = Sender);

            // To behave as TCP listener, actor should be able to handle Tcp.Connected messages
            Receive<Tcp.Connected>(connected =>
            {
                Console.WriteLine("Remote address {0} connected", connected.RemoteAddress);
                var handler = Context.ActorOf(Props.Create(() => new EchoConnectionHandler(connected.RemoteAddress, Sender)));
                Sender.Tell(new Tcp.Register(handler));
            });
            
            // Close connection before exit
            Receive<StopServer>(_ =>
            {
                _stopCommander = Sender;
                _tcpListener?.Tell(Tcp.Unbind.Instance);
            });
            // Report that close completed
            Receive<Tcp.Unbound>(_ => _stopCommander?.Tell("Done"));
        }
        
        public class StopServer { }
    }

    public class EchoConnectionHandler : ReceiveActor
    {
        private readonly IActorRef _connection;

        public EchoConnectionHandler(EndPoint remote, IActorRef connection)
        {
            _connection = connection;
            // we want to know when the connection dies (without using Tcp.ConnectionClosed)
            Context.Watch(connection);

            Receive<Tcp.Received>(received =>
            {
                var text = Encoding.UTF8.GetString(received.Data.ToArray()).Trim();
                Console.WriteLine("Received '{0}' from remote address [{1}]", text, remote);
                if (text == "exit")
                    Context.Stop(Self);
                else
                    Sender.Tell(Tcp.Write.Create(received.Data));
            });
            Receive<Tcp.ConnectionClosed>(closed =>
            {
                Console.WriteLine("Stopped, remote connection [{0}] closed", remote);
                Context.Stop(Self);
            });
            Receive<Terminated>(terminated =>
            {
                Console.WriteLine("Stopped, remote connection [{0}] died", remote);
                Context.Stop(Self);
            });
        }

        /// <inheritdoc />
        protected override void PostStop()
        {
            base.PostStop();
            
            _connection.Tell(Tcp.Close.Instance);
        }
    }
}
