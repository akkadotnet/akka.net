//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;
using DotNetty.Buffers;

namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Defines the settings for the <see cref="DotNettyTransport"/>.
    /// </summary>
    internal sealed class DotNettyTransportSettings
    {
        public static DotNettyTransportSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>("akka.remote.dot-netty.tcp");
            return Create(config);
        }

        /// <summary>
        /// Adds support for the "off-for-windows" option per https://github.com/akkadotnet/akka.net/issues/3293
        /// </summary>
        /// <param name="hoconTcpReuseAddr">The HOCON string for the akka.remote.dot-netty.tcp.reuse-addr option</param>
        /// <returns><c>true</c> if we should enable REUSE_ADDR for tcp. <c>false</c> otherwise.</returns>
        internal static bool ResolveTcpReuseAddrOption(string hoconTcpReuseAddr)
        {
            switch (hoconTcpReuseAddr.ToLowerInvariant())
            {
                case "off-for-windows" when RuntimeDetector.IsWindows:
                    return false;
                case "off-for-windows":
                    return true;
                case "on":
                    return true;
                case "off":
                default:
                    return false;
            }
        }

        public static DotNettyTransportSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>();

            var transportMode = config.GetString("transport-protocol", "tcp").ToLower();
            var host = config.GetString("hostname", null);
            if (string.IsNullOrEmpty(host)) host = IPAddress.Any.ToString();
            var publicHost = config.GetString("public-hostname", null);
            var publicPort = config.GetInt("public-port", 0);

            var order = ByteOrder.LittleEndian;
            var byteOrderString = config.GetString("byte-order", "little-endian").ToLowerInvariant();
            switch (byteOrderString)
            {
                case "little-endian": order = ByteOrder.LittleEndian; break;
                case "big-endian": order = ByteOrder.BigEndian; break;
                default: throw new ArgumentException($"Unknown byte-order option [{byteOrderString}]. Supported options are: big-endian, little-endian.");
            }

            var batchWriterSettings = new BatchWriterSettings(config.GetConfig("batching"));

            return new DotNettyTransportSettings(
                transportMode: transportMode == "tcp" ? TransportMode.Tcp : TransportMode.Udp,
                enableSsl: config.GetBoolean("enable-ssl", false),
                connectTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                hostname: host,
                publicHostname: !string.IsNullOrEmpty(publicHost) ? publicHost : host,
                port: config.GetInt("port", 2552),
                publicPort: publicPort > 0 ? publicPort : (int?)null,
                serverSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("server-socket-worker-pool")),
                clientSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("client-socket-worker-pool")),
                maxFrameSize: ToNullableInt(config.GetByteSize("maximum-frame-size", null)) ?? 128000,
                ssl: config.HasPath("ssl") ? SslSettings.Create(config.GetConfig("ssl")) : SslSettings.Empty,
                dnsUseIpv6: config.GetBoolean("dns-use-ipv6", false),
                tcpReuseAddr: ResolveTcpReuseAddrOption(config.GetString("tcp-reuse-addr", "off-for-windows")),
                tcpKeepAlive: config.GetBoolean("tcp-keepalive", true),
                tcpNoDelay: config.GetBoolean("tcp-nodelay", true),
                backlog: config.GetInt("backlog", 4096),
                enforceIpFamily: RuntimeDetector.IsMono || config.GetBoolean("enforce-ip-family", false),
                receiveBufferSize: ToNullableInt(config.GetByteSize("receive-buffer-size", null) ?? 256000),
                sendBufferSize: ToNullableInt(config.GetByteSize("send-buffer-size", null) ?? 256000),
                writeBufferHighWaterMark: ToNullableInt(config.GetByteSize("write-buffer-high-water-mark", null)),
                writeBufferLowWaterMark: ToNullableInt(config.GetByteSize("write-buffer-low-water-mark", null)),
                backwardsCompatibilityModeEnabled: config.GetBoolean("enable-backwards-compatibility", false),
                logTransport: config.HasPath("log-transport") && config.GetBoolean("log-transport", false),
                byteOrder: order,
                enableBufferPooling: config.GetBoolean("enable-pooling", true),
                batchWriterSettings: batchWriterSettings);
        }

        private static int? ToNullableInt(long? value) => value.HasValue && value.Value > 0 ? (int?)value.Value : null;

        private static int ComputeWorkerPoolSize(Config config)
        {
            if (config.IsNullOrEmpty())
                return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

            return ThreadPoolConfig.ScaledPoolSize(
                floor: config.GetInt("pool-size-min", 0),
                scalar: config.GetDouble("pool-size-factor", 0),
                ceiling: config.GetInt("pool-size-max", 0));
        }

        /// <summary>
        /// Transport mode used by underlying socket channel.
        /// Currently only TCP is supported.
        /// </summary>
        public readonly TransportMode TransportMode;

        /// <summary>
        /// If set to true, a Secure Socket Layer will be established
        /// between remote endpoints. They need to share a X509 certificate
        /// which path is specified in `akka.remote.dot-netty.tcp.ssl.certificate.path`
        /// </summary>
        public readonly bool EnableSsl;

        /// <summary>
        /// Sets a connection timeout for all outbound connections
        /// i.e. how long a connect may take until it is timed out.
        /// </summary>
        public readonly TimeSpan ConnectTimeout;

        /// <summary>
        /// The hostname or IP to bind the remoting to.
        /// </summary>
        public readonly string Hostname;

        /// <summary>
        /// If this value is set, this becomes the public address for the actor system on this
        /// transport, which might be different than the physical ip address (hostname)
        /// this is designed to make it easy to support private / public addressing schemes
        /// </summary>
        public readonly string PublicHostname;

        /// <summary>
        /// The default remote server port clients should connect to.
        /// Default is 2552 (AKKA), use 0 if you want a random available port
        /// This port needs to be unique for each actor system on the same machine.
        /// </summary>
        public readonly int Port;

        /// <summary>
        /// If this value is set, this becomes the public port for the actor system on this
        /// transport, which might be different than the physical port
        /// this is designed to make it easy to support private / public addressing schemes
        /// </summary>
        public readonly int? PublicPort;

        public readonly int ServerSocketWorkerPoolSize;
        public readonly int ClientSocketWorkerPoolSize;
        public readonly int MaxFrameSize;
        public readonly SslSettings Ssl;

        /// <summary>
        /// If set to true, we will use IPv6 addresses upon DNS resolution for
        /// host names. Otherwise IPv4 will be used.
        /// </summary>
        public readonly bool DnsUseIpv6;

        /// <summary>
        /// Enables SO_REUSEADDR, which determines when an ActorSystem can open
        /// the specified listen port (the meaning differs between *nix and Windows).
        /// </summary>
        public readonly bool TcpReuseAddr;

        /// <summary>
        /// Enables TCP Keepalive, subject to the O/S kernel's configuration.
        /// </summary>
        public readonly bool TcpKeepAlive;

        /// <summary>
        /// Enables the TCP_NODELAY flag, i.e. disables Nagle's algorithm
        /// </summary>
        public readonly bool TcpNoDelay;

        /// <summary>
        /// If set to true, we will enforce usage of IPv4 or IPv6 addresses upon DNS
        /// resolution for host names. If true, we will use IPv6 enforcement. Otherwise,
        /// we will use IPv4.
        /// </summary>
        public readonly bool EnforceIpFamily;

        /// <summary>
        /// Sets the size of the connection backlog.
        /// </summary>
        public readonly int Backlog;

        /// <summary>
        /// Sets the default receive buffer size of the Sockets.
        /// </summary>
        public readonly int? ReceiveBufferSize;

        /// <summary>
        /// Sets the default send buffer size of the Sockets.
        /// </summary>
        public readonly int? SendBufferSize;
        public readonly int? WriteBufferHighWaterMark;
        public readonly int? WriteBufferLowWaterMark;

        /// <summary>
        /// Enables backwards compatibility with Akka.Remote clients running Helios 1.*
        /// </summary>
        public readonly bool BackwardsCompatibilityModeEnabled;

        /// <summary>
        /// When set to true, it will enable logging of DotNetty user events
        /// and message frames.
        /// </summary>
        public readonly bool LogTransport;

        /// <summary>
        /// Byte order used by DotNetty, either big or little endian.
        /// By default a little endian is used to achieve compatibility with Helios.
        /// </summary>
        public readonly ByteOrder ByteOrder;

        /// <summary>
        /// Used mostly as a work-around for https://github.com/akkadotnet/akka.net/issues/3370
        /// on .NET Core on Linux. Should always be left to <c>true</c> unless running DotNetty v0.4.6
        /// on Linux, which can accidentally release buffers early and corrupt frames. Turn this setting
        /// to <c>false</c> to disable pooling and work-around this issue at the cost of some performance.
        /// </summary>
        public readonly bool EnableBufferPooling;

        /// <summary>
        /// Used for performance-tuning the DotNetty channels to maximize I/O performance.
        /// </summary>
        public readonly BatchWriterSettings BatchWriterSettings;

        public DotNettyTransportSettings(TransportMode transportMode, bool enableSsl, TimeSpan connectTimeout, string hostname, string publicHostname,
            int port, int? publicPort, int serverSocketWorkerPoolSize, int clientSocketWorkerPoolSize, int maxFrameSize, SslSettings ssl,
            bool dnsUseIpv6, bool tcpReuseAddr, bool tcpKeepAlive, bool tcpNoDelay, int backlog, bool enforceIpFamily,
            int? receiveBufferSize, int? sendBufferSize, int? writeBufferHighWaterMark, int? writeBufferLowWaterMark, bool backwardsCompatibilityModeEnabled, bool logTransport, ByteOrder byteOrder,
            bool enableBufferPooling, BatchWriterSettings batchWriterSettings)
        {
            if (maxFrameSize < 32000) throw new ArgumentException("maximum-frame-size must be at least 32000 bytes", nameof(maxFrameSize));

            TransportMode = transportMode;
            EnableSsl = enableSsl;
            ConnectTimeout = connectTimeout;
            Hostname = hostname;
            PublicHostname = publicHostname;
            Port = port;
            PublicPort = publicPort;
            ServerSocketWorkerPoolSize = serverSocketWorkerPoolSize;
            ClientSocketWorkerPoolSize = clientSocketWorkerPoolSize;
            MaxFrameSize = maxFrameSize;
            Ssl = ssl;
            DnsUseIpv6 = dnsUseIpv6;
            TcpReuseAddr = tcpReuseAddr;
            TcpKeepAlive = tcpKeepAlive;
            TcpNoDelay = tcpNoDelay;
            Backlog = backlog;
            EnforceIpFamily = enforceIpFamily;
            ReceiveBufferSize = receiveBufferSize;
            SendBufferSize = sendBufferSize;
            WriteBufferHighWaterMark = writeBufferHighWaterMark;
            WriteBufferLowWaterMark = writeBufferLowWaterMark;
            BackwardsCompatibilityModeEnabled = backwardsCompatibilityModeEnabled;
            LogTransport = logTransport;
            ByteOrder = byteOrder;
            EnableBufferPooling = enableBufferPooling;
            BatchWriterSettings = batchWriterSettings;
        }
    }
    internal enum TransportMode
    {
        Tcp,
        Udp
    }

    internal sealed class SslSettings
    {
        public static readonly SslSettings Empty = new SslSettings();
        public static SslSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw new ConfigurationException($"Failed to create {typeof(DotNettyTransportSettings)}: DotNetty SSL HOCON config was not found (default path: `akka.remote.dot-netty.Ssl`)");

            if (config.GetBoolean("certificate.use-thumprint-over-file", false))
            {
                return new SslSettings(config.GetString("certificate.thumbprint", null),
                    config.GetString("certificate.store-name", null),
                    ParseStoreLocationName(config.GetString("certificate.store-location", null)),
                        config.GetBoolean("suppress-validation", false));

            }
            else
            {
                var flagsRaw = config.GetStringList("certificate.flags", new string[] { });
                var flags = flagsRaw.Aggregate(X509KeyStorageFlags.DefaultKeySet, (flag, str) => flag | ParseKeyStorageFlag(str));

                return new SslSettings(
                    certificatePath: config.GetString("certificate.path", null),
                    certificatePassword: config.GetString("certificate.password", null),
                    flags: flags,
                    suppressValidation: config.GetBoolean("suppress-validation", false));
            }

        }

        private static StoreLocation ParseStoreLocationName(string str)
        {
            switch (str)
            {
                case "local-machine": return StoreLocation.LocalMachine;
                case "current-user": return StoreLocation.CurrentUser;
                default: throw new ArgumentException($"Unrecognized flag in X509 certificate config [{str}]. Available flags: local-machine | current-user");
            }
        }

        private static X509KeyStorageFlags ParseKeyStorageFlag(string str)
        {
            switch (str)
            {
                case "default-key-set": return X509KeyStorageFlags.DefaultKeySet;
                case "exportable": return X509KeyStorageFlags.Exportable;
                case "machine-key-set": return X509KeyStorageFlags.MachineKeySet;
                case "persist-key-set": return X509KeyStorageFlags.PersistKeySet;
                case "user-key-set": return X509KeyStorageFlags.UserKeySet;
                case "user-protected": return X509KeyStorageFlags.UserProtected;
                default: throw new ArgumentException($"Unrecognized flag in X509 certificate config [{str}]. Available flags: default-key-set | exportable | machine-key-set | persist-key-set | user-key-set | user-protected");
            }
        }

        /// <summary>
        /// X509 certificate used to establish Secure Socket Layer (SSL) between two remote endpoints.
        /// </summary>
        public readonly X509Certificate2 Certificate;

        /// <summary>
        /// Flag used to suppress certificate validation - use true only, when on dev machine or for testing.
        /// </summary>
        public readonly bool SuppressValidation;

        public SslSettings()
        {
            Certificate = null;
            SuppressValidation = false;
        }

        public SslSettings(string certificateThumbprint, string storeName, StoreLocation storeLocation, bool suppressValidation)
        {
            using (var store = new X509Store(storeName, storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);

                var find = store.Certificates.Find(X509FindType.FindByThumbprint, certificateThumbprint, !suppressValidation);
                if (find.Count == 0)
                {
                    throw new ArgumentException(
                        "Could not find Valid certificate for thumbprint (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.thumpbrint`. Also check akka.remote.dot-netty.tcp.ssl.certificate.store-name and akka.remote.dot-netty.tcp.ssl.certificate.store-location)");
                }

                Certificate = find[0];
                SuppressValidation = suppressValidation;
            }
        }

        public SslSettings(string certificatePath, string certificatePassword, X509KeyStorageFlags flags, bool suppressValidation)
        {
            if (string.IsNullOrEmpty(certificatePath))
                throw new ArgumentNullException(nameof(certificatePath), "Path to SSL certificate was not found (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.path`)");

            Certificate = new X509Certificate2(certificatePath, certificatePassword, flags);
            SuppressValidation = suppressValidation;
        }
    }
}
