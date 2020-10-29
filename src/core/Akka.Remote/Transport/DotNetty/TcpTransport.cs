//-----------------------------------------------------------------------
// <copyright file="TcpTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Google.Protobuf;

namespace Akka.Remote.Transport.DotNetty
{
    internal abstract class TcpHandlers : CommonHandlers
    {
        private IHandleEventListener _listener;
        
        protected void NotifyListener(IHandleEvent msg)
        {
            _listener?.Notify(msg);
        }
        
        protected TcpHandlers(DotNettyTransport transport, ILoggingAdapter log) : base(transport, log)
        {
        }
        
        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, object msg, IPEndPoint remoteAddress)
        {
            this._listener = listener;
        }
        
        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            if(Transport.Settings.BatchWriterSettings.EnableBatching)
                return new BatchingTcpAssociationHandle(localAddress, remoteAddress, Transport, channel);
            return new NonBatchingTcpAssociationHandle(localAddress, remoteAddress, Transport, channel);
        }
        
        public override void ChannelInactive(IChannelHandlerContext context)
        {
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            base.ChannelInactive(context);
        }
        
        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buf = ((IByteBuffer)message);
            if (buf.ReadableBytes > 0)
            {
                // no need to copy the byte buffer contents; ByteString does that automatically
                var bytes = ByteString.CopyFrom(buf.Array, buf.ArrayOffset + buf.ReaderIndex, buf.ReadableBytes);
                NotifyListener(new InboundPayload(bytes));
            }

            // decrease the reference count to 0 (releases buffer)
            ReferenceCountUtil.SafeRelease(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="exception">TBD</param>
        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            var se = exception as SocketException;

            if (se?.SocketErrorCode == SocketError.OperationAborted || se?.SocketErrorCode == SocketError.ConnectionAborted)
            {
                Log.Info("Socket read operation aborted. Connection is about to be closed. Channel [{0}->{1}](Id={2})",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);

                NotifyListener(new Disassociated(DisassociateInfo.Shutdown));
            }
            else if (se?.SocketErrorCode == SocketError.ConnectionReset)
            {
                Log.Info("Connection was reset by the remote peer. Channel [{0}->{1}](Id={2})",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);

                NotifyListener(new Disassociated(DisassociateInfo.Shutdown));
            }
            else
            {
                base.ExceptionCaught(context, exception);
                NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            }

            context.CloseAsync(); // close the channel
        }
    }

    internal sealed class TcpServerHandler : TcpHandlers
    {
        private readonly Task<IAssociationEventListener> _associationEventListener;
        
        public TcpServerHandler(DotNettyTransport transport, ILoggingAdapter log, Task<IAssociationEventListener> associationEventListener) 
            : base(transport, log)
        {
            this._associationEventListener = associationEventListener;
        }
        
        public override void ChannelActive(IChannelHandlerContext context)
        {
            InitInbound(context.Channel, (IPEndPoint)context.Channel.RemoteAddress, null);
            base.ChannelActive(context);
        }

        void InitInbound(IChannel channel, IPEndPoint socketAddress, object msg)
        {
            // disable automatic reads
            channel.Configuration.AutoRead = false;

            _associationEventListener.ContinueWith(r =>
            {
                var listener = r.Result;
                var remoteAddress = DotNettyTransport.MapSocketToAddress(
                    socketAddress: socketAddress, 
                    schemeIdentifier: Transport.SchemeIdentifier,
                    systemName: Transport.System.Name);
                AssociationHandle handle;
                Init(channel, socketAddress, remoteAddress, msg, out handle);
                listener.Notify(new InboundAssociation(handle));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    internal sealed class TcpClientHandler : TcpHandlers
    {
        private readonly TaskCompletionSource<AssociationHandle> _statusPromise = new TaskCompletionSource<AssociationHandle>();
        private readonly Address _remoteAddress;

        public Task<AssociationHandle> StatusFuture => _statusPromise.Task;
        
        public TcpClientHandler(DotNettyTransport transport, ILoggingAdapter log, Address remoteAddress) 
            : base(transport, log)
        {
            _remoteAddress = remoteAddress;
        }
        
        public override void ChannelActive(IChannelHandlerContext context)
        {
            InitOutbound(context.Channel, (IPEndPoint)context.Channel.RemoteAddress, null);
            base.ChannelActive(context);

        }

        private void InitOutbound(IChannel channel, IPEndPoint socketAddress, object msg)
        {
            AssociationHandle handle;
            Init(channel, socketAddress, _remoteAddress, msg, out handle);
            _statusPromise.TrySetResult(handle);
        }
    }

    internal sealed class BatchingTcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel _channel;

        public BatchingTcpAssociationHandle(Address localAddress, Address remoteAddress, DotNettyTransport transport, IChannel channel)
            : base(localAddress, remoteAddress)
        {
            _channel = channel;
        }

        public override bool Write(ByteString payload)
        {
            if (_channel.Open)
            {
                var data = ToByteBuffer(_channel, payload);
                _channel.WriteAsync(data);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IByteBuffer ToByteBuffer(IChannel channel, ByteString payload)
        {
            var buffer = Unpooled.WrappedBuffer(payload.ToByteArray());
            return buffer;
        }

        public override void Disassociate()
        {
            _channel.Flush(); // flush before we close
            _channel.CloseAsync();
        }
    }

    internal sealed class NonBatchingTcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel _channel;

        public NonBatchingTcpAssociationHandle(Address localAddress, Address remoteAddress, DotNettyTransport transport, IChannel channel)
            : base(localAddress, remoteAddress)
        {
            _channel = channel;
        }

        public override bool Write(ByteString payload)
        {
            if (_channel.Open)
            {
                var data = ToByteBuffer(_channel, payload);
                _channel.WriteAndFlushAsync(data);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IByteBuffer ToByteBuffer(IChannel channel, ByteString payload)
        {
            var buffer = Unpooled.WrappedBuffer(payload.ToByteArray());
            return buffer;
        }

        public override void Disassociate()
        {
            _channel.CloseAsync();
        }
    }

    internal sealed class TcpTransport : DotNettyTransport
    {
        public TcpTransport(ActorSystem system, Config config) : base(system, config)
        {
        }

        protected override async Task<AssociationHandle> AssociateInternal(Address remoteAddress)
        {
            try
            {
                var clientBootstrap = ClientFactory(remoteAddress);
                var socketAddress = AddressToSocketAddress(remoteAddress);
                socketAddress = await MapEndpointAsync(socketAddress).ConfigureAwait(false);
                var associate = await clientBootstrap.ConnectAsync(socketAddress).ConfigureAwait(false);
                var handler = (TcpClientHandler)associate.Pipeline.Last();
                return await handler.StatusFuture.ConfigureAwait(false);
            }
            catch (ConnectException c)
            {
                throw HandleConnectException(remoteAddress, c, null);
            }
            catch (AggregateException e) when (e.InnerException is ConnectException)
            {
                var cause = (ConnectException)e.InnerException;
                throw HandleConnectException(remoteAddress, cause, e);
            }
            catch (ConnectTimeoutException t)
            {
                throw new InvalidAssociationException(t.Message);
            }
            catch (AggregateException e) when (e.InnerException is ConnectTimeoutException)
            {
                var cause = (ConnectTimeoutException)e.InnerException;

                throw new InvalidAssociationException(cause.Message);
            }
        }

        private static Exception HandleConnectException(Address remoteAddress, ConnectException cause, AggregateException e)
        {
            var socketException = cause?.InnerException as SocketException;

            if (socketException?.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return new InvalidAssociationException(socketException.Message + " " + remoteAddress);
            }

            return new InvalidAssociationException("Failed to associate with " + remoteAddress, e ?? (Exception)cause);
        }

        private async Task<IPEndPoint> MapEndpointAsync(EndPoint socketAddress)
        {
            IPEndPoint ipEndPoint;

            if (socketAddress is DnsEndPoint dns)
                ipEndPoint = await DnsToIPEndpoint(dns).ConfigureAwait(false);
            else
                ipEndPoint = (IPEndPoint) socketAddress;

            if (ipEndPoint.Address.Equals(IPAddress.Any) || ipEndPoint.Address.Equals(IPAddress.IPv6Any))
            {
                // client hack
                return ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Loopback, ipEndPoint.Port)
                    : new IPEndPoint(IPAddress.Loopback, ipEndPoint.Port);
            }
            return ipEndPoint;
        }
    }

}
