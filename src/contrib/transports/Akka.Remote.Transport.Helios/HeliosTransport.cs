#region copyright
// -----------------------------------------------------------------------
//  <copyright file="HeliosTransport.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using Helios.Channels;
using Helios.Channels.Bootstrap;
using Helios.Channels.Sockets;
using Helios.Codecs;
using Helios.Exceptions;
using Helios.Util.Concurrency;
using AtomicCounter = Helios.Util.AtomicCounter;
using LengthFieldPrepender = Helios.Codecs.LengthFieldPrepender;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class TransportMode { }

    /// <summary>
    /// TBD
    /// </summary>
    internal class Tcp : TransportMode
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "tcp";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class Udp : TransportMode
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "udp";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class TcpTransportException : RemoteTransportException
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="cause">TBD</param>
        public TcpTransportException(string message, Exception cause = null) : base(message, cause)
        {
        }
    }

    /// <summary>
    /// Abstract base class for HeliosTransport - has separate child implementations for TCP / UDP respectively
    /// </summary>
    abstract class HeliosTransport : Transport
    {
        internal static Config DefaultConfig()
        {
            var config = ConfigurationFactory.FromResource<HeliosTransport>("Akka.Remote.Transport.Helios.remote.conf");
            return config;
        }

        private readonly IEventLoopGroup _serverEventLoopGroup;
        private readonly IEventLoopGroup _clientEventLoopGroup;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="config">TBD</param>
        /// <param name="fallbackConfigPath">TBD</param>
        protected HeliosTransport(ActorSystem system, Config config, string fallbackConfigPath)
        {
            Config = config;
            System = system;
            var fallbackConfig = DefaultConfig();
            Settings = new HeliosTransportSettings(config.WithFallback(fallbackConfig.GetConfig(fallbackConfigPath)));
            Log = Logging.GetLogger(System, GetType());
            _serverEventLoopGroup = new MultithreadEventLoopGroup(Settings.ServerSocketWorkerPoolSize);
            _clientEventLoopGroup = new MultithreadEventLoopGroup(Settings.ClientSocketWorkerPoolSize);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override string SchemeIdentifier
        {
            get
            {
                var sslPrefix = (Settings.EnableSsl ? "ssl." : "");
                return string.Format("{0}{1}", sslPrefix, Settings.TransportMode);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override long MaximumPayloadBytes
        {
            get
            {
                return Settings.MaxFrameSize;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log;



        /// <summary>
        /// maintains a list of all established connections, so we can close them easily
        /// </summary>
        internal readonly ConcurrentSet<IChannel> ConnectionGroup = new ConcurrentSet<IChannel>();
        /// <summary>
        /// TBD
        /// </summary>
        protected volatile Address LocalAddress;
        /// <summary>
        /// TBD
        /// </summary>
        protected volatile IChannel ServerChannel;

        /// <summary>
        /// TBD
        /// </summary>
        protected TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();

        /// <summary>
        /// TBD
        /// </summary>
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
        /// <param name="remoteAddres">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
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
                                        new TcpSocketChannel(addressFamily) :
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remote">TBD</param>
        /// <returns>TBD</returns>
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        /// <summary>
        /// Internal factory used for creating inbound connection listeners
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
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
                                            new TcpServerSocketChannel(addressFamily) :
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listenAddress">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        protected async Task<IChannel> NewServer(EndPoint listenAddress)
        {
            if (InternalTransport == TransportType.Tcp)
            {
                return await ServerFactory.BindAsync(listenAddress).ConfigureAwait(false);
            }

            else
                throw new NotImplementedException("Haven't implemented UDP transport at this time");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ConfigurationException">TBD</exception>
        /// <exception cref="Exception">TBD</exception>
        /// <returns>TBD</returns>
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
                var newServerChannel = await NewServer(listenAddress).ConfigureAwait(false);


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
                    await Shutdown().ConfigureAwait(false);
                }
                catch
                {
                    // ignore errors occurring during shutdown
                }
                throw;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (!ServerChannel.IsOpen)
                throw new HeliosConnectionException(ExceptionType.NotOpen, "Transport is not open");

            return AssociateInternal(remoteAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        protected abstract Task<AssociationHandle> AssociateInternal(Address remoteAddress);

        #region Static Members

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly AtomicCounter UniqueIdCounter = new AtomicCounter(0);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="socketAddr">TBD</param>
        /// <param name="schemeIdentifier">TBD</param>
        /// <param name="systemName">TBD</param>
        /// <param name="hostName">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <returns>TBD</returns>
        public static string SafeMapIPv6(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return "[" + address + "]";
            return address.ToString();
        }

        #endregion
    }
}