//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Sockets;
using Akka.Actor;
using System;

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
                                     Socket socket, 
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options, 
                                     bool readThrottling)
            : base(tcp, socket, readThrottling)
        {
            _bindHandler = bindHandler;
            _options = options;

            Context.Watch(bindHandler); // sign death pact
        }

        protected override void PreStart()
        {
            CompleteConnect(_bindHandler, _options);
        }

        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }
    }
}
