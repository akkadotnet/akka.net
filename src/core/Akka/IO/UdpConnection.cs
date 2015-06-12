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
    internal class UdpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly UdpConnectedExt _udpConn;
        private readonly IChannelRegistry _channelRegistry;
        private readonly IActorRef _commander;
        private readonly UdpConnected.Connect _connect;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        private DatagramChannel _channel;

        public UdpConnection(UdpConnectedExt udpConn, 
                             IChannelRegistry channelRegistry, 
                             IActorRef commander, 
                             UdpConnected.Connect connect)
        {
            _udpConn = udpConn;
            _channelRegistry = channelRegistry;
            _commander = commander;
            _connect = connect;

            Context.Watch(connect.Handler);

            //TODO: DNS lookup

            DoConnect(_connect.RemoteAddress);
        }

        private Tuple<UdpConnected.Send, IActorRef> _pendingSend;
        private bool WritePending
        {
            get { return _pendingSend != null; }
        }

        private void DoConnect(IPEndPoint address)
        {
            ReportConnectFailure(() =>
            {
                _channel = DatagramChannel.Open();
                _channel.ConfigureBlocking(false);
                var socket = _channel.Socket;
                _connect.Options.ForEach(x => x.BeforeDatagramBind(socket));
                if (_connect.LocalAddress != null)
                    socket.Bind(_connect.LocalAddress);
                _channel.Connect(_connect.RemoteAddress);
                _channelRegistry.Register(_channel, SocketAsyncOperation.Receive, Self);
            });
            _log.Debug("Successfully connected to [{0}]", _connect.RemoteAddress);
        }

        protected override bool Receive(object message)
        {
            var registration = message as ChannelRegistration;
            if (registration != null)
            {
                _connect.Options
                        .OfType<Inet.SocketOptionV2>().ForEach(v2 => v2.AfterConnect(_channel.Socket));
                _commander.Tell(UdpConnected.Connected.Instance);
                Context.Become(Connected(registration));
                return true;
            }
            return false;
        }

        private Receive Connected(ChannelRegistration registration)
        {
            return message =>
            {
                if (message is UdpConnected.SuspendReading)
                {
                    registration.DisableInterest(SocketAsyncOperation.Receive);
                    return true;
                }
                if (message is UdpConnected.ResumeReading)
                {
                    registration.EnableInterest(SocketAsyncOperation.Receive);
                    return true;
                }
                if (message is SelectionHandler.ChannelReadable)
                {
                    DoRead(registration, _connect.Handler);   
                    return true;
                }

                if (message is UdpConnected.Disconnect)
                {
                    _log.Debug("Closing UDP connection to [{0}]", _connect.RemoteAddress);
                    _channel.Close();
                    Sender.Tell(UdpConnected.Disconnected.Instance);
                    _log.Debug("Connection closed to [{0}], stopping listener", _connect.RemoteAddress);
                    Context.Stop(Self);
                    return true;
                }

                var send = message as UdpConnected.Send;
                if (send != null && WritePending)
                {
                    if (_udpConn.Settings.TraceLogging) _log.Debug("Dropping write because queue is full");
                    Sender.Tell(new UdpConnected.CommandFailed(send));
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
                    registration.EnableInterest(SocketAsyncOperation.Send);
                    return true;
                }
                if (message is SelectionHandler.ChannelWritable)
                {
                    DoWrite();
                    return true;
                }
                return false;
            };
        }

        private void DoRead(ChannelRegistration registration, IActorRef handler)
        {
            Action<int, ByteBuffer> innerRead = null;
            innerRead = (readsLeft, buffer) =>
            {
                buffer.Clear();
                buffer.Limit(_udpConn.Settings.DirectBufferSize);
                if (_channel.Read(buffer) > 0)
                {
                    buffer.Flip();
                    handler.Tell(new UdpConnected.Received(ByteString.Create(buffer)));
                    innerRead(readsLeft - 1, buffer);
                }

            };
            var buffr = _udpConn.BufferPool.Acquire();
            try
            {
                innerRead(_udpConn.Settings.BatchReceiveLimit, buffr);
            }
            finally
            {
                registration.EnableInterest(SocketAsyncOperation.Receive);
                _udpConn.BufferPool.Release(buffr);
            }

        }

        private void DoWrite()
        {
            var buffer = _udpConn.BufferPool.Acquire();
            try
            {
                var send = _pendingSend.Item1;
                var commander = _pendingSend.Item2;
                buffer.Clear();
                send.Payload.CopyToBuffer(buffer);
                buffer.Flip();
                var writtenBytes = _channel.Write(buffer);
                if (_udpConn.Settings.TraceLogging) _log.Debug("Wrote [{0}] bytes to channel", writtenBytes);

                // Datagram channel either sends the whole message, or nothing
                if (writtenBytes == 0) commander.Tell(new UdpConnected.CommandFailed(send));
                else if (send.WantsAck) commander.Tell(send.Ack);
            }
            finally
            {
                _udpConn.BufferPool.Release(buffer);
                _pendingSend = null;
            }
        }

        protected override void PostStop()
        {
            if (_channel.IsOpen())
            {
                _log.Debug("Closing DatagramChannel after being stopped");
                try
                {
                    _channel.Close();
                }
                catch (Exception ex)
                {
                    _log.Debug("Error closing DatagramChannel: {0}", ex);
                }
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
                _commander.Tell(new UdpConnected.CommandFailed(_connect));
                Context.Stop(Self);
            }
        }
    }
}
