#region copyright
// -----------------------------------------------------------------------
//  <copyright file="HeliosTcpTransport.cs" company="Akka.NET project">
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
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;
using Google.Protobuf;
using Helios.Buffers;
using Helios.Channels;
using Helios.Exceptions;
using Helios.Util;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    abstract class TcpHandlers : CommonHandlers
    {
        private IHandleEventListener _listener;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="msg">TBD</param>
        protected void NotifyListener(IHandleEvent msg)
        {
            _listener?.Notify(msg);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="log">TBD</param>
        protected TcpHandlers(HeliosTransport wrappedTransport, ILoggingAdapter log) : base(wrappedTransport, log)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="listener">TBD</param>
        /// <param name="msg">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        protected override void RegisterListener(IChannel channel, IHandleEventListener listener, object msg, IPEndPoint remoteAddress)
        {
            _listener = listener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        protected override AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress)
        {
            return new TcpAssociationHandle(localAddress, remoteAddress, WrappedTransport, channel);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        public override void ChannelInactive(IChannelHandlerContext context)
        {
            NotifyListener(new Disassociated(DisassociateInfo.Unknown));
            base.ChannelInactive(context);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="message">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="exception">TBD</param>
        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            var se = exception as SocketException;
            if (se?.SocketErrorCode == SocketError.OperationAborted)
            {
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

    /// <summary>
    /// TCP handlers for inbound connections
    /// </summary>
    internal sealed class TcpServerHandler : TcpHandlers
    {
        private readonly Task<IAssociationEventListener> _associationEventListener;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="log">TBD</param>
        /// <param name="associationEventListener">TBD</param>
        public TcpServerHandler(HeliosTransport wrappedTransport, ILoggingAdapter log, Task<IAssociationEventListener> associationEventListener) : base(wrappedTransport, log)
        {
            _associationEventListener = associationEventListener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
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
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly TaskCompletionSource<AssociationHandle> StatusPromise = new TaskCompletionSource<AssociationHandle>();
        private readonly Address _remoteAddress;
        /// <summary>
        /// TBD
        /// </summary>
        public Task<AssociationHandle> StatusFuture { get { return StatusPromise.Task; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="log">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        public TcpClientHandler(HeliosTransport wrappedTransport, ILoggingAdapter log, Address remoteAddress) : base(wrappedTransport, log)
        {
            _remoteAddress = remoteAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        public override void ChannelActive(IChannelHandlerContext context)
        {
            InitOutbound(context.Channel, (IPEndPoint)context.Channel.RemoteAddress, null);
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
    [InternalApi]
    class TcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel _channel;
        private HeliosTransport _transport;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="transport">TBD</param>
        /// <param name="connection">TBD</param>
        public TcpAssociationHandle(Address localAddress, Address remoteAddress, HeliosTransport transport, IChannel connection)
            : base(localAddress, remoteAddress)
        {
            _channel = connection;
            _transport = transport;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override bool Write(ByteString payload)
        {
            if (_channel.IsOpen && _channel.IsWritable)
            {
                _channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(payload.ToByteArray()));
                return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="config">TBD</param>
        public HeliosTcpTransport(ActorSystem system, Config config)
            : base(system, config, "akka.remote.helios.tcp")
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <exception cref="HeliosConnectionException">TBD</exception>
        /// <returns>TBD</returns>
        protected override async Task<AssociationHandle> AssociateInternal(Address remoteAddress)
        {
            try
            {
                var clientBootstrap = ClientFactory(remoteAddress);
                var socketAddress = AddressToSocketAddress(remoteAddress);

                var associate = await clientBootstrap.ConnectAsync(socketAddress).ConfigureAwait(false);

                var handler = (TcpClientHandler)associate.Pipeline.Last();
                return await handler.StatusFuture.ConfigureAwait(false);
            }
            catch (AggregateException e) when (e.InnerException is ConnectException)
            {
                var heliosException = (ConnectException)e.InnerException;
                var socketException = heliosException?.InnerException as SocketException;

                if (socketException?.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    throw new InvalidAssociationException(socketException.Message + " " + remoteAddress);
                }

                throw new InvalidAssociationException("Failed to associate with " + remoteAddress, e);
            }
            catch (AggregateException e) when (e.InnerException is ConnectTimeoutException)
            {
                var heliosException = (ConnectTimeoutException)e.InnerException;

                throw new InvalidAssociationException(heliosException.Message);
            }
        }
    }
}
