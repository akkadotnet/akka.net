//-----------------------------------------------------------------------
// <copyright file="HeliosTcpTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Google.ProtocolBuffers;
using Helios.Buffers;
using Helios.Channels;
using Helios.Exceptions;
using Helios.Util;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    abstract class TcpHandlers : CommonHandlers
    {
        private IHandleEventListener _listener;

        protected void NotifyListener(IHandleEvent msg)
        {
            _listener?.Notify(msg);
        }

        protected TcpHandlers(HeliosTransport wrappedTransport, ILoggingAdapter log) : base(wrappedTransport, log)
        {
        }

        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, object msg, IPEndPoint remoteAddress)
        {
            _listener = listener;
        }

        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            return new TcpAssociationHandle(localAddress, remoteAddress, WrappedTransport, channel);
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            base.ChannelInactive(context);
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buf = (IByteBuf)message;
            if (buf.ReadableBytes > 0)
            {
                // no need to copy the byte buffer contents; ByteString does that automatically
                var bytes = ByteString.CopyFrom(buf.Array, buf.ArrayOffset + buf.ReaderIndex, buf.ReadableBytes);
                NotifyListener(new InboundPayload(bytes));
            }

            // decrease the reference count to 0 (releases buffer)
            ReferenceCountUtil.SafeRelease(message);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            base.ExceptionCaught(context, exception);
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            context.CloseAsync(); // close the channel
        }
    }

    /// <summary>
    /// TCP handlers for inbound connections
    /// </summary>
    internal sealed class TcpServerHandler : TcpHandlers
    {
        private readonly Task<IAssociationEventListener> _associationEventListener;

        public TcpServerHandler(HeliosTransport wrappedTransport, ILoggingAdapter log, Task<IAssociationEventListener> associationEventListener) : base(wrappedTransport, log)
        {
            _associationEventListener = associationEventListener;
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
                var remoteAddress = HeliosTransport.MapSocketToAddress(socketAddress, WrappedTransport.SchemeIdentifier,
                    WrappedTransport.System.Name);
                AssociationHandle handle;
                Init(channel, socketAddress, remoteAddress, msg, out handle);
                listener.Notify(new InboundAssociation(handle));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    /// <summary>
    /// TCP handlers for outbound connections
    /// </summary>
    class TcpClientHandler : TcpHandlers
    {
        protected readonly TaskCompletionSource<AssociationHandle> StatusPromise = new TaskCompletionSource<AssociationHandle>();
        private readonly Address _remoteAddress;
        public Task<AssociationHandle> StatusFuture { get { return StatusPromise.Task; } }

        public TcpClientHandler(HeliosTransport wrappedTransport, ILoggingAdapter log, Address remoteAddress) : base(wrappedTransport, log)
        {
            _remoteAddress = remoteAddress;
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            InitOutbound(context.Channel,(IPEndPoint)context.Channel.RemoteAddress, null);
            base.ChannelActive(context);
        }

        void InitOutbound(IChannel channel, IPEndPoint socketAddress, object msg)
        {
            AssociationHandle handle;
            Init(channel, socketAddress, _remoteAddress, msg, out handle);
            StatusPromise.TrySetResult(handle);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    class TcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel _channel;
        private HeliosTransport _transport;

        public TcpAssociationHandle(Address localAddress, Address remoteAddress, HeliosTransport transport, IChannel connection)
            : base(localAddress, remoteAddress)
        {
            _channel = connection;
            _transport = transport;
        }

        public override bool Write(ByteString payload)
        {
            if (_channel.IsOpen && _channel.IsWritable)
            {
                _channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(payload.ToByteArray()));
                return true;
            }
            return false;
        }

        public override void Disassociate()
        {
            _channel.CloseAsync();
        }
    }

    /// <summary>
    /// TCP implementation of a <see cref="HeliosTransport"/>.
    /// 
    /// <remarks>
    /// Due to the connection-oriented nature of TCP connections, this transport doesn't have to do any
    /// additional bookkeeping when transports are disposed or opened.
    /// </remarks>
    /// </summary>
    class HeliosTcpTransport : HeliosTransport
    {
        public HeliosTcpTransport(ActorSystem system, Config config)
            : base(system, config)
        {
        }

        protected override Task<AssociationHandle> AssociateInternal(Address remoteAddress)
        {
            var clientBootstrap = ClientFactory(remoteAddress);
            var socketAddress = AddressToSocketAddress(remoteAddress);
            var associate = clientBootstrap.ConnectAsync(socketAddress).ContinueWith(tr =>
            {
                var channel = tr.Result;
                var handler = (TcpClientHandler) channel.Pipeline.Last();
                return handler.StatusFuture;
            }).Unwrap().ContinueWith(r =>
            {
                if(r.IsCanceled)
                    throw new HeliosConnectionException(ExceptionType.TimedOut, "Connection was cancelled");
                if (r.IsFaulted)
                {
                    var ex = r.Exception;
                    throw new HeliosConnectionException(ExceptionType.Unknown, $"failed as a result of {ex}", ex);
                }

                var ah = r.Result;
                return ah;
            });

          

            return associate;
        }
    }
}

