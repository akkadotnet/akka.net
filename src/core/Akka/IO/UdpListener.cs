//-----------------------------------------------------------------------
// <copyright file="UdpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.IO
{
    using static Udp;


    // INTERNAL API
    class UdpListener : WithUdpSend, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpExt _udp;
        private readonly IActorRef _bindCommander;
        private readonly Bind _bind;

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Socket _socket;
        
        private IActorRef _selector;

        public UdpListener(UdpExt udp, IActorRef bindCommander, Bind bind)
        {
            _udp = udp;
            _bindCommander = bindCommander;
            _bind = bind;

            _selector = Context.Parent;

            Context.Watch(bind.Handler);        // sign death pact

            _socket = (bind.Options.OfType<Inet.DatagramChannelCreator>()
                            .FirstOrDefault() ?? new Inet.DatagramChannelCreator()).Create();
            _socket.Blocking = false;

            var localAddress = new Func<object>(() =>
            {
                try
                {
                    bind.Options.ForEach(x => x.BeforeDatagramBind(_socket));
                    _socket.Bind(bind.LocalAddress);
                    var ret = _socket.LocalEndPoint;
                    if (ret == null)
                        throw new ArgumentException($"bound to unknown SocketAddress [{_socket.LocalEndPoint}]");

                    _log.Debug("Successfully bound to [{0}]", ret);
                    bind.Options.OfType<Inet.SocketOptionV2>().ForEach(x => x.AfterBind(_socket));

                    ReceiveAsync();
                    return ret;
                }
                catch (Exception e)
                {
                    bindCommander.Tell(new CommandFailed(bind));
                    _log.Error(e, "Failed to bind UDP channel to endpoint [{0}]", bind.LocalAddress);
                    Context.Stop(Self);
                    return null;
                }
            })();
        }

        protected override Socket Socket
        {
            get { return _socket; }
        }
        protected override UdpExt Udp
        {
            get { return _udp; }
        }

        protected override void PreStart()
        {
            _bindCommander.Tell(new Bound(Socket.LocalEndPoint));
            Context.Become(m => ReadHandlers(m) || SendHandlers(m));
        }

        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }

        private bool ReadHandlers(object message)
        {
            if (message is SuspendReading)
            {
                // TODO: What should we do here - we cant cancel a pending ReceiveAsync
                return true;
            }
            if (message is ResumeReading)
            {
                ReceiveAsync();
                return true;
            }
            if (message is SocketReceived)
            {
                var received = (SocketReceived)message;
                DoReceive(received.EventArgs, _bind.Handler);
                return true;
            }

            if (message is Unbind)
            {
                _log.Debug("Unbinding endpoint [{0}]", _bind.LocalAddress);
                try
                {
                    Socket.Close();
                    Sender.Tell(Unbound.Instance);
                    _log.Debug("Unbound endpoint [{0}], stopping listener", _bind.LocalAddress);
                }
                finally
                {
                    Context.Stop(Self);
                }
                return true;
            }
            return false;
        }

        private void DoReceive(SocketAsyncEventArgs saea, IActorRef handler)
        {
            try
            {
                handler.Tell(new Received(ByteString.Create(saea.Buffer, saea.Offset, saea.BytesTransferred), saea.RemoteEndPoint));
                ReceiveAsync();
            }
            finally
            {
                Udp.SocketEventArgsPool.Release(saea);
            }
        }

        protected override void PostStop()
        {
            if (Socket.Connected)
            {
                _log.Debug("Closing DatagramChannel after being stopped");
                try
                {
                    Socket.Close();
                }
                catch (Exception e)
                {
                    _log.Debug("Error closing DatagramChannel: {0}", e);
                }
            }
        }


        private void ReceiveAsync()
        {
            var saea = Udp.SocketEventArgsPool.Acquire(Self);
            saea.RemoteEndPoint = _socket.LocalEndPoint;
            if (!_socket.ReceiveFromAsync(saea))
                Self.Tell(new SocketReceived(saea, _udp.SocketEventArgsPool));
        }
    }
}
