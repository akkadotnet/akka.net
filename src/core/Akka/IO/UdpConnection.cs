//-----------------------------------------------------------------------
// <copyright file="UdpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        public UdpConnection(UdpConnectedExt udp, IActorRef commander, Connect connect)
        {
            Udp = udp;
            _commander = commander;
            _connect = connect;

            Context.Watch(connect.Handler);

            if (connect.RemoteAddress is DnsEndPoint remoteAddress)
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

        private (Send send, IActorRef sender)? _pendingSend = null;
        private bool WritePending => _pendingSend != null;

        private Receive Resolving(DnsEndPoint remoteAddress) => message =>
        {
            if (message is Dns.Resolved r)
            {
                DoConnect(new IPEndPoint(r.Addr, remoteAddress.Port));
                return true;
            }
            return false;
        };

        private void DoConnect(EndPoint address)
        {
            ReportConnectFailure(() =>
            {
                _socket = (_connect.Options.OfType<Inet.DatagramChannelCreator>().FirstOrDefault() ??
                           new Inet.DatagramChannelCreator()).Create(address.AddressFamily);
                _socket.Blocking = false;

                foreach (var option in _connect.Options)
                {
                    option.BeforeDatagramBind(_socket);
                }

                if (_connect.LocalAddress != null)
                    _socket.Bind(address);

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

        protected override bool Receive(object message) => throw new NotSupportedException();

        private bool Connected(object message)
        {
            switch (message)
            {
                case SuspendReading _: _readingSuspended = true; return true;
                case ResumeReading _:
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
                
                case SocketReceived socketReceived:
                    _pendingReceive = null;
                    DoRead(socketReceived, _connect.Handler); 
                    return true;
                
                case Disconnect _:
                    {
                        Log.Debug("Closing UDP connection to [{0}]", _connect.RemoteAddress);

                        _socket.Dispose();

                        Sender.Tell(Disconnected.Instance);
                        Log.Debug("Connection closed to [{0}], stopping listener", _connect.RemoteAddress);
                        Context.Stop(Self);
                        return true;
                    }
                case Send send:
                    {
                        if (WritePending)
                        {
                            if (Udp.Settings.TraceLogging) Log.Debug("Dropping write because queue is full");
                            Sender.Tell(new CommandFailed(send));
                        }
                        else
                        {
                            if (!send.Payload.IsEmpty)
                            {
                                _pendingSend = (send, Sender);
                                DoWrite();
                            }
                            else
                            {
                                if (send.WantsAck)
                                    Sender.Tell(send.Ack);
                            }
                        }
                        return true;
                    }
                case SocketSent sent:
                {
                    if (_pendingSend == null)
                        throw new Exception("There are no pending sent");
                    
                    var (send, sender) = _pendingSend.Value;
                    if (send.WantsAck)
                        sender.Tell(send.Ack);
                    if (Udp.Settings.TraceLogging)
                        Log.Debug("Wrote [{0}] bytes to socket", sent.BytesTransferred);
                    _pendingSend = null;
                    return true;
                }
                default: return false;
            }
        }

        private void DoRead(SocketReceived received, IActorRef handler)
        {
            var data = new Received(received.Data);

            if (!_readingSuspended)
            {
                handler.Tell(data);
                ReceiveAsync();
            }
            else _pendingRead = data;
        }

        private void DoWrite()
        {
            try
            {
                var (send, sender) = _pendingSend.Value;
                var data = send.Payload;

                var bytesWritten = _socket.Send(data.Buffers);
                if (Udp.Settings.TraceLogging)
                    Log.Debug("Wrote [{0}] bytes to socket", bytesWritten);

                // Datagram channel either sends the whole message or nothing
                if (bytesWritten == 0) _commander.Tell(new CommandFailed(send));
                else if (send.WantsAck) _commander.Tell(send.Ack);
            }
            finally
            {
                _pendingSend = null;
            }
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
                Log.Error(e, "Failure while connecting UDP channel to remote address [{0}] local address [{1}]", _connect.RemoteAddress, _connect.LocalAddress);
                _commander.Tell(new CommandFailed(_connect));
                Context.Stop(Self);
            }
        }

        private SocketAsyncEventArgs _pendingReceive;
        
        private void ReceiveAsync()
        {
            if (_pendingReceive != null)
                return;
            
            var e = Udp.SocketEventArgsPool.Acquire(Self);
            _pendingReceive = e;
            if (!_socket.ReceiveAsync(e))
            {
                Self.Tell(new SocketReceived(e));
                Udp.SocketEventArgsPool.Release(e);
            }
        }
    }
}
