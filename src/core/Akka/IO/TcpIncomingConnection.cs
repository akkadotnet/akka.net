//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.IO
{
    /**
    * An actor handling the connection state machine for an incoming, already connected
    * SocketChannel.
    *
    * INTERNAL API
    */
    internal class TcpIncomingConnection : TcpConnection
    {
        private readonly IActorRef _bindHandler;
        private readonly IEnumerable<Inet.SocketOption> _options;

        public TcpIncomingConnection(TcpExt tcp, 
                                     SocketChannel channel, 
                                     IChannelRegistry registry, 
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options, 
                                     bool readThrottling)
            : base(tcp, channel, readThrottling)
        {
            _bindHandler = bindHandler;
            _options = options;

            Context.Watch(bindHandler); // sign death pact

            registry.Register(channel, SocketAsyncOperation.None, Self);
        }

        protected override bool Receive(object message)
        {
            var registration = message as ChannelRegistration;
            if (registration != null)
            {
                CompleteConnect(registration, _bindHandler, _options);
                return true;
            }
            return false;
        }
    }
}
