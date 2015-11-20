using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Groups;
using DotNetty.Transport.Channels.Sockets;

namespace Akka.Remote.Transport.DotNetty
{
    internal abstract class TransportMode
    {
    }

    internal class Tcp : TransportMode
    {
        public override string ToString()
        {
            return "tcp";
        }
    }

    internal class Udp : TransportMode
    {
        public override string ToString()
        {
            return "udp";
        }
    }

    internal class TcpTransportException : RemoteTransportException
    {
        public TcpTransportException(string message, Exception cause = null) : base(message, cause)
        {
        }
    }

    /// <summary>
    /// Configuration options for the DotNetty transport
    /// </summary>
    internal class DotNettyTransportSettings
    {
        internal readonly Config Config;

        public DotNettyTransportSettings(Config config)
        {
            Config = config;
            Init();
        }

        private void Init()
        {
            //TransportMode
            var protocolString = Config.GetString("transport-protocol");
            if (protocolString.Equals("tcp")) TransportMode = new Tcp();
            else if (protocolString.Equals("udp")) TransportMode = new Udp();
            else
                throw new ConfigurationException(string.Format("Unknown transport transport-protocol='{0}'",
                    protocolString));
            EnableSsl = Config.GetBoolean("enable-ssl");
            ConnectTimeout = Config.GetTimeSpan("connection-timeout");
            WriteBufferHighWaterMark = OptionSize("write-buffer-high-water-mark");
            WriteBufferLowWaterMark = OptionSize("write-buffer-low-water-mark");
            SendBufferSize = OptionSize("send-buffer-size");
            ReceiveBufferSize = OptionSize("receive-buffer-size");
            var size = OptionSize("maximum-frame-size");
            if (size == null || size < 32000)
                throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
            MaxFrameSize = (long)size;
            Backlog = Config.GetInt("backlog");
            TcpNoDelay = Config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = Config.GetBoolean("tcp-keepalive");
            TcpReuseAddr = Config.GetBoolean("tcp-reuse-addr");
            var configHost = Config.GetString("hostname");
            var publicConfigHost = Config.GetString("public-hostname");
            Hostname = string.IsNullOrEmpty(configHost) ? IPAddress.Any.ToString() : configHost;
            PublicHostname = string.IsNullOrEmpty(publicConfigHost) ? configHost : publicConfigHost;
            ServerSocketWorkerPoolSize = ComputeWps(Config.GetConfig("server-socket-worker-pool"));
            ClientSocketWorkerPoolSize = ComputeWps(Config.GetConfig("client-socket-worker-pool"));
            Port = Config.GetInt("port");

            //TODO: SslSettings
        }

        public TransportMode TransportMode { get; private set; }

        public bool EnableSsl { get; private set; }

        public TimeSpan ConnectTimeout { get; private set; }

        public long? WriteBufferHighWaterMark { get; private set; }

        public long? WriteBufferLowWaterMark { get; private set; }

        public long? SendBufferSize { get; private set; }

        public long? ReceiveBufferSize { get; private set; }

        public long MaxFrameSize { get; private set; }

        public int Port { get; private set; }

        public int Backlog { get; private set; }

        public bool TcpNoDelay { get; private set; }

        public bool TcpKeepAlive { get; private set; }

        public bool TcpReuseAddr { get; private set; }

        /// <summary>
        /// The hostname that this server binds to
        /// </summary>
        public string Hostname { get; private set; }

        /// <summary>
        /// If different from <see cref="Hostname"/>, this is the public "address" that is bound to the <see cref="ActorSystem"/>,
        /// whereas <see cref="Hostname"/> becomes the physical address that the low-level socket connects to.
        /// </summary>
        public string PublicHostname { get; private set; }

        public int ServerSocketWorkerPoolSize { get; private set; }

        public int ClientSocketWorkerPoolSize { get; private set; }

        #region Internal methods

        private long? OptionSize(string s)
        {
            var bytes = Config.GetByteSize(s);
            if (bytes == null || bytes == 0) return null;
            if (bytes < 0) throw new ConfigurationException(string.Format("Setting {0} must be 0 or positive", s));
            return bytes;
        }

