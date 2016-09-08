//-----------------------------------------------------------------------
// <copyright file="UdpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    using static UdpConnected;

    internal class UdpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpConnectedExt _udpConn;
        private readonly IActorRef _commander;
        private readonly Connect _connect;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        private Socket _socket;
        private bool _readingSuspended = false;
        private Received _pendingRead;

        public UdpConnection(UdpConnectedExt udpConn, 
                             IActorRef commander, 
                             Connect connect)
        {
            _udpConn = udpConn;
            _commander = commander;
            _connect = connect;

            Context.Watch(connect.Handler);

            var remoteAddress = connect.RemoteAddress as DnsEndPoint;
            if (remoteAddress != null)
            {
                var resolved = Dns.ResolveName(remoteAddress.Host, Context.System, Self);
                if (resolved != null)
                    DoConnect(new IPEndPoint(resolved.Addr, remoteAddress.Port));
                else
                    Context.Become(Resolving(remoteAddress));
            }
            else
            {
                DoConnect(_connect.RemoteAddress);
            }
        }

        private Tuple<Send, IActorRef> _pendingSend;
        private bool WritePending => _pendingSend != null;

        private Receive Resolving(DnsEndPoint remoteAddress)
        {
            return message =>
            {
                var r = message as Dns.Resolved;
                if (r != null)
                {
                    ReportConnectFailure(() => DoConnect(new IPEndPoint(r.Addr, remoteAddress.Port)));
                    return true;
                }
                return false;
            };
        }

        private void DoConnect(EndPoint address)
        {
            ReportConnectFailure(() =>
            {
                _socket = new Socket(SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                _connect.Options.ForEach(x => x.BeforeDatagramBind(_socket));
                if (_connect.LocalAddress != null)
                    _socket.Bind(_connect.LocalAddress);

                _socket.Connect(_connect.RemoteAddress);

                _connect.Options
                        .OfType<Inet.SocketOptionV2>().ForEach(v2 => v2.AfterConnect(_socket));
                _commander.Tell(UdpConnected.Connected.Instance);

                ReceiveAsync();
                Context.Become(Connected);
            });
            _log.Debug("Successfully connected to [{0}]", _connect.RemoteAddress);
        }


        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }

        private bool Connected(object message)
        {
            if (message is SuspendReading)
            {
                _readingSuspended = true;
                return true;
            }
            if (message is ResumeReading)
            {
                _readingSuspended = false;
                if (_pendingRead != null)
                {
                    _connect.Handler.Tell(_pendingRead);
                    _pendingRead = null;
                    ReceiveAsync();
                }
                return true;
            }
            if (message is SocketReceived)
            {
                DoRead((SocketReceived)message, _connect.Handler);
                return true;
            }

            if (message is Disconnect)
            {
                _log.Debug("Closing UDP connection to [{0}]", _connect.RemoteAddress);
                _socket.Close();
                Sender.Tell(Disconnected.Instance);
                _log.Debug("Connection closed to [{0}], stopping listener", _connect.RemoteAddress);
                Context.Stop(Self);
                return true;
            }

            var send = message as Send;
            if (send != null && WritePending)
            {
                if (_udpConn.Settings.TraceLogging) _log.Debug("Dropping write because queue is full");
                Sender.Tell(new CommandFailed(send));
                return true;
            }
            if (send != null && send.Payload.IsEmpty)
            {
                if (send.WantsAck)
                    Sender.Tell(send.Ack);
                return true;
            }
            if (send != null)
            {
                _pendingSend = Tuple.Create(send, Sender);
                DoWrite();
                return true;
            }
            if (message is SocketSent)
            {
                var sent = (SocketSent)message;
                if (_pendingSend.Item1.WantsAck)
                    _pendingSend.Item2.Tell(_pendingSend.Item1.Ack);
                if (_udpConn.Settings.TraceLogging) _log.Debug("Wrote [{0}] bytes to socket", sent.EventArgs.BytesTransferred);
                _pendingSend = null;
                sent.Pool.Release(sent.EventArgs);
                return true;
            }
            return false;
        }

        private void DoRead(SocketReceived received, IActorRef handler)
        {
            var saea = received.EventArgs;
            var data = new Received(ByteString.Create(saea.Buffer, saea.Offset, saea.BytesTransferred));
            received.Pool.Release(saea);

            if (!_readingSuspended)
            {
                handler.Tell(data);
                ReceiveAsync();
            }
            else _pendingRead = data;

        }

        private void DoWrite()
        {
            var saea = _udpConn.SocketEventArgsPool.Acquire(Self);
            var send = _pendingSend.Item1;

            var len = Math.Min(send.Payload.Count, saea.Count);
            saea.RemoteEndPoint = _connect.RemoteAddress;
            saea.SetBuffer(saea.Offset, len);
            send.Payload.CopyTo(saea.Buffer, saea.Offset, len);

            SendAsync(saea);
        }

        protected override void PostStop()
        {
            _log.Debug("Closing DatagramChannel after being stopped");
            try
            {
                _socket.Dispose();
            }
            catch (Exception ex)
            {
                _log.Debug("Error closing DatagramChannel: {0}", ex);
            }
        }

        private void ReportConnectFailure(Action thunk)
        {
            try
            {
                thunk();
            }
            catch (Exception e)
            {
                _log.Debug("Failure while connecting UDP channel to remote address [{0}] local address [{1}]: {2}", _connect.RemoteAddress, _connect.LocalAddress, e);
                _commander.Tell(new CommandFailed(_connect));
                Context.Stop(Self);
            }
        }

        private void ReceiveAsync()
        {
            var saea = _udpConn.SocketEventArgsPool.Acquire(Self);
            if (!_socket.ReceiveAsync(saea))
                Self.Tell(new SocketReceived(saea, _udpConn.SocketEventArgsPool));
        }
        private void SendAsync(SocketAsyncEventArgs saea)
        {
            if (!_socket.SendToAsync(saea))
                Self.Tell(new SocketSent(saea, _udpConn.SocketEventArgsPool));
        }
    }
}
