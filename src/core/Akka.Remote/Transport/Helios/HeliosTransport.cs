//-----------------------------------------------------------------------
// <copyright file="HeliosTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;
using Helios.Channels;
using Helios.Channels.Bootstrap;
using Helios.Channels.Sockets;
using Helios.Codecs;
using Helios.Exceptions;
using Helios.Logging;
using Helios.Topology;
using Helios.Util.Concurrency;
using AtomicCounter = Helios.Util.AtomicCounter;
using LengthFieldPrepender = Helios.Codecs.LengthFieldPrepender;

namespace Akka.Remote.Transport.Helios
{
    internal abstract class TransportMode { }

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

    internal class HeliosTransportSettings
    {
        internal readonly Config Config;

        public HeliosTransportSettings(Config config)
        {
            Config = config;
            Init();
        }
        static HeliosTransportSettings()
        {
            // Disable STDOUT logging for Helios in release mode
#if !DEBUG
            LoggingFactory.DefaultFactory = new NoOpLoggerFactory();
#endif

        }

        private void Init()
        {
            //TransportMode
            var protocolString = Config.GetString("transport-protocol");
            if (protocolString.Equals("tcp")) TransportMode = new Tcp();
            else if (protocolString.Equals("udp")) TransportMode = new Udp();
            else throw new ConfigurationException(string.Format("Unknown transport transport-protocol='{0}'", protocolString));
            EnableSsl = Config.GetBoolean("enable-ssl");
            ConnectTimeout = Config.GetTimeSpan("connection-timeout");
            WriteBufferHighWaterMark = OptionSize("write-buffer-high-water-mark");
            WriteBufferLowWaterMark = OptionSize("write-buffer-low-water-mark");
            SendBufferSize = OptionSize("send-buffer-size");
            ReceiveBufferSize = OptionSize("receive-buffer-size");
            var size = OptionSize("maximum-frame-size");
            if (size == null || size < 32000) throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
            MaxFrameSize = (int)size;
            Backlog = Config.GetInt("backlog");
            TcpNoDelay = Config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = Config.GetBoolean("tcp-keepalive");
            TcpReuseAddr = Config.GetBoolean("tcp-reuse-addr");
            var configHost = Config.GetString("hostname");
            var publicConfigHost = Config.GetString("public-hostname");
            DnsUseIpv6 = Config.GetBoolean("dns-use-ipv6");
            EnforceIpFamily = RuntimeDetector.IsMono || Config.GetBoolean("enforce-ip-family");
            Hostname = string.IsNullOrEmpty(configHost) ? IPAddress.Any.ToString() : configHost;
            PublicHostname = string.IsNullOrEmpty(publicConfigHost) ? Hostname : publicConfigHost;
            ServerSocketWorkerPoolSize = ComputeWps(Config.GetConfig("server-socket-worker-pool"));
            ClientSocketWorkerPoolSize = ComputeWps(Config.GetConfig("client-socket-worker-pool"));
            Port = Config.GetInt("port");

            // used to provide backwards compatibility with Helios 1.* clients
            BackwardsCompatibilityModeEnabled = Config.GetBoolean("enable-backwards-compatibility", false);
        }

        public TransportMode TransportMode { get; private set; }

        public bool EnableSsl { get; private set; }

        public TimeSpan ConnectTimeout { get; private set; }

        public long? WriteBufferHighWaterMark { get; private set; }

        public long? WriteBufferLowWaterMark { get; private set; }

        public long? SendBufferSize { get; private set; }

        public long? ReceiveBufferSize { get; private set; }

        public int MaxFrameSize { get; private set; }

        public int Port { get; private set; }

        public int Backlog { get; private set; }

        public bool TcpNoDelay { get; private set; }

        public bool TcpKeepAlive { get; private set; }

        public bool TcpReuseAddr { get; private set; }

        public bool DnsUseIpv6 { get; private set; }

        public bool EnforceIpFamily { get; private set; }

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

        public bool BackwardsCompatibilityModeEnabled { get; private set; }

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
    /// Abstract base class for HeliosTransport - has separate child implementations for TCP / UDP respectively
    /// </summary>
    abstract class HeliosTransport : Transport
    {

        private readonly IEventLoopGroup _serverEventLoopGroup;
        private readonly IEventLoopGroup _clientEventLoopGroup;

        protected HeliosTransport(ActorSystem system, Config config)
        {
            Config = config;
            System = system;
            Settings = new HeliosTransportSettings(config);
            Log = Logging.GetLogger(System, GetType());
            _serverEventLoopGroup = new MultithreadEventLoopGroup(Settings.ServerSocketWorkerPoolSize);
            _clientEventLoopGroup = new MultithreadEventLoopGroup(Settings.ClientSocketWorkerPoolSize);
        }

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
            get
            {
                return Settings.MaxFrameSize;
            }
        }

