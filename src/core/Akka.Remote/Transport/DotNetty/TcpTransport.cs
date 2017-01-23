#region copyright
// -----------------------------------------------------------------------
//  <copyright file="TcpTransport.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport.DotNetty
{
    internal abstract class TcpHandlers : CommonHandlers
    {
        private IHandleEventListener listener;
        protected readonly ByteOrder Order;
        
        protected void NotifyListener(IHandleEvent msg)
        {
            listener?.Notify(msg);
        }
        
        protected TcpHandlers(DotNettyTransport transport, ILoggingAdapter log) : base(transport, log)
        {
            Order = transport.Settings.ByteOrder;
        }
        
        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, object msg, IPEndPoint remoteAddress)
        {
            this.listener = listener;
        }
        
        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            return new TcpAssociationHandle(localAddress, remoteAddress, Transport, channel);
        }
        
        public override void ChannelInactive(IChannelHandlerContext context)
        {
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            base.ChannelInactive(context);
        }
        
        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buf = ((IByteBuffer)message).WithOrder(Order);
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
            base.ExceptionCaught(context, exception);
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            context.CloseAsync(); // close the channel
        }
    }

    internal sealed class TcpServerHandler : TcpHandlers
    {
        private readonly Task<IAssociationEventListener> associationEventListener;
        
        public TcpServerHandler(DotNettyTransport transport, ILoggingAdapter log, Task<IAssociationEventListener> associationEventListener) 
            : base(transport, log)
        {
            this.associationEventListener = associationEventListener;
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

            associationEventListener.ContinueWith(r =>
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
        private readonly TaskCompletionSource<AssociationHandle> statusPromise = new TaskCompletionSource<AssociationHandle>();
        private readonly Address remoteAddress;

        public Task<AssociationHandle> StatusFuture => statusPromise.Task;
        
        public TcpClientHandler(DotNettyTransport transport, ILoggingAdapter log, Address remoteAddress) 
            : base(transport, log)
        {
            this.remoteAddress = remoteAddress;
        }
        
        public override void ChannelActive(IChannelHandlerContext context)
        {
            InitOutbound(context.Channel, (IPEndPoint)context.Channel.RemoteAddress, null);
            base.ChannelActive(context);
        }

        private void InitOutbound(IChannel channel, IPEndPoint socketAddress, object msg)
        {
            AssociationHandle handle;
            Init(channel, socketAddress, remoteAddress, msg, out handle);
            statusPromise.TrySetResult(handle);
        }
    }

    internal sealed class TcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel _channel;
        private readonly DotNettyTransport _transport;
        private readonly ByteOrder _order;

        public TcpAssociationHandle(Address localAddress, Address remoteAddress, DotNettyTransport transport, IChannel channel)
            : base(localAddress, remoteAddress)
        {
            _channel = channel;
            _transport = transport;
            _order = transport.Settings.ByteOrder;
        }

        public override bool Write(ByteString payload)
        {
            if (_channel.Open && _channel.IsWritable)
            {
                var data = ToByteBuffer(payload);
                _channel.WriteAndFlushAsync(data);
                return true;
            }
            return false;
        }

        private IByteBuffer ToByteBuffer(ByteString payload)
        {
            //TODO: optimize DotNetty byte buffer usage 
            // (maybe custom IByteBuffer working directly on ByteString?)
            var buffer = Unpooled.WrappedBuffer(payload.ToByteArray());
            return buffer.WithOrder(_order);
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
                socketAddress = await MapEndpointAsync(socketAddress);
                var associate = await clientBootstrap.ConnectAsync(socketAddress);
                var handler = (TcpClientHandler)associate.Pipeline.Last();
                return await handler.StatusFuture;
            }
            catch (AggregateException e) when (e.InnerException is ConnectException)
            {
                var cause = (ConnectException)e.InnerException;
                var socketException = cause?.InnerException as SocketException;

                if (socketException?.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    throw new InvalidAssociationException(socketException.Message + " " + remoteAddress);
                }

                throw new InvalidAssociationException("Failed to associate with " + remoteAddress, e);
            }
            catch (AggregateException e) when (e.InnerException is ConnectTimeoutException)
            {
                var cause = (ConnectTimeoutException)e.InnerException;

                throw new InvalidAssociationException(cause.Message);
            }
        }

        private async Task<IPEndPoint> MapEndpointAsync(EndPoint socketAddress)
        {
            IPEndPoint ipEndPoint;

            var dns = socketAddress as DnsEndPoint;
            if (dns != null)
                ipEndPoint = await DnsToIPEndpoint(dns);
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