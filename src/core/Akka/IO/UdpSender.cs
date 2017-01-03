﻿//-----------------------------------------------------------------------
// <copyright file="UdpSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// <summary>
    /// TBD
    /// </summary>
    class UdpSender : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpExt _udp;
        private readonly IActorRef _commander;
        private readonly IEnumerable<Inet.SocketOption> _options;

        private readonly DatagramChannel _channel;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="udp">TBD</param>
        /// <param name="channelRegistry">TBD</param>
        /// <param name="commander">TBD</param>
        /// <param name="options">TBD</param>
        public UdpSender(UdpExt udp, IChannelRegistry channelRegistry, IActorRef commander, IEnumerable<Inet.SocketOption> options)
        {
            _udp = udp;
            _commander = commander;
            _options = options;

            _channel = new Func<DatagramChannel>(() =>
            {
                var datagramChannel = DatagramChannel.Open();
                datagramChannel.ConfigureBlocking(false);
                var socket = datagramChannel.Socket;
                _options.ForEach(x => x.BeforeDatagramBind(socket));

                return datagramChannel;
            })();

            channelRegistry.Register(_channel, SocketAsyncOperation.None, Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override UdpExt Udp
        {
            get { return _udp; }
        }
        /// <summary>
        /// TBD
        /// </summary>
        protected override DatagramChannel Channel
        {
            get { return _channel; }
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
                _options
                    .OfType<Inet.SocketOptionV2>()
                    .ForEach(x => x.AfterConnect(Channel.Socket));
                _commander.Tell(IO.Udp.SimpleSenderReady.Instance);
                Context.Become(SendHandlers(registration));
                return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            if (Channel.IsOpen())
            {
                _log.Debug("Closing DatagramChannel after being stopped");
                try
                {
                    Channel.Close();
                }
                catch (Exception e)
                {
                    _log.Debug("Error closing DatagramChannel: {0}", e);
                }
            }
        }
    }
}
