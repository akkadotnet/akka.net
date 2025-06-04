//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Sockets;
using Akka.Actor;
using System;
using Akka.Util;

namespace Akka.IO
{
    /// <summary>
    /// An actor handling the connection state machine for an incoming, already connected SocketChannel.
    /// </summary>
    internal sealed class TcpIncomingConnection : TcpConnection
    {
        private readonly IActorRef _bindHandler;
        private readonly IEnumerable<Inet.SocketOption> _options;
        
        public TcpIncomingConnection(TcpSettings settings, 
                                     Socket socket, 
                                     IActorRef bindHandler,
                                     IEnumerable<Inet.SocketOption> options, 
                                     bool readThrottling)
            : base(settings, socket, readThrottling)
        {
            _bindHandler = bindHandler;
            _options = options;

            Context.Watch(bindHandler); // sign death pact
        }

        protected override void PreStart()
        {
            CompleteConnect(_bindHandler, _options);
        }
    }
}