        protected ILoggingAdapter Log;



        /// <summary>
        /// maintains a list of all established connections, so we can close them easily
        /// </summary>
        internal readonly ConcurrentSet<IChannel> ConnectionGroup = new ConcurrentSet<IChannel>();
        protected volatile Address LocalAddress;
        protected volatile IChannel ServerChannel;

        protected TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();

        public HeliosTransportSettings Settings { get; private set; }

        private TransportType InternalTransport
        {
            get
            {
                if (Settings.TransportMode is Tcp)
                    return TransportType.Tcp;
                return TransportType.Udp;
            }
        }

        private void SetInitialChannelPipeline(IChannel channel)
        {
            var pipeline = channel.Pipeline;
            if (Settings.EnableSsl)
            {
                // TODO: SSL handlers
            }
            if (InternalTransport == TransportType.Tcp)
            {
                pipeline.AddLast("FrameDecoder", new LengthFieldBasedFrameDecoder((int)MaximumPayloadBytes, 0, 4, 0, 4));
                if (Settings.BackwardsCompatibilityModeEnabled)
                {
                    pipeline.AddLast("FrameEncoder", new HeliosBackwardsCompatabilityLengthFramePrepender(4, false));
                }
                else
                {
                    pipeline.AddLast("FrameEncoder", new LengthFieldPrepender(4, false));
                }
            }
        }

        private void SetClientPipeline(IChannel channel, Address remoteAddress)
        {
            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;
            if (InternalTransport == TransportType.Tcp)
            {
                var handler = new TcpClientHandler(this, Logging.GetLogger(System, typeof(TcpClientHandler)),
                    remoteAddress);
                pipeline.AddLast("clientHandler", handler);
            }
        }

        private void SetServerPipeline(IChannel channel)
        {
            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;
            if (InternalTransport == TransportType.Tcp)
            {
                var handler = new TcpServerHandler(this, Logging.GetLogger(System, typeof(TcpServerHandler)),
                    AssociationListenerPromise.Task);
                pipeline.AddLast("serverHandler", handler);
            }
        }

