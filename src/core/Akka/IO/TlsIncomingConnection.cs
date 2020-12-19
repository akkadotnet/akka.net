// //-----------------------------------------------------------------------
// // <copyright file="TlsIncomingConnection.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Util;

namespace Akka.IO
{
    /// <summary>
    /// An actor handling the connection state machine for an incoming, already connected SocketChannel.
    /// </summary>
    internal sealed class TlsIncomingConnection : TlsConnection
    {
        private readonly IActorRef _bindHandler;
        private readonly IEnumerable<Inet.SocketOption> _options;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcp">TBD</param>
        /// <param name="socket">TBD</param>
        /// <param name="bindHandler">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="readThrottling">TBD</param>
        public TlsIncomingConnection(TcpExt tcp, 
            Socket socket,
            IActorRef bindHandler,
            IEnumerable<Inet.SocketOption> options, 
            bool readThrottling)
            : base(tcp, socket, (Inet.SO.TlsConnectionOption)options.FirstOrDefault(r=>r is Inet.SO.TlsConnectionOption),readThrottling, Option<int>.None)
        {
            _bindHandler = bindHandler;
            _options = options;

            Context.Watch(bindHandler); // sign death pact
        }

        protected override void PreStart()
        {
            AcquireSocketAsyncEventArgs();

            CompleteConnect(_bindHandler, _options);
        }

        protected override void Authenticate()
        {
            SslStream.AuthenticateAsClient(this.TargetHost);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }
    }
}