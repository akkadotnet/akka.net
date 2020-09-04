//-----------------------------------------------------------------------
// <copyright file="DotNettyTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Handlers.Tls;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Akka.Remote.Transport.DotNetty
{
    internal abstract class CommonHandlers : ChannelHandlerAdapter
    {
        protected readonly DotNettyTransport Transport;
        protected readonly ILoggingAdapter Log;

        protected CommonHandlers(DotNettyTransport transport, ILoggingAdapter log)
        {
            Transport = transport;
            Log = log;
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            base.ChannelActive(context);
            if (!Transport.ConnectionGroup.TryAdd(context.Channel))
            {
                Log.Warning("Unable to ADD channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            base.ChannelInactive(context);
            if (!Transport.ConnectionGroup.TryRemove(context.Channel))
            {
                Log.Warning("Unable to REMOVE channel [{0}->{1}](Id={2}) from connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            base.ExceptionCaught(context, exception);
            Log.Error(exception, "Error caught channel [{0}->{1}](Id={2})", context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
        }

        protected abstract AssociationHandle CreateHandle(IChannel channel, Address localAddress, Address remoteAddress);

        protected abstract void RegisterListener(IChannel channel, IHandleEventListener listener, object msg, IPEndPoint remoteAddress);

        protected void Init(IChannel channel, IPEndPoint remoteSocketAddress, Address remoteAddress, object msg,
            out AssociationHandle op)
        {
            var localAddress = DotNettyTransport.MapSocketToAddress((IPEndPoint)channel.LocalAddress, Transport.SchemeIdentifier, Transport.System.Name, Transport.Settings.Hostname);

            if (localAddress != null)
            {
                var handle = CreateHandle(channel, localAddress, remoteAddress);
                handle.ReadHandlerSource.Task.ContinueWith(s =>
                {
                    var listener = s.Result;
                    RegisterListener(channel, listener, msg, remoteSocketAddress);
                    channel.Configuration.AutoRead = true; // turn reads back on
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted);
                op = handle;
            }
            else
            {
                op = null;
                channel.CloseAsync();
            }
        }
    }

    internal class DotNettyTransportException : RemoteTransportException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DotNettyTransportException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public DotNettyTransportException(string message, Exception cause = null) : base(message, cause)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="DotNettyTransportException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected DotNettyTransportException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    internal abstract class DotNettyTransport : Transport
    {
        internal readonly ConcurrentSet<IChannel> ConnectionGroup;

        protected readonly TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise;
        protected readonly ILoggingAdapter Log;
        protected volatile Address LocalAddress;
        protected internal volatile IChannel ServerChannel;

        private readonly IEventLoopGroup _serverEventLoopGroup;
        private readonly IEventLoopGroup _clientEventLoopGroup;

        protected DotNettyTransport(ActorSystem system, Config config)
        {
            System = system;
            Config = config;

            if (system.Settings.Config.HasPath("akka.remote.helios.tcp"))
            {
                var heliosFallbackConfig = system.Settings.Config.GetConfig("akka.remote.helios.tcp");
                config = heliosFallbackConfig.WithFallback(config);
            }

            Settings = DotNettyTransportSettings.Create(config);
            Log = Logging.GetLogger(System, GetType());
            _serverEventLoopGroup = new MultithreadEventLoopGroup(Settings.ServerSocketWorkerPoolSize);
            _clientEventLoopGroup = new MultithreadEventLoopGroup(Settings.ClientSocketWorkerPoolSize);
            ConnectionGroup = new ConcurrentSet<IChannel>();
            AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();

            SchemeIdentifier = (Settings.EnableSsl ? "ssl." : string.Empty) + Settings.TransportMode.ToString().ToLowerInvariant();
        }

        public DotNettyTransportSettings Settings { get; }
        public sealed override string SchemeIdentifier { get; protected set; }
        public override long MaximumPayloadBytes => Settings.MaxFrameSize;
        private TransportMode InternalTransport => Settings.TransportMode;

        public sealed override bool IsResponsibleFor(Address remote) => true;

        protected async Task<IChannel> NewServer(EndPoint listenAddress)
        {
            if (InternalTransport != TransportMode.Tcp)
                throw new NotImplementedException("Haven't implemented UDP transport at this time");

            if (listenAddress is DnsEndPoint dns)
            {
                listenAddress = await DnsToIPEndpoint(dns).ConfigureAwait(false);
            }

            return await ServerFactory().BindAsync(listenAddress).ConfigureAwait(false);
        }

        public override async Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            EndPoint listenAddress;
            IPAddress ip;
            if (IPAddress.TryParse(Settings.Hostname, out ip))
                listenAddress = new IPEndPoint(ip, Settings.Port);
            else
                listenAddress = new DnsEndPoint(Settings.Hostname, Settings.Port);

            try
            {
                var newServerChannel = await NewServer(listenAddress).ConfigureAwait(false);

                // Block reads until a handler actor is registered
                // no incoming connections will be accepted until this value is reset
                // it's possible that the first incoming association might come in though
                newServerChannel.Configuration.AutoRead = false;
                ConnectionGroup.TryAdd(newServerChannel);
                ServerChannel = newServerChannel;

                var addr = MapSocketToAddress(
                    socketAddress: (IPEndPoint)newServerChannel.LocalAddress,
                    schemeIdentifier: SchemeIdentifier,
                    systemName: System.Name,
                    hostName: Settings.PublicHostname,
                    publicPort: Settings.PublicPort);

                if (addr == null) throw new ConfigurationException($"Unknown local address type {newServerChannel.LocalAddress}");

                LocalAddress = addr;
                // resume accepting incoming connections
#pragma warning disable 4014 // we WANT this task to run without waiting
                AssociationListenerPromise.Task.ContinueWith(result => newServerChannel.Configuration.AutoRead = true,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
#pragma warning restore 4014


                return (addr, AssociationListenerPromise);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to bind to {0}; shutting down DotNetty transport.", listenAddress);
                try
                {
                    await Shutdown().ConfigureAwait(false);
                }
                catch
                {
                    // ignore errors occurring during shutdown
                }
                throw;
            }
        }

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (!ServerChannel.Open)
                throw new ChannelException("Transport is not open");

            return await AssociateInternal(remoteAddress).ConfigureAwait(false);
        }

        protected abstract Task<AssociationHandle> AssociateInternal(Address remoteAddress);

        public override async Task<bool> Shutdown()
        {
            try
            {
                var tasks = new List<Task>();
                foreach (var channel in ConnectionGroup)
                {
                    tasks.Add(channel.CloseAsync());
                }
                var all = Task.WhenAll(tasks);
                await all.ConfigureAwait(false);

                var server = ServerChannel?.CloseAsync() ?? TaskEx.Completed;
                await server.ConfigureAwait(false);

                return all.IsCompleted && server.IsCompleted;
            }
            finally
            {
                // free all of the connection objects we were holding onto
                ConnectionGroup.Clear();
#pragma warning disable 4014 // shutting down the worker groups can take up to 10 seconds each. Let that happen asnychronously.
                _clientEventLoopGroup.ShutdownGracefullyAsync();
                _serverEventLoopGroup.ShutdownGracefullyAsync();
#pragma warning restore 4014
            }
        }

        protected Bootstrap ClientFactory(Address remoteAddress)
        {
            if (InternalTransport != TransportMode.Tcp)
                throw new NotSupportedException("Currently DotNetty client supports only TCP tranport mode.");

            var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

            var client = new Bootstrap()
                .Group(_clientEventLoopGroup)
                .Option(ChannelOption.SoReuseaddr, Settings.TcpReuseAddr)
                .Option(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                .Option(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                .Option(ChannelOption.ConnectTimeout, Settings.ConnectTimeout)
                .Option(ChannelOption.AutoRead, false)
                .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                .ChannelFactory(() => Settings.EnforceIpFamily
                    ? new TcpSocketChannel(addressFamily)
                    : new TcpSocketChannel())
                .Handler(new ActionChannelInitializer<TcpSocketChannel>(channel => SetClientPipeline(channel, remoteAddress)));

            if (Settings.ReceiveBufferSize.HasValue) client.Option(ChannelOption.SoRcvbuf, Settings.ReceiveBufferSize.Value);
            if (Settings.SendBufferSize.HasValue) client.Option(ChannelOption.SoSndbuf, Settings.SendBufferSize.Value);
            if (Settings.WriteBufferHighWaterMark.HasValue) client.Option(ChannelOption.WriteBufferHighWaterMark, Settings.WriteBufferHighWaterMark.Value);
            if (Settings.WriteBufferLowWaterMark.HasValue) client.Option(ChannelOption.WriteBufferLowWaterMark, Settings.WriteBufferLowWaterMark.Value);

            return client;
        }

        protected async Task<IPEndPoint> DnsToIPEndpoint(DnsEndPoint dns)
        {
            IPEndPoint endpoint;
            //if (!Settings.EnforceIpFamily)
            //{
            //    endpoint = await ResolveNameAsync(dns).ConfigureAwait(false);
            //}
            //else
            //{
            var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            endpoint = await ResolveNameAsync(dns, addressFamily).ConfigureAwait(false);
            //}
            return endpoint;
        }

        #region private methods 

        private void SetInitialChannelPipeline(IChannel channel)
        {
            var pipeline = channel.Pipeline;

            if (Settings.LogTransport)
            {
                pipeline.AddLast("Logger", new AkkaLoggingHandler(Log));
            }

            if (InternalTransport == TransportMode.Tcp)
            {
                pipeline.AddLast("FrameDecoder", new LengthFieldBasedFrameDecoder(Settings.ByteOrder, (int)MaximumPayloadBytes, 0, 4, 0, 4, true));
                if (Settings.BackwardsCompatibilityModeEnabled)
                {
                    pipeline.AddLast("FrameEncoder", new HeliosBackwardsCompatabilityLengthFramePrepender(4, false));
                }
                else
                {
                    pipeline.AddLast("FrameEncoder", new LengthFieldPrepender(Settings.ByteOrder, 4, 0, false));
                }
            }

            if(Settings.BatchWriterSettings.EnableBatching)
                pipeline.AddLast("BatchWriter", new BatchWriter(Settings.BatchWriterSettings));
        }

        private void SetClientPipeline(IChannel channel, Address remoteAddress)
        {
            if (Settings.EnableSsl)
            {
                var certificate = Settings.Ssl.Certificate;
                var host = certificate.GetNameInfo(X509NameType.DnsName, false);

                var tlsHandler = Settings.Ssl.SuppressValidation
                    ? new TlsHandler(stream => new SslStream(stream, true, (sender, cert, chain, errors) => true), new ClientTlsSettings(host))
                    : TlsHandler.Client(host, certificate);

                channel.Pipeline.AddFirst("TlsHandler", tlsHandler);
            }

            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;

            if (InternalTransport == TransportMode.Tcp)
            {
                var handler = new TcpClientHandler(this, Logging.GetLogger(System, typeof(TcpClientHandler)), remoteAddress);
                pipeline.AddLast("ClientHandler", handler);
            }
        }

        private void SetServerPipeline(IChannel channel)
        {
            if (Settings.EnableSsl)
            {
                channel.Pipeline.AddFirst("TlsHandler", TlsHandler.Server(Settings.Ssl.Certificate));
            }

            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;

            if (Settings.TransportMode == TransportMode.Tcp)
            {
                var handler = new TcpServerHandler(this, Logging.GetLogger(System, typeof(TcpServerHandler)), AssociationListenerPromise.Task);
                pipeline.AddLast("ServerHandler", handler);
            }
        }

        private ServerBootstrap ServerFactory()
        {
            if (InternalTransport != TransportMode.Tcp)
                throw new NotSupportedException("Currently DotNetty server supports only TCP tranport mode.");

            var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

            var server = new ServerBootstrap()
                .Group(_serverEventLoopGroup)
                .Option(ChannelOption.SoReuseaddr, Settings.TcpReuseAddr)
                .Option(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                .Option(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                .Option(ChannelOption.AutoRead, false)
                .Option(ChannelOption.SoBacklog, Settings.Backlog)
                .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                .ChannelFactory(() => Settings.EnforceIpFamily
                    ? new TcpServerSocketChannel(addressFamily)
                    : new TcpServerSocketChannel())
                .ChildHandler(new ActionChannelInitializer<TcpSocketChannel>(SetServerPipeline));

            if (Settings.ReceiveBufferSize.HasValue) server.Option(ChannelOption.SoRcvbuf, Settings.ReceiveBufferSize.Value);
            if (Settings.SendBufferSize.HasValue) server.Option(ChannelOption.SoSndbuf, Settings.SendBufferSize.Value);
            if (Settings.WriteBufferHighWaterMark.HasValue) server.Option(ChannelOption.WriteBufferHighWaterMark, Settings.WriteBufferHighWaterMark.Value);
            if (Settings.WriteBufferLowWaterMark.HasValue) server.Option(ChannelOption.WriteBufferLowWaterMark, Settings.WriteBufferLowWaterMark.Value);

            return server;
        }

        private async Task<IPEndPoint> ResolveNameAsync(DnsEndPoint address)
        {
            var resolved = await Dns.GetHostEntryAsync(address.Host).ConfigureAwait(false);
            //NOTE: for some reason while Helios takes first element from resolved address list
            // on the DotNetty side we need to take the last one in order to be compatible
            return new IPEndPoint(resolved.AddressList[resolved.AddressList.Length - 1], address.Port);
        }

        private async Task<IPEndPoint> ResolveNameAsync(DnsEndPoint address, AddressFamily addressFamily)
        {
            var resolved = await Dns.GetHostEntryAsync(address.Host).ConfigureAwait(false);
            var found = resolved.AddressList.LastOrDefault(a => a.AddressFamily == addressFamily);
            if (found == null)
            {
                throw new KeyNotFoundException($"Couldn't resolve IP endpoint from provided DNS name '{address}' with address family of '{addressFamily}'");
            }

            return new IPEndPoint(found, address.Port);
        }

        #endregion

        #region static methods

        public static Address MapSocketToAddress(IPEndPoint socketAddress, string schemeIdentifier, string systemName, string hostName = null, int? publicPort = null)
        {
            return socketAddress == null
                ? null
                : new Address(schemeIdentifier, systemName, SafeMapHostName(hostName) ?? SafeMapIPv6(socketAddress.Address), publicPort ?? socketAddress.Port);
        }

        private static string SafeMapHostName(string hostName)
        {
            IPAddress ip;
            return !string.IsNullOrEmpty(hostName) && IPAddress.TryParse(hostName, out ip) ? SafeMapIPv6(ip) : hostName;
        }

        private static string SafeMapIPv6(IPAddress ip) => ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();

        public static EndPoint ToEndpoint(Address address)
        {
            if (!address.Port.HasValue) throw new ArgumentNullException(nameof(address), $"Address port must not be null: {address}");

            IPAddress ip;
            return IPAddress.TryParse(address.Host, out ip)
                ? (EndPoint)new IPEndPoint(ip, address.Port.Value)
                : new DnsEndPoint(address.Host, address.Port.Value);
        }

        /// <summary>
        /// Maps an Akka.NET address to correlated <see cref="EndPoint"/>.
        /// </summary>
        /// <param name="address">Akka.NET fully qualified node address.</param>
        /// <exception cref="ArgumentException">Thrown if address port was not provided.</exception>
        /// <returns><see cref="IPEndPoint"/> for IP-based addresses, <see cref="DnsEndPoint"/> for named addresses.</returns>
        public static EndPoint AddressToSocketAddress(Address address)
        {
            if (address.Port == null) throw new ArgumentException($"address port must not be null: {address}");
            EndPoint listenAddress;
            IPAddress ip;
            if (IPAddress.TryParse(address.Host, out ip))
            {
                listenAddress = new IPEndPoint(ip, (int)address.Port);
            }
            else
            {
                // DNS resolution will be performed by the transport
                listenAddress = new DnsEndPoint(address.Host, (int)address.Port);
            }
            return listenAddress;
        }

        #endregion
    }

    internal class HeliosBackwardsCompatabilityLengthFramePrepender : LengthFieldPrepender
    {
        private readonly List<object> _temporaryOutput = new List<object>(2);

        public HeliosBackwardsCompatabilityLengthFramePrepender(int lengthFieldLength,
            bool lengthFieldIncludesLengthFieldLength) : base(ByteOrder.LittleEndian, lengthFieldLength, 0, lengthFieldIncludesLengthFieldLength)
        {
        }

        protected override void Encode(IChannelHandlerContext context, IByteBuffer message, List<object> output)
        {
            base.Encode(context, message, output);
            var lengthFrame = (IByteBuffer)_temporaryOutput[0];
            var combined = lengthFrame.WriteBytes(message);
            ReferenceCountUtil.SafeRelease(message, 1); // ready to release it - bytes have been copied
            output.Add(combined.Retain());
            _temporaryOutput.Clear();
        }
    }
}
