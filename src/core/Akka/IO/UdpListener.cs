//-----------------------------------------------------------------------
// <copyright file="IO.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.IO
{
    // INTERNAL API
    class UdpListener : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpExt _udp;
        private readonly IChannelRegistry _channelRegistry;
        private readonly IActorRef _bindCommander;
        private readonly Udp.Bind _bind;

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly DatagramChannel _channel;
        
        private IActorRef _selector;

        public UdpListener(UdpExt udp, IChannelRegistry channelRegistry, IActorRef bindCommander, Udp.Bind bind)
        {
            _udp = udp;
            _channelRegistry = channelRegistry;
            _bindCommander = bindCommander;
            _bind = bind;

            _selector = Context.Parent;

            Context.Watch(bind.Handler);        // sign death pact

            _channel = (bind.Options.OfType<Inet.DatagramChannelCreator>()
                            .FirstOrDefault() ?? new Inet.DatagramChannelCreator()).Create();
            _channel.ConfigureBlocking(false);

            var localAddress = new Func<object>(() =>
            {
                try
                {
                    var socket = Channel.Socket;
                    bind.Options.ForEach(x => x.BeforeDatagramBind(socket));
                    socket.Bind(bind.LocalAddress);
                    var ret = socket.LocalEndPoint;
                    if (ret == null)
                        throw new ArgumentException(string.Format("bound to unknown SocketAddress [{0}]", socket.LocalEndPoint));
                    channelRegistry.Register(Channel, SocketAsyncOperation.Receive, Self);
                    _log.Debug("Successfully bound to [{0}]", ret);
                    bind.Options.OfType<Inet.SocketOptionV2>().ForEach(x => x.AfterBind(socket));
                    return ret;
                }
                catch (Exception e)
                {
                    bindCommander.Tell(new Udp.CommandFailed(bind));
                    _log.Error(e, "Failed to bind UDP channel to endpoint [{0}]", bind.LocalAddress);
                    Context.Stop(Self);
                    return null;
                }
            })();
        }

        protected override DatagramChannel Channel
        {
            get { return _channel; }
        }
        protected override UdpExt Udp
        {
            get { return _udp; }
        }

      


        protected override bool Receive(object message)
        {
            var registration = message as ChannelRegistration;
            if (registration != null)
            {
                _bindCommander.Tell(new Udp.Bound(Channel.Socket.LocalEndPoint));
                Context.Become(m => ReadHandlers(registration)(m) || SendHandlers(registration)(m));
                return true;
            }
            return false;
        }

        private Receive ReadHandlers(ChannelRegistration registration)
        {
            return message =>
            {
                if (message is Udp.SuspendReading)
                {
                    registration.DisableInterest(SocketAsyncOperation.Receive);
                    return true;
                }
                if (message is Udp.ResumeReading)
                {
                    registration.EnableInterest(SocketAsyncOperation.Receive);
                    return true;
                }
                if (message is SelectionHandler.ChannelReadable)
                {
                    DoReceive(registration, _bind.Handler);
                    return true;
                }

                if (message is Udp.Unbind)
                {
                    _log.Debug("Unbinding endpoint [{0}]", _bind.LocalAddress);
                    try
                    {
                        Channel.Close();
                        Sender.Tell(IO.Udp.Unbound.Instance);
                        _log.Debug("Unbound endpoint [{0}], stopping listener", _bind.LocalAddress);
                    }
                    finally
                    {
                        Context.Stop(Self);
                    }
                    return true;
                }
                return false;
            };
        }

        private void DoReceive(ChannelRegistration registration, IActorRef handler)
        {
            Action<int, ByteBuffer> innerReceive = null;
            innerReceive = (readsLeft, buffer) =>
            {
                buffer.Clear();
                buffer.Limit(_udp.Setting.DirectBufferSize);

                var sender = Channel.Receive(buffer);
                if (sender != null)
                {
                    buffer.Flip();
                    handler.Tell(new Udp.Received(ByteString.Create(buffer), sender));
                    if (readsLeft > 0) innerReceive(readsLeft - 1, buffer);
                }
            };

            var buffr = _udp.BufferPool.Acquire();
            try
            {
                innerReceive(_udp.Setting.BatchReceiveLimit, buffr);
            }
            finally
            {
                _udp.BufferPool.Release(buffr);
                registration.EnableInterest(SocketAsyncOperation.Receive);
            }
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
