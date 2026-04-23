//-----------------------------------------------------------------------
// <copyright file="DotNettyTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
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
            out AssociationHandle? op)
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
        public DotNettyTransportException(string message, Exception? cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DotNettyTransportException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected DotNettyTransportException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    internal abstract class DotNettyTransport : Transport
    {
        internal readonly ConcurrentSet<IChannel> ConnectionGroup;

        protected readonly TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise;
        protected readonly ILoggingAdapter Log;
        protected volatile Address? LocalAddress;
        protected internal volatile IChannel? ServerChannel;

        private readonly IEventLoopGroup _serverEventLoopGroup;
        private readonly IEventLoopGroup _clientEventLoopGroup;

        protected DotNettyTransport(ActorSystem system, Config config)
        {
            System = system;
            Config = config;

            // Helios compatibility
            if (system.Settings.Config.HasPath("akka.remote.helios.tcp"))
            {
                var heliosFallbackConfig = system.Settings.Config.GetConfig("akka.remote.helios.tcp")
                    .WithFallback("transport-class = \"Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote.Transport.Helios\"");
                config = heliosFallbackConfig.WithFallback(config);
            }

            var setup = system.Settings.Setup.Get<DotNettySslSetup>();
            var sslSettings = setup.HasValue ? setup.Value.Settings : null;
            Settings = DotNettyTransportSettings.Create(config, sslSettings);
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

            try
            {
                if (listenAddress is DnsEndPoint dns)
                {
                    listenAddress = await DnsToIPEndpoint(dns).ConfigureAwait(false);
                }

                return await ServerFactory().BindAsync(listenAddress).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new RemoteTransportException($"Failed to bind to [{listenAddress}]. See InnerException for details.", ex);
            }
        }

        public override async Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            // Validate SSL certificate before starting server
            // This ensures fail-fast behavior if private key is inaccessible
            if (Settings.EnableSsl)
            {
                Settings.Ssl.ValidateCertificate();
            }

            EndPoint listenAddress;
            if (IPAddress.TryParse(Settings.Hostname, out var ip))
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
                AssociationListenerPromise.Task.ContinueWith(_ => newServerChannel.Configuration.AutoRead = true,
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

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (ServerChannel == null || !ServerChannel.Open)
                throw new ChannelException("Transport is not bound or not open");

            return AssociateInternal(remoteAddress);
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
                pipeline.AddLast("BatchWriter", new FlushConsolidationHandler(Settings.BatchWriterSettings.MaxExplicitFlushes));
        }

        private void SetClientPipeline(IChannel channel, Address remoteAddress)
        {
            if (Settings.EnableSsl)
            {
                var certificate = Settings.Ssl.Certificate;
                // Use the remote address host for TLS validation, not the client's certificate name
                var host = remoteAddress.Host;

                IChannelHandler tlsHandler;

                // Compose validator: either use custom validator or build from config settings
                // This ensures a single execution path through validation logic
                var validator = Settings.Ssl.CustomValidator ?? ComposeValidatorFromSettings();

                // Create adapter bridge from our CertificateValidationCallback to RemoteCertificateValidationCallback
                // The adapter extracts remote peer information from the remote address
                RemoteCertificateValidationCallback validationCallback = (sender, cert, chain, errors) =>
                {
                    // Convert X509Certificate to X509Certificate2 if needed
#if NET10_0_OR_GREATER
                    var x509Cert = cert as X509Certificate2 ?? (cert != null ? X509CertificateLoader.LoadCertificate(cert.GetRawCertData()) : null);
#else
                    var x509Cert = cert as X509Certificate2 ?? (cert != null ? new X509Certificate2(cert) : null);
#endif
                    return validator(x509Cert, chain, remoteAddress.ToString(), errors, Log);
                };

                if (Settings.Ssl.RequireMutualAuthentication)
                {
                    // Mutual TLS requires a certificate to be configured
                    if (certificate == null)
                        throw new InvalidOperationException("Mutual TLS authentication is enabled but no certificate is configured. Please provide a certificate via DotNettySslSetup or HOCON configuration.");

                    // Provide client cert for mutual TLS
                    tlsHandler = new TlsHandler(
                        stream => new SslStream(stream, true, validationCallback,
                            (_, _, _, _, _) => certificate),
                        new ClientTlsSettings(host));
                }
                else
                {
                    // Standard TLS: Only validate server certificate, no client cert
                    tlsHandler = new TlsHandler(
                        stream => new SslStream(stream, true, validationCallback),
                        new ClientTlsSettings(host));
                }

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
                IChannelHandler tlsHandler;

                if (Settings.Ssl.RequireMutualAuthentication)
                {
                    // Mutual TLS: Require client certificate authentication
                    // Compose validator: either use custom validator or build from config settings
                    // This ensures a single execution path through validation logic
                    var validator = Settings.Ssl.CustomValidator ?? ComposeValidatorFromSettings();

                    // Create adapter bridge from our CertificateValidationCallback to RemoteCertificateValidationCallback
                    // For server-side, extract the remote peer (client address) from the channel
                    RemoteCertificateValidationCallback validationCallback = (sender, certificate, chain, errors) =>
                    {
                        // When mutual TLS is required, reject if no client certificate was provided
                        if (certificate == null)
                        {
                            Log.Warning("Mutual TLS required but client did not provide a certificate from {0}",
                                channel.RemoteAddress?.ToString() ?? "unknown");
                            return false;
                        }

                        // Extract client address from channel
                        var remoteAddress = channel.RemoteAddress?.ToString() ?? "unknown";
                        // Convert X509Certificate to X509Certificate2 if needed
#if NET10_0_OR_GREATER
                        var x509Cert = certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
#else
                        var x509Cert = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
#endif
                        return validator(x509Cert, chain, remoteAddress, errors, Log);
                    };

                    tlsHandler = new TlsHandler(
                        stream => new SslStream(
                            stream,
                            leaveInnerStreamOpen: true,
                            userCertificateValidationCallback: validationCallback),
                        new ServerTlsSettings(Settings.Ssl.Certificate, negotiateClientCertificate: true));
                }
                else
                {
                    // Standard TLS: Server authentication only (backward compatible)
                    tlsHandler = TlsHandler.Server(Settings.Ssl.Certificate);
                }

                channel.Pipeline.AddFirst("TlsHandler", tlsHandler);
            }

            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;

            if (Settings.TransportMode == TransportMode.Tcp)
            {
                var handler = new TcpServerHandler(this, Logging.GetLogger(System, typeof(TcpServerHandler)), AssociationListenerPromise.Task);
                pipeline.AddLast("ServerHandler", handler);
            }
        }

        /// <summary>
        /// Composes a certificate validation callback from the current SSL settings.
        /// This creates a validator that respects SuppressValidation
        /// and ValidateCertificateHostname configuration options.
        /// </summary>
        /// <returns>A CertificateValidationCallback composed from configuration settings.</returns>
        private CertificateValidationCallback ComposeValidatorFromSettings()
        {
            // Build validator from configuration settings
            // Note: SuppressValidation and ValidateCertificateHostname are independent settings
            var suppressChain = Settings.Ssl.SuppressValidation;
            var validateHostname = Settings.Ssl.ValidateCertificateHostname;

            return suppressChain switch
            {
                true when validateHostname => CertificateValidation.ValidateHostname(log: Log),
                true => (cert, chain, peer, errors, log) => true,
                false when validateHostname => CertificateValidation.Combine(
                    CertificateValidation.ValidateChain(log: Log), CertificateValidation.ValidateHostname(log: Log)),
                _ => CertificateValidation.ValidateChain(log: Log)
            };
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
            // on the DotNetty side we need to take the last one in order to be compatible.
            // We filter link-local addresses first, but fallback to the original list if filtering 
            // eliminates everything, preserving backward compatibility.
            var candidates = FilterLinkLocalAddresses(resolved.AddressList);
            var selected = candidates.FirstOrDefault() ?? resolved.AddressList.LastOrDefault();
            return new IPEndPoint(selected!, address.Port);
        }

        private async Task<IPEndPoint> ResolveNameAsync(DnsEndPoint address, AddressFamily addressFamily)
        {
            var resolved = await Dns.GetHostEntryAsync(address.Host).ConfigureAwait(false);
            var matching = resolved.AddressList.Where(a => a.AddressFamily == addressFamily).ToArray();
            
            // Filter out link-local addresses (169.254.0.0/16, fe80::/10) which break cluster formation 
            // on multi-NIC hosts where APIPA addresses appear in DNS results.
            // Fallback to unfiltered list to preserve backward compatibility if filtering eliminates all candidates.
            var filtered = FilterLinkLocalAddresses(matching);
            var found = filtered.FirstOrDefault() ?? matching.FirstOrDefault();
            
            if (found == null)
            {
                throw new KeyNotFoundException($"Couldn't resolve IP endpoint from provided DNS name '{address}' with address family of '{addressFamily}'");
            }

            return new IPEndPoint(found, address.Port);
        }

        /// <summary>
        /// Filters out IPv4 link-local (169.254.0.0/16) and IPv6 link-local (fe80::/10) addresses.
        /// These addresses are not routable and can break cluster formation on multi-NIC hosts 
        /// where APIPA addresses appear in DNS results.
        /// </summary>
        internal static IPAddress[] FilterLinkLocalAddresses(IPAddress[] addresses)
        {
            return addresses.Where(a => !IsIPv4LinkLocal(a) && !IsIPv6LinkLocal(a)).ToArray();
        }

        private static bool IsIPv4LinkLocal(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }

        private static bool IsIPv6LinkLocal(IPAddress ip)
        {
            return ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal;
        }

        #endregion

        #region static methods

        public static Address? MapSocketToAddress(IPEndPoint socketAddress, string schemeIdentifier, string systemName, string? hostName = null, int? publicPort = null)
        {
            return socketAddress == null
                ? null
                : new Address(schemeIdentifier, systemName, SafeMapHostName(hostName) ?? SafeMapIPv6(socketAddress.Address), publicPort ?? socketAddress.Port);
        }

        private static string? SafeMapHostName(string? hostName)
        {
            return !string.IsNullOrEmpty(hostName) && IPAddress.TryParse(hostName, out var ip) ? SafeMapIPv6(ip) : hostName;
        }

        private static string SafeMapIPv6(IPAddress ip) => ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();

        public static EndPoint ToEndpoint(Address address)
        {
            if (!address.Port.HasValue) throw new ArgumentNullException(nameof(address), $"Address port must not be null: {address}");

            return IPAddress.TryParse(address.Host, out var ip)
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
            if (IPAddress.TryParse(address.Host, out var ip))
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

    internal sealed class HeliosBackwardsCompatabilityLengthFramePrepender : LengthFieldPrepender
    {
        private readonly List<object> _temporaryOutput = new(2);

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
