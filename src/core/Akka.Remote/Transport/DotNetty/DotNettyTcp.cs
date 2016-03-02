using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    class TcpAssociationHandle : AssociationHandle
    {
        public DotNettyTransport Transport { get; }
        private readonly IChannel _channel;


        public TcpAssociationHandle(Address localAddress, Address remoteAddress, DotNettyTransport transport, IChannel channel) : base(localAddress, remoteAddress)
        {
            Transport = transport;
            _channel = channel;
        }

        

        public override bool Write(ByteString payload)
        {
            if (_channel.IsWritable && _channel.Open)
            {
                _channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(payload.ToByteArray()));
                return true;
            }
            return false;
        }

        public override void Disassociate()
        {
            DotNettyTransport.GracefulClose(_channel);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    static class TcpHelper
    {
        public readonly static byte[] EmptyBytes = new byte[0];

        internal static void PropagateMessage(IChannelHandlerContext ctx, object message)
        {
            var buffer = message as IByteBuffer;
            var bytes = buffer != null ? buffer.ToArray() : EmptyBytes;
            if (bytes.Length > 0) ChannelLocalActor.Notify(ctx.Channel, new InboundPayload(ByteString.CopyFrom(bytes)));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    class TcpServerHandler : ServerHandler
    {
        public TcpServerHandler(DotNettyTransport transport, Task<IAssociationEventListener> associationListenerFuture) : base(transport, associationListenerFuture)
        {
        }

        protected override void OnConnect(IChannelHandlerContext ctx)
        {
            InitInbound(ctx.Channel, ctx.Channel.RemoteAddress, null);
        }

        protected override void OnDisconnect(IChannelHandlerContext ctx)
        {
            ChannelLocalActor.Notify(ctx.Channel, new Disassociated(DisassociateInfo.Unknown));
        }

        protected override void OnMessage(IChannelHandlerContext ctx, object message)
        {
            TcpHelper.PropagateMessage(ctx, message);
        }

        protected override void OnException(IChannelHandlerContext ctx, Exception ex)
        {
            ChannelLocalActor.Notify(ctx.Channel, new Disassociated(DisassociateInfo.Unknown));
            ctx.CloseAsync(); //no graceful close here
        }

        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            return new TcpAssociationHandle(localAddress, remoteAddress, Transport, channel);
        }

        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, IByteBuffer message, EndPoint remoteSocketAddress)
        {
            ChannelLocalActor.Set(channel, listener);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    class TcpClientHandler : ClientHandler
    {
        public TcpClientHandler(DotNettyTransport transport, Address remoteAddress) : base(transport, remoteAddress)
        {
        }

        protected override void OnConnect(IChannelHandlerContext ctx)
        {
            InitOutbound(ctx.Channel, ctx.Channel.RemoteAddress, null);
        }

        protected override void OnDisconnect(IChannelHandlerContext ctx)
        {
            ChannelLocalActor.Notify(ctx.Channel, new Disassociated(DisassociateInfo.Unknown));
        }

        protected override void OnMessage(IChannelHandlerContext ctx, object message)
        {
            TcpHelper.PropagateMessage(ctx, message);
        }

        protected override void OnException(IChannelHandlerContext ctx, Exception ex)
        {
            ChannelLocalActor.Notify(ctx.Channel, new Disassociated(DisassociateInfo.Unknown));
            ctx.CloseAsync(); //no graceful close here
        }

        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            return new TcpAssociationHandle(localAddress, remoteAddress, Transport, channel);
        }

        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, IByteBuffer message, EndPoint remoteSocketAddress)
        {
            ChannelLocalActor.Set(channel, listener);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    static class ChannelLocalActor
    {
        private static readonly ConcurrentDictionary<IChannel, IHandleEventListener> ChannelActors = new ConcurrentDictionary<IChannel, IHandleEventListener>();

        public static void Set(IChannel channel, IHandleEventListener listener = null)
        {
            ChannelActors.AddOrUpdate(channel, listener, (connection, eventListener) => listener);
        }

        public static void Remove(IChannel channel)
        {
            IHandleEventListener listener;
            ChannelActors.TryRemove(channel, out listener);
        }

        public static void Notify(IChannel channel, IHandleEvent msg)
        {
            IHandleEventListener listener;

            if (ChannelActors.TryGetValue(channel, out listener))
            {
                listener.Notify(msg);
            }
        }
    }
}
