//-----------------------------------------------------------------------
// <copyright file="TcpIncomingConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
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
    /// <summary>
    /// TBD
    /// </summary>
    internal class TcpIncomingConnection : TcpConnection
    {
        private readonly IActorRef _bindHandler;
        private readonly IEnumerable<Inet.SocketOption> _options;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcp">TBD</param>
        /// <param name="channel">TBD</param>
        /// <param name="registry">TBD</param>
        /// <param name="bindHandler">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="readThrottling">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
#endif