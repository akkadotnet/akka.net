//-----------------------------------------------------------------------
// <copyright file="UdpSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly IActorRef _commander;
        private readonly IEnumerable<Inet.SocketOption> _options;

        private readonly ILoggingAdapter _log = Context.GetLogger();
        
        public UdpSender(UdpExt udp, IActorRef commander, IEnumerable<Inet.SocketOption> options)
        {
            Udp = udp;
            _commander = commander;
            _options = options;

            Socket = new Func<Socket>(() =>
            {
                var socket = new Socket(SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                _options.ForEach(x => x.BeforeDatagramBind(socket));
                return socket;
            })();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override UdpExt Udp { get; }

        protected override Socket Socket { get; }

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
