//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;
using DotNetty.Buffers;

#nullable enable
namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    ///     INTERNAL API.
    ///
    ///     Defines the settings for the <see cref="DotNettyTransport"/>.
    /// </summary>
    /// <param name="TransportMode">
    ///     Transport mode used by underlying socket channel.
    ///     Currently only TCP is supported.
    /// </param>
    /// <param name="EnableSsl">
    ///     If set to true, a Secure Socket Layer will be established
    ///     between remote endpoints. They need to share a X509 certificate
    ///     which path is specified in `akka.remote.dot-netty.tcp.ssl.certificate.path`
    /// </param>
    /// <param name="ConnectTimeout">
    ///     Sets a connection timeout for all outbound connections
    ///     i.e. how long a connect may take until it is timed out.
    /// </param>
    /// <param name="Hostname">
    ///     If this value is set, this becomes the public address for the actor system on this
    ///     transport, which might be different than the physical ip address (hostname)
    ///     this is designed to make it easy to support private / public addressing schemes
    /// </param>
    /// <param name="PublicHostname">
    ///     The hostname or IP to bind the remoting to.
    /// </param>
    /// <param name="Port">
    ///     The default remote server port clients should connect to.
    ///     Default is 2552 (AKKA), use 0 if you want a random available port
    ///     This port needs to be unique for each actor system on the same machine.
    /// </param>
    /// <param name="PublicPort">
    ///     If this value is set, this becomes the public port for the actor system on this
    ///     transport, which might be different than the physical port
    ///     this is designed to make it easy to support private / public addressing schemes
    /// </param>
    /// <param name="ServerSocketWorkerPoolSize">TBD</param>
    /// <param name="ClientSocketWorkerPoolSize">TBD</param>
    /// <param name="MaxFrameSize">TBD</param>
    /// <param name="Ssl">TBD</param>
    /// <param name="DnsUseIpv6">
    ///     If set to true, we will use IPv6 addresses upon DNS resolution for
    ///     host names. Otherwise IPv4 will be used.
    /// </param>
    /// <param name="TcpReuseAddr">
    ///     Enables SO_REUSEADDR, which determines when an ActorSystem can open
    ///     the specified listen port (the meaning differs between *nix and Windows).
    /// </param>
    /// <param name="TcpKeepAlive">
    ///     Enables TCP Keepalive, subject to the O/S kernel's configuration.
    /// </param>
    /// <param name="TcpNoDelay">
    ///     Enables the TCP_NODELAY flag, i.e. disables Nagle's algorithm
    /// </param>
    /// <param name="Backlog">
    ///     Sets the size of the connection backlog.
    /// </param>
    /// <param name="EnforceIpFamily">
    ///     If set to true, we will enforce usage of IPv4 or IPv6 addresses upon DNS
    ///     resolution for host names. If true, we will use IPv6 enforcement. Otherwise,
    ///     we will use IPv4.
    /// </param>
    /// <param name="ReceiveBufferSize">
    ///     Sets the default receive buffer size of the Sockets.
    /// </param>
    /// <param name="SendBufferSize">
    ///     Sets the default send buffer size of the Sockets.
    /// </param>
    /// <param name="WriteBufferHighWaterMark">TBD</param>
    /// <param name="WriteBufferLowWaterMark">TBD</param>
    /// <param name="BackwardsCompatibilityModeEnabled">
    ///     Enables backwards compatibility with Akka.Remote clients running Helios 1.*
    /// </param>
    /// <param name="LogTransport">
    ///     When set to true, it will enable logging of DotNetty user events
    ///     and message frames.
    /// </param>
    /// <param name="ByteOrder">
    ///     Byte order used by DotNetty, either big or little endian.
    ///     By default a little endian is used to achieve compatibility with Helios.
    /// </param>
    /// <param name="EnableBufferPooling">
    ///     Used mostly as a work-around for https://github.com/akkadotnet/akka.net/issues/3370
    ///     on .NET Core on Linux. Should always be left to <c>true</c> unless running DotNetty v0.4.6
    ///     on Linux, which can accidentally release buffers early and corrupt frames. Turn this setting
    ///     to <c>false</c> to disable pooling and work-around this issue at the cost of some performance.
    /// </param>
    /// <param name="BatchWriterSettings">
    ///     Used for performance-tuning the DotNetty channels to maximize I/O performance.
    /// </param>
    internal sealed record DotNettyTransportSettings(
        TransportMode TransportMode, 
        bool EnableSsl,
        TimeSpan ConnectTimeout,
        string Hostname, 
        string PublicHostname,
        int Port,
        int? PublicPort,
        int ServerSocketWorkerPoolSize,
        int ClientSocketWorkerPoolSize,
        int MaxFrameSize,
        SslSettings Ssl,
        bool DnsUseIpv6,
        bool TcpReuseAddr,
        bool TcpKeepAlive,
        bool TcpNoDelay,
        int Backlog,
        bool EnforceIpFamily,
        int? ReceiveBufferSize,
        int? SendBufferSize, 
        int? WriteBufferHighWaterMark,
        int? WriteBufferLowWaterMark,
        bool BackwardsCompatibilityModeEnabled,
        bool LogTransport,
        ByteOrder ByteOrder,
        bool EnableBufferPooling,
        BatchWriterSettings BatchWriterSettings)
    {
        public static DotNettyTransportSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>("akka.remote.dot-netty.tcp");
            
            var setup = system.Settings.Setup.Get<DotNettySslSetup>();
            var sslSettings = setup.HasValue ? setup.Value.Settings : null;
            return Create(config, sslSettings);
        }

        /// <summary>
        /// Adds support for the "off-for-windows" option per https://github.com/akkadotnet/akka.net/issues/3293
        /// </summary>
        /// <param name="hoconTcpReuseAddr">The HOCON string for the akka.remote.dot-netty.tcp.reuse-addr option</param>
        /// <returns><c>true</c> if we should enable REUSE_ADDR for tcp. <c>false</c> otherwise.</returns>
        private static bool ResolveTcpReuseAddrOption(string hoconTcpReuseAddr)
        {
            return hoconTcpReuseAddr.ToLowerInvariant() switch
            {
                "off-for-windows" when RuntimeDetector.IsWindows => false,
                "off-for-windows" => true,
                "on" => true,
                "off" => false,
                _ => false
            };
        }

        public static DotNettyTransportSettings Create(Config config, SslSettings? sslSettings = null)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>();

            var transportMode = config.GetString("transport-protocol", "tcp").ToLower();
            var host = config.GetString("hostname");
            if (string.IsNullOrWhiteSpace(host)) 
                host = IPAddress.Any.ToString();

            var publicHost = config.GetString("public-hostname");
            var publicPort = config.GetInt("public-port");

            var byteOrderString = config.GetString("byte-order", "little-endian").ToLowerInvariant();
            var order = byteOrderString switch
            {
                "little-endian" => ByteOrder.LittleEndian,
                "big-endian" => ByteOrder.BigEndian,
                _ => throw new ArgumentException(
                    $"Unknown byte-order option [{byteOrderString}]. Supported options are: big-endian, little-endian.")
            };

            var batchWriterSettings = new BatchWriterSettings(config.GetConfig("batching"));

            var enableSsl = config.GetBoolean("enable-ssl");

            return new DotNettyTransportSettings(
                TransportMode: transportMode == "tcp" ? TransportMode.Tcp : TransportMode.Udp,
                EnableSsl: enableSsl,
                ConnectTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                Hostname: host,
                PublicHostname: !string.IsNullOrEmpty(publicHost) ? publicHost : host,
                Port: config.GetInt("port", 2552),
                PublicPort: publicPort > 0 ? publicPort : null,
                ServerSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("server-socket-worker-pool")),
                ClientSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("client-socket-worker-pool")),
                MaxFrameSize: ToNullableInt(config.GetByteSize("maximum-frame-size", null)) ?? 128000,
                Ssl: enableSsl ? SslSettings.CreateOrDefault(config.GetConfig("ssl"), sslSettings) : SslSettings.Empty,
                DnsUseIpv6: config.GetBoolean("dns-use-ipv6"),
                TcpReuseAddr: ResolveTcpReuseAddrOption(config.GetString("tcp-reuse-addr", "off-for-windows")),
                TcpKeepAlive: config.GetBoolean("tcp-keepalive", true),
                TcpNoDelay: config.GetBoolean("tcp-nodelay", true),
                Backlog: config.GetInt("backlog", 4096),
                EnforceIpFamily: RuntimeDetector.IsMono || config.GetBoolean("enforce-ip-family"),
                ReceiveBufferSize: ToNullableInt(config.GetByteSize("receive-buffer-size", null) ?? 256000),
                SendBufferSize: ToNullableInt(config.GetByteSize("send-buffer-size", null) ?? 256000),
                WriteBufferHighWaterMark: ToNullableInt(config.GetByteSize("write-buffer-high-water-mark", null)),
                WriteBufferLowWaterMark: ToNullableInt(config.GetByteSize("write-buffer-low-water-mark", null)),
                BackwardsCompatibilityModeEnabled: config.GetBoolean("enable-backwards-compatibility"),
                LogTransport: config.HasPath("log-transport") && config.GetBoolean("log-transport"),
                ByteOrder: order,
                EnableBufferPooling: config.GetBoolean("enable-pooling", true),
                BatchWriterSettings: batchWriterSettings
            ).Validate();
        }

        private static int? ToNullableInt(long? value) => value is > 0 ? (int?)value.Value : null;

        private static int ComputeWorkerPoolSize(Config config)
        {
            if (config.IsNullOrEmpty())
                return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

            return ThreadPoolConfig.ScaledPoolSize(
                floor: config.GetInt("pool-size-min", 2),
                scalar: config.GetDouble("pool-size-factor", 1.0),
                ceiling: config.GetInt("pool-size-max", 2));
        }

        internal DotNettyTransportSettings Validate()
        {
            if (MaxFrameSize < 32000) 
                throw new ArgumentException("maximum-frame-size must be at least 32000 bytes", nameof(MaxFrameSize));

            return this;
        }
    }
    internal enum TransportMode
    {
        Tcp,
        Udp
    }

    internal sealed class SslSettings
    {
        public static readonly SslSettings Empty = new();

        public static SslSettings CreateOrDefault(Config config, SslSettings? @default = null)
        {
            try
            {
                return Create(config);
            }
            catch(Exception e)
            {
                return @default ?? throw e;
            }
        }

        private static SslSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw new ConfigurationException($"Failed to create {typeof(DotNettyTransportSettings)}: DotNetty SSL HOCON config was not found (default path: `akka.remote.dot-netty.tcp.ssl`)");

            if (config.GetBoolean("certificate.use-thumprint-over-file")
                || config.GetBoolean("certificate.use-thumbprint-over-file"))
            {
                var thumbprint = config.GetString("certificate.thumbprint") 
                                 ?? config.GetString("certificate.thumpbrint");
                if (string.IsNullOrWhiteSpace(thumbprint))
                    throw new Exception("`akka.remote.dot-netty.tcp.ssl.certificate.use-thumbprint-over-file` is set to true but `akka.remote.dot-netty.tcp.ssl.certificate.thumbprint` is null or empty");
                
                return new SslSettings(certificateThumbprint: thumbprint,
                    storeName: config.GetString("certificate.store-name"),
                    storeLocation: ParseStoreLocationName(config.GetString("certificate.store-location")),
                    suppressValidation: config.GetBoolean("suppress-validation"));
            }

            var flagsRaw = config.GetStringList("certificate.flags", new string[] { });
            var flags = flagsRaw.Aggregate(X509KeyStorageFlags.DefaultKeySet, (flag, str) => flag | ParseKeyStorageFlag(str));

            return new SslSettings(
                certificatePath: config.GetString("certificate.path"),
                certificatePassword: config.GetString("certificate.password"),
                flags: flags,
                suppressValidation: config.GetBoolean("suppress-validation"));

        }

        private static StoreLocation ParseStoreLocationName(string str)
        {
            return str switch
            {
                "local-machine" => StoreLocation.LocalMachine,
                "current-user" => StoreLocation.CurrentUser,
                _ => throw new ArgumentException(
                    $"Unrecognized flag in X509 certificate config [{str}]. Available flags: local-machine | current-user")
            };
        }

        private static X509KeyStorageFlags ParseKeyStorageFlag(string str)
        {
            return str switch
            {
                "default-key-set" => X509KeyStorageFlags.DefaultKeySet,
                "exportable" => X509KeyStorageFlags.Exportable,
                "machine-key-set" => X509KeyStorageFlags.MachineKeySet,
                "persist-key-set" => X509KeyStorageFlags.PersistKeySet,
                "user-key-set" => X509KeyStorageFlags.UserKeySet,
                "user-protected" => X509KeyStorageFlags.UserProtected,
                _ => throw new ArgumentException(
                    $"Unrecognized flag in X509 certificate config [{str}]. Available flags: default-key-set | exportable | machine-key-set | persist-key-set | user-key-set | user-protected")
            };
        }

        /// <summary>
        /// X509 certificate used to establish Secure Socket Layer (SSL) between two remote endpoints.
        /// </summary>
        public readonly X509Certificate2? Certificate;

        /// <summary>
        /// Flag used to suppress certificate validation - use true only, when on dev machine or for testing.
        /// </summary>
        public readonly bool SuppressValidation;

        private SslSettings()
        {
            Certificate = null;
            SuppressValidation = false;
        }

        public SslSettings(X509Certificate2 certificate, bool suppressValidation)
        {
            Certificate = certificate;
            SuppressValidation = suppressValidation;
        }

        private SslSettings(string certificateThumbprint, string storeName, StoreLocation storeLocation, bool suppressValidation)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var find = store.Certificates.Find(X509FindType.FindByThumbprint, certificateThumbprint, !suppressValidation);
            if (find.Count == 0)
            {
                throw new ArgumentException(
                    "Could not find Valid certificate for thumbprint (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.thumbprint`. Also check `akka.remote.dot-netty.tcp.ssl.certificate.store-name` and `akka.remote.dot-netty.tcp.ssl.certificate.store-location`)");
            }

            Certificate = find[0];
            SuppressValidation = suppressValidation;
        }

        private SslSettings(string certificatePath, string certificatePassword, X509KeyStorageFlags flags, bool suppressValidation)
        {
            if (string.IsNullOrEmpty(certificatePath))
                throw new ArgumentNullException(nameof(certificatePath), "Path to SSL certificate was not found (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.path`)");

            Certificate = new X509Certificate2(certificatePath, certificatePassword, flags);
            SuppressValidation = suppressValidation;
        }
    }
}