        private int ComputeWps(Config config)
        {
            return ThreadPoolConfig.ScaledPoolSize(config.GetInt("pool-size-min"), config.GetDouble("pool-size-factor"),
                config.GetInt("pool-size-max"));
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class DotNettyTransport : Transport
    {
        #region Static Members

        /// <summary>
        /// We use 4 bytes to represent the frame length. Used by the <see cref="LengthFieldPrepender"/>
        /// and <see cref="LengthFieldBasedFrameDecoder"/>.
        /// </summary>
        public const int FrameLengthFieldLength = 4;

        public static readonly AtomicCounter UniqueIdCounter = new AtomicCounter(0);

        public static void GracefulClose(IChannel channel)
        {
            // TODO: check if this needs a configurable timeout setting
            channel.WriteAndFlushAsync(Unpooled.Empty)
                .ContinueWith(tr => channel.DisconnectAsync())
                .Unwrap()
                .ContinueWith(tc => channel.CloseAsync())
                .Unwrap().Wait();
        }

        public static Address AddressFromSocketAddress(EndPoint socketAddress, string schemeIdentifier,
            string systemName, string hostName = null, int? port = null)
        {
            var ipAddress = socketAddress as IPEndPoint;
            if (ipAddress == null) return null;
            return new Address(schemeIdentifier, systemName, string.IsNullOrEmpty(hostName) ? ipAddress.Address.ToString() : hostName,
                port ?? ipAddress.Port);
        }

        #endregion

        /// <summary>
        /// Used to coordinate shutdown across all <see cref="IChannel"/> instances used
        /// by this transport.
        /// </summary>
        public readonly IChannelGroup ChannelGroup;

        protected ILoggingAdapter Log;

        /// <summary>
        /// Logical / virtual address
        /// </summary>
        private volatile Address _localAddress;

        /// <summary>
        /// Physical / reachable socket listening address
        /// </summary>
        private volatile Address _boundTo;

        /// <summary>
        /// DotNetty server channel
        /// </summary>
        private volatile IChannel _serverChannel;

        private readonly MultithreadEventLoopGroup _serverBossGroup;
        private readonly MultithreadEventLoopGroup _serverWorkerGroup;
        private readonly MultithreadEventLoopGroup _clientWorkerGroup;

        private readonly SingleThreadEventLoop _channelGroupEventLoop = new SingleThreadEventLoop();

        private readonly TaskCompletionSource<IAssociationEventListener> _associationEventListenerPromise =
            new TaskCompletionSource<IAssociationEventListener>();

        //private readonly ObservableEventListener _eventListener = new ObservableEventListener();

        public DotNettyTransportSettings Settings { get; }

        public DotNettyTransport(ActorSystem system, Config config)
        {
            Config = config;
            System = system;
            Settings = new DotNettyTransportSettings(config);
            Log = Logging.GetLogger(System, GetType());

            _serverBossGroup = new MultithreadEventLoopGroup(1);
            _serverWorkerGroup = new MultithreadEventLoopGroup(Settings.ServerSocketWorkerPoolSize);
            _clientWorkerGroup = new MultithreadEventLoopGroup(Settings.ClientSocketWorkerPoolSize);
            //_eventListener.LogToConsole();
            //_eventListener.EnableEvents(DefaultEventSource.Log, EventLevel.Verbose);
            ChannelGroup = new DefaultChannelGroup("akka-netty-transport-driver-channelgroup-" + UniqueIdCounter.GetAndIncrement(), _channelGroupEventLoop);
        }

        public bool IsDatagram
        {
            get { return Settings.TransportMode is Udp; }
        }

        private ServerBootstrap SetupServerBootstrap(ServerBootstrap b)
        {
            var a = b
                .Group(_serverBossGroup, _serverWorkerGroup)
                .Option(ChannelOption.AutoRead, false) //initially disable reads (server)
                .ChildOption(ChannelOption.AutoRead, false) //initially disable reads (children)
                .ChildOption(ChannelOption.SoBacklog, Settings.Backlog)
                .ChildOption(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                .ChildOption(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                .ChildOption(ChannelOption.SoReuseaddr, Settings.TcpReuseAddr)
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                {
                    IChannelPipeline pipeline = channel.Pipeline;
                    BindServerPipeline(pipeline);
                }));

            if (Settings.ReceiveBufferSize.HasValue)
                a = a.ChildOption(ChannelOption.SoRcvbuf, (int)Settings.ReceiveBufferSize.Value);
            if (Settings.SendBufferSize.HasValue)
                a = a.ChildOption(ChannelOption.SoSndbuf, (int)Settings.SendBufferSize.Value);
            if (Settings.WriteBufferHighWaterMark.HasValue)
                a = a.ChildOption(ChannelOption.WriteBufferHighWaterMark, (int)Settings.WriteBufferHighWaterMark.Value);
            if (Settings.WriteBufferLowWaterMark.HasValue)
                a = a.ChildOption(ChannelOption.WriteBufferLowWaterMark, (int)Settings.WriteBufferLowWaterMark.Value);
            return a;
        }

        private ServerBootstrap InboundBootstrap()
        {
            if (IsDatagram) throw new NotImplementedException("UDP not supported");
            return SetupServerBootstrap(new ServerBootstrap().Channel<TcpServerSocketChannel>());
        }

        private Bootstrap OutboundBootstrap(Address remoteAddress)
        {
            if (IsDatagram) throw new NotImplementedException("UDP not supported");
            var b = new Bootstrap().Channel<TcpSocketChannel>()
                 .Group(_clientWorkerGroup)
                 .Option(ChannelOption.AutoRead, false) //initially disable reads
                 .Option(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                 .Option(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                 .Handler(new ActionChannelInitializer<IChannel>(channel =>
                 {
                     IChannelPipeline pipeline = channel.Pipeline;
                     BindClientPipeline(pipeline, remoteAddress);
                 }));

            if (Settings.ReceiveBufferSize.HasValue)
                b = b.Option(ChannelOption.SoRcvbuf, (int)Settings.ReceiveBufferSize.Value);
            if (Settings.SendBufferSize.HasValue)
                b = b.Option(ChannelOption.SoSndbuf, (int)Settings.SendBufferSize.Value);
            if (Settings.WriteBufferHighWaterMark.HasValue)
                b = b.Option(ChannelOption.WriteBufferHighWaterMark, (int)Settings.WriteBufferHighWaterMark.Value);
            if (Settings.WriteBufferLowWaterMark.HasValue)
                b = b.Option(ChannelOption.WriteBufferLowWaterMark, (int)Settings.WriteBufferLowWaterMark.Value);
            return b;
        }


        /// <summary>
        /// Should only be called by <see cref="BindServerPipeline"/>
        /// or <see cref="BindClientPipeline"/>.
        /// </summary>
        /// <param name="pipeline"></param>
        protected void BindDefaultPipeline(IChannelPipeline pipeline)
        {
            if (!IsDatagram)
            {
                pipeline.AddLast(new LengthFieldBasedFrameDecoder(
                    (int)MaximumPayloadBytes,
                    0,
                    FrameLengthFieldLength,
                    0,
                    FrameLengthFieldLength, // Strip the header
                    true));
                pipeline.AddLast(new LengthFieldPrepender(FrameLengthFieldLength));
            }
        }

        protected void BindServerPipeline(IChannelPipeline pipeline)
        {
            BindDefaultPipeline(pipeline);
            if (Settings.EnableSsl)
            {
                // TODO: SSL support
            }
            var handler = CreateServerHandler(this, _associationEventListenerPromise.Task);
            pipeline.AddLast("ServerHandler", handler);
        }

        protected void BindClientPipeline(IChannelPipeline pipeline, Address remoteAddress)
        {
            BindDefaultPipeline(pipeline);
            if (Settings.EnableSsl)
            {
                // TODO: SSL support
            }
            var handler = CreateClientHandler(this, remoteAddress);
            pipeline.AddLast("ClientHandler", handler);
        }

        protected IChannelHandler CreateClientHandler(DotNettyTransport transport, Address remoteAddress)
        {
            if (IsDatagram)
                throw new NotImplementedException("UDP support not enabled at this time");
            return new TcpClientHandler(transport, remoteAddress);
        }

        protected IChannelHandler CreateServerHandler(DotNettyTransport transport,
            Task<IAssociationEventListener> associationListenerFuture)
        {
            if (IsDatagram)
                throw new NotImplementedException("UDP support not enabled at this time");
            return new TcpServerHandler(transport, associationListenerFuture);
        }

        #region Akka.Remote.Transport Members

        public override string SchemeIdentifier
        {
            get
            {
                var sslPrefix = (Settings.EnableSsl ? "ssl." : "");
                return string.Format("{0}{1}", sslPrefix, Settings.TransportMode);
            }
        }

        public override long MaximumPayloadBytes
        {
            get { return Settings.MaxFrameSize; }
        }

        public override async Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            var akkaAddress = new Address("", "", Settings.Hostname, Settings.Port);
            var listenAddress = await AddressToSocketAddress(akkaAddress);
            try
            {
                var newServerChannel = await InboundBootstrap().BindAsync(listenAddress);

                newServerChannel.Configuration.AutoRead = false;
                ChannelGroup.Add(newServerChannel);
                _serverChannel = newServerChannel;

                /*
                 * ServerChannel.LocalAddress gets us the full information provided by the local OS
                 * as to which port and IP we're bound.
                 *
                 * Settings.PublicHostname allows us to specify a different virtual hostname than the one 
                 * the socket is physically bound to, in the event of using a service like Elastic IP on AWS.
                 */
                var addrByConfig = AddressFromSocketAddress(newServerChannel.LocalAddress, SchemeIdentifier, System.Name,
                    Settings.PublicHostname, Settings.Port == 0 ? null : (int?)Settings.Port);
                if (addrByConfig == null) throw new DotNettyTransportException($"Unknown local address type ${newServerChannel.LocalAddress}");
                _localAddress = addrByConfig;

                /*
                 * Need to validate that the socket was bound correctly, since config values will override
                 * whatever the underlying socket returns. This code tests to ensure that the socket was bound
                 * to a reachable IP address and port.
                 */
                var addrBySocket = AddressFromSocketAddress(newServerChannel.LocalAddress, SchemeIdentifier, System.Name);
                if (addrBySocket == null) throw new DotNettyTransportException($"Unknown local address type ${newServerChannel.LocalAddress}");
                _boundTo = addrByConfig;

                
                _associationEventListenerPromise.Task.ContinueWith(tr =>
                {
                    _serverChannel.Configuration.AutoRead = true;
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
                return Tuple.Create(_localAddress, _associationEventListenerPromise);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to bind to {0}, shutting down DotNetty transport.", listenAddress);
                try
                {
                    await Shutdown();
                }
                catch
                {
                    // ignore posible exception during shutdown
                }
                throw;
            }
        }

        /*
         * TODO: add configurable subnet filtering
         */

        /// <summary>
        /// Determines if this <see cref="DotNettyTransport"/> instance is responsible
        /// for an association with <see cref="remote"/>.
        /// </summary>
        /// <param name="remote">The remote <see cref="Address"/> on the other end of the association.</param>
        /// <returns><c>true</c> if this transport is responsible for an association at this address,<c>false</c> otherwise.
        /// Always returns <c>true</c> for <see cref="DotNettyTransport"/>.
        /// </returns>
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (!_serverChannel.Active)
                return await TaskEx.FromException<AssociationHandle>(new DotNettyTransportException("Transport is not bound."));
            var bootstrap = OutboundBootstrap(remoteAddress);

            try
            {
                var socketAddress = await AddressToSocketAddress(remoteAddress);
                var readyChannel = await bootstrap.ConnectAsync(socketAddress);
                if (Settings.EnableSsl)
                {
                    // TODO: SSL support
                }
                if (!IsDatagram)
                {
                    // TODO: mark channel as not-readable
                }

                if (IsDatagram)
                {
                    // TODO: UDP support https://github.com/akka/akka/blob/dc12f3bfd339ac1ad4407bdc1f01d6a418f5f339/akka-remote/src/main/scala/akka/remote/transport/netty/NettyTransport.scala#L461
                    throw new NotImplementedException("UDP not supported");
                }
                else
                {
                    return await readyChannel.Pipeline.Get<ClientHandler>().StatusFuture;
                }
            }
            catch (TaskCanceledException ex)
            {
                throw new DotNettyTransportException($"Connection to {remoteAddress} was cancelled");
            }
            catch (Exception ex)
            {
                throw new DotNettyTransportException(ex.Message, ex);
            }
        }

        /*
         * TODO: might need better error handling
         */

        /// <summary>
        /// Create an <see cref="IPEndPoint"/> for DotNetty from an <see cref="Address"/>.
        /// 
        /// Uses <see cref="Dns"/> internally to perform hostname --> IP resolution.
        /// </summary>
        /// <param name="address">The address of the remote system to which we need to connect.</param>
        /// <returns>A <see cref="Task{IPEndPoint}"/> that will resolve to the correct local or remote IP address.</returns>
        private Task<IPEndPoint> AddressToSocketAddress(Address address)
        {
            if (string.IsNullOrEmpty(address.Host) || address.Port == null)
                throw new ArgumentException($"Address {address} does not contain host or port information.");


            /*
             * Before we attempt DNS resolution, need to check to make sure that the specified
             * IP doesn't match any of the "special" reserved addresses which could cause
             */
            IPAddress configuredIp;
            if (IPAddress.TryParse(address.Host, out configuredIp))
            {
                if(configuredIp.Equals(IPAddress.Any) || configuredIp.Equals(IPAddress.IPv6Any))
                    return Task.FromResult(new IPEndPoint(configuredIp, address.Port.Value));
               
            }

            return Dns.GetHostEntryAsync(address.Host).ContinueWith(tr =>
            {
                var ipHostEntry = tr.Result;
                var ip = ipHostEntry.AddressList.First(x => x.AddressFamily == AddressFamily.InterNetwork);
                return new IPEndPoint(ip, address.Port.Value);
            });
        }

        public override async Task<bool> Shutdown()
        {
            Func<Task, bool> always = task => task.IsCompleted & !task.IsCanceled & !task.IsFaulted;

            var writeStatus = ChannelGroup.WriteAndFlushAsync(Unpooled.Empty).ContinueWith(always);
            var disconnectStatus = ChannelGroup.DisconnectAsync().ContinueWith(always);
            var closeStatus = ChannelGroup.CloseAsync().ContinueWith(always);
            //_eventListener.Dispose();
            return await Task.WhenAll(writeStatus, disconnectStatus, closeStatus).ContinueWith(tr =>
            {
                if (tr.IsFaulted || tr.IsCanceled) return false;
                return tr.Result.All(x => x);
            });

           

        }

        #endregion
    }
}