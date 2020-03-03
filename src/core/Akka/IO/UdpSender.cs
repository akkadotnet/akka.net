//-----------------------------------------------------------------------
// <copyright file="UdpSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.IO
{
    using static Udp;
    
    class UdpSender : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpExt _udp;
        private readonly IActorRef _commander;
        private readonly IEnumerable<Inet.SocketOption> _options;

        private readonly Socket _socket;

        private readonly ILoggingAdapter _log = Context.GetLogger();
        
        public UdpSender(UdpExt udp, IActorRef commander, IEnumerable<Inet.SocketOption> options)
        {
            _udp = udp;
            _commander = commander;
            _options = options;

            _socket = new Func<Socket>(() =>
            {
                var socket = new Socket(SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                _options.ForEach(x => x.BeforeDatagramBind(socket));
                return socket;
            })();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override UdpExt Udp => _udp;

        protected override Socket Socket => _socket;

        public override void AroundPreStart()
        {
            _options
                .OfType<Inet.SocketOptionV2>()
                .ForEach(x => x.AfterConnect(Socket));
            _commander.Tell(SimpleSenderReady.Instance);
            Context.Become(SendHandlers);
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

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _log.Debug("Closing Socket after being stopped");
            try
            {
                Socket.Dispose();
            }
            catch (Exception e)
            {
                _log.Debug("Error closing Socket: {0}", e);
            }
        }
    }
}
