﻿//-----------------------------------------------------------------------
// <copyright file="UdpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.IO
{
    using static UdpConnected;
    using ByteBuffer = ArraySegment<byte>;

    internal class UdpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        protected readonly UdpConnectedExt Udp;
        private readonly IActorRef _commander;
        private readonly Connect _connect;

        protected readonly ILoggingAdapter Log = Context.GetLogger();

        private Socket _socket;
        private bool _readingSuspended = false;
        private Received _pendingRead;

        /// <summary>
        /// TBD
        /// </summary>
        public UdpConnection(UdpConnectedExt udp,
                             IActorRef commander,
                             Connect connect)
        {
            Udp = udp;
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
                    DoConnect(new IPEndPoint(r.Addr, remoteAddress.Port));
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

                foreach (var option in _connect.Options)
                {
                    option.BeforeDatagramBind(_socket);
                }

                if (_connect.LocalAddress != null)
                    _socket.Bind(_connect.LocalAddress);

                _socket.Connect(_connect.RemoteAddress);

                foreach (var v2 in _connect.Options.OfType<Inet.SocketOptionV2>())
                {
                    v2.AfterConnect(_socket);
                }

                _commander.Tell(UdpConnected.Connected.Instance);

                ReceiveAsync();
                Context.Become(Connected);
            });
            Log.Debug("Successfully connected to [{0}]", _connect.RemoteAddress);
        }

        protected override bool Receive(object message)
        {
            throw new NotSupportedException();
        }

        private bool Connected(object message)
        {
            if (message is SuspendReading) _readingSuspended = true;
            else if (message is ResumeReading)
            {
                _readingSuspended = false;
                if (_pendingRead != null)
                {
                    _connect.Handler.Tell(_pendingRead);
                    _pendingRead = null;
                    ReceiveAsync();
                }
            }
            else if (message is SocketReceived) DoRead((SocketReceived)message, _connect.Handler);
            else if (message is Disconnect)
            {
                Log.Debug("Closing UDP connection to [{0}]", _connect.RemoteAddress);

                _socket.Dispose();

                Sender.Tell(Disconnected.Instance);
                Log.Debug("Connection closed to [{0}], stopping listener", _connect.RemoteAddress);
                Context.Stop(Self);
            }
            else if (message is Send)
            {
                var send = (Send)message;
                if (WritePending)
                {
                    if (Udp.Settings.TraceLogging) Log.Debug("Dropping write because queue is full");
                    Sender.Tell(new CommandFailed(send));
                }
                else
                {
                    if (send.Payload.MoveNext())
                    {
                        _pendingSend = Tuple.Create(send, Sender);
                        DoWrite();
                    }
                    else
                    {
                        if (send.WantsAck)
                            Sender.Tell(send.Ack);
                    }
                }
            }
            else if (message is SocketSent)
            {
                var sent = (SocketSent) message;
                if (_pendingSend.Item1.WantsAck)
                    _pendingSend.Item2.Tell(_pendingSend.Item1.Ack);
                if (Udp.Settings.TraceLogging)
                    Log.Debug("Wrote [{0}] bytes to socket", sent.EventArgs.BytesTransferred);
                _pendingSend = null;
                Udp.SocketEventArgsPool.Release(sent.EventArgs);
            }
            else return false;
            return true;
        }

        private void DoRead(SocketReceived received, IActorRef handler)
        {
            var e = received.EventArgs;
            var buffer = new ByteBuffer(e.Buffer, e.Offset, e.BytesTransferred);
            var data = new Received(ByteString.CopyFrom(buffer));
            Udp.BufferPool.Release(buffer);
            Udp.SocketEventArgsPool.Release(e);

            if (!_readingSuspended)
            {
                handler.Tell(data);
                ReceiveAsync();
            }
            else _pendingRead = data;
        }

        private void DoWrite()
        {
            var e = Udp.SocketEventArgsPool.Acquire(Self);
            var send = _pendingSend.Item1;
            var buffer = send.Payload.Current;

            e.RemoteEndPoint = _connect.RemoteAddress;
            e.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

            SendAsync(e);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            Log.Debug("Closing DatagramChannel after being stopped");
            try
            {
                _socket.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug("Error closing DatagramChannel: {0}", ex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReportConnectFailure(Action thunk)
        {
            try
            {
                thunk();
            }
            catch (Exception e)
            {
                Log.Debug("Failure while connecting UDP channel to remote address [{0}] local address [{1}]: {2}", _connect.RemoteAddress, _connect.LocalAddress, e);
                _commander.Tell(new CommandFailed(_connect));
                Context.Stop(Self);
            }
        }

        private void ReceiveAsync()
        {
            var e = Udp.SocketEventArgsPool.Acquire(Self);
            var buffer = Udp.BufferPool.Rent();
            e.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
            if (!_socket.ReceiveAsync(e))
                Self.Tell(new SocketReceived(e));
        }

        private void SendAsync(SocketAsyncEventArgs e)
        {
            if (!_socket.SendToAsync(e))
                Self.Tell(new SocketSent(e));
        }
    }
}