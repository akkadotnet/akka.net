//-----------------------------------------------------------------------
// <copyright file="UdpSender.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    class UdpSender : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpExt _udp;
        private readonly IActorRef _commander;
        private readonly IEnumerable<Inet.SocketOption> _options;

        private readonly DatagramChannel _channel;

        private readonly ILoggingAdapter _log = Context.GetLogger();

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

        protected override UdpExt Udp
        {
            get { return _udp; }
        }
        protected override DatagramChannel Channel
        {
            get { return _channel; }
        }

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