        /// <summary>
        /// Internal factory used for creating new outbound connection transports
        /// </summary>
        protected ClientBootstrap ClientFactory(Address remoteAddres)
        {
            if (InternalTransport == TransportType.Tcp)
            {
                var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

                var client = new ClientBootstrap()
                    .Group(_clientEventLoopGroup)
                    .Option(ChannelOption.SoReuseaddr, Settings.TcpReuseAddr)
                    .Option(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                    .Option(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                    .Option(ChannelOption.ConnectTimeout, Settings.ConnectTimeout)
                    .Option(ChannelOption.AutoRead, false)
                    .PreferredDnsResolutionFamily(addressFamily)
                    .ChannelFactory(() => Settings.EnforceIpFamily ? 
                                        new TcpSocketChannel(addressFamily):
                                        new TcpSocketChannel())
                    .Handler(
                        new ActionChannelInitializer<TcpSocketChannel>(
                            channel => SetClientPipeline(channel, remoteAddres)));

                if (Settings.ReceiveBufferSize != null) client.Option(ChannelOption.SoRcvbuf, (int)(Settings.ReceiveBufferSize));
                if (Settings.SendBufferSize != null) client.Option(ChannelOption.SoSndbuf, (int)(Settings.SendBufferSize));
                if (Settings.WriteBufferHighWaterMark != null) client.Option(ChannelOption.WriteBufferHighWaterMark, (int)(Settings.WriteBufferHighWaterMark));
                if (Settings.WriteBufferLowWaterMark != null) client.Option(ChannelOption.WriteBufferLowWaterMark, (int)(Settings.WriteBufferLowWaterMark));

                return client;
            }

            throw new NotSupportedException("UDP is not supported");
        }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        /// <summary>
        /// Internal factory used for creating inbound connection listeners
        /// </summary>
        protected ServerBootstrap ServerFactory
        {
            get
            {
                if (InternalTransport == TransportType.Tcp)
                {
                    var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

                    var client = new ServerBootstrap()
                     .Group(_serverEventLoopGroup)
                     .Option(ChannelOption.SoReuseaddr, Settings.TcpReuseAddr)
                     .ChildOption(ChannelOption.SoKeepalive, Settings.TcpKeepAlive)
                     .ChildOption(ChannelOption.TcpNodelay, Settings.TcpNoDelay)
                     .ChildOption(ChannelOption.AutoRead, false)
                     .Option(ChannelOption.SoBacklog, Settings.Backlog)
                     .PreferredDnsResolutionFamily(addressFamily)
                     .ChannelFactory(() => Settings.EnforceIpFamily ? 
                                            new TcpServerSocketChannel(addressFamily):
                                            new TcpServerSocketChannel())
                     .ChildHandler(
                         new ActionChannelInitializer<TcpSocketChannel>(
                             SetServerPipeline));

                    if (Settings.ReceiveBufferSize != null) client.Option(ChannelOption.SoRcvbuf, (int)(Settings.ReceiveBufferSize));
                    if (Settings.SendBufferSize != null) client.Option(ChannelOption.SoSndbuf, (int)(Settings.SendBufferSize));
                    if (Settings.WriteBufferHighWaterMark != null) client.Option(ChannelOption.WriteBufferHighWaterMark, (int)(Settings.WriteBufferHighWaterMark));
                    if (Settings.WriteBufferLowWaterMark != null) client.Option(ChannelOption.WriteBufferLowWaterMark, (int)(Settings.WriteBufferLowWaterMark));

                    return client;
                }

                throw new NotSupportedException("UDP is not supported");
            }
        }

        protected async Task<IChannel> NewServer(EndPoint listenAddress)
        {
            if (InternalTransport == TransportType.Tcp)
            {
                return await ServerFactory.BindAsync(listenAddress).ConfigureAwait(false);
            }
                
            else
                throw new NotImplementedException("Haven't implemented UDP transport at this time");
        }

        public override async Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            EndPoint listenAddress;
            IPAddress ip;
            if (IPAddress.TryParse(Settings.Hostname, out ip))
            {
                listenAddress = new IPEndPoint(ip, Settings.Port);
            }
            else
            {
                listenAddress = new DnsEndPoint(Settings.Hostname, Settings.Port);
            }

            try
            {
                var newServerChannel = await NewServer(listenAddress);


                // Block reads until a handler actor is registered
                // no incoming connections will be accepted until this value is reset
                // it's possible that the first incoming association might come in though
                newServerChannel.Configuration.AutoRead = false;
                ConnectionGroup.TryAdd(newServerChannel);
                ServerChannel = newServerChannel;

                var addr = MapSocketToAddress((IPEndPoint)ServerChannel.LocalAddress, SchemeIdentifier, System.Name,
                    Settings.PublicHostname);
                if (addr == null)
                    throw new ConfigurationException($"Unknown local address type {ServerChannel.LocalAddress}");
                LocalAddress = addr;
                // resume accepting incoming connections
#pragma warning disable 4014 // we WANT this task to run without waiting
                AssociationListenerPromise.Task.ContinueWith(result => ServerChannel.Configuration.AutoRead = true,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
#pragma warning restore 4014


                return Tuple.Create(addr, AssociationListenerPromise);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to bind to {0}; shutting down Helios transport.", listenAddress);
                try
                {
                    await Shutdown();
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
            if (!ServerChannel.IsOpen)
                throw new HeliosConnectionException(ExceptionType.NotOpen, "Transport is not open");

            return await AssociateInternal(remoteAddress);
        }

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
                await all;

                var server = ServerChannel?.CloseAsync() ?? TaskEx.Completed;
                await server;

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

        protected abstract Task<AssociationHandle> AssociateInternal(Address remoteAddress);

#region Static Members

        public static EndPoint AddressToSocketAddress(Address address)
        {
            if(address.Port == null) throw new ArgumentException($"address port must not be null: {address}");
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

        public static readonly AtomicCounter UniqueIdCounter = new AtomicCounter(0);

        public static Address MapSocketToAddress(IPEndPoint socketAddr, string schemeIdentifier, string systemName, string hostName = null)
        {
            if (socketAddr == null) return null;
            return new Address(schemeIdentifier, systemName, SafeMapHostName(hostName) ?? SafeMapIPv6(socketAddr.Address), socketAddr.Port);
        }

        /// <summary>
        /// Check to see if a given hostname is IPV6, IPV4, or DNS and apply the appropriate formatting
        /// </summary>
        /// <param name="hostName">The hostname parsed from the file</param>
        /// <returns>An RFC-compliant formatting of the hostname</returns>
        public static string SafeMapHostName(string hostName)
        {
            IPAddress addr;
            if (!string.IsNullOrEmpty(hostName) && IPAddress.TryParse(hostName, out addr))
            {
                if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                    return "[" + addr + "]";
                return addr.ToString();
            }

            return hostName;
        }

        public static string SafeMapIPv6(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return "[" + address + "]";
            return address.ToString();
        }

#endregion
    }
}

