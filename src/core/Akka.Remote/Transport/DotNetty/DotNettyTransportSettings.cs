#region copyright
// -----------------------------------------------------------------------
//  <copyright file="DotNettyTransportSettings.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
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
    internal sealed class DotNettyTransportSettings
    {
        public static DotNettyTransportSettings Create(ActorSystem system)
        {
            return Create(system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
        }

        public static DotNettyTransportSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "DotNetty HOCON config was not found (default path: `akka.remote.dot-netty`)");

            var transportMode = config.GetString("transport-protocol", "tcp").ToLower();
            var host = config.GetString("hostname");
            if (string.IsNullOrEmpty(host)) host = IPAddress.Any.ToString();
            var publicHost = config.GetString("public-hostname", null);

            var order = ByteOrder.LittleEndian;
            var byteOrderString = config.GetString("byte-order", "little-endian").ToLowerInvariant();
            switch (byteOrderString)
            {
                case "little-endian": order = ByteOrder.LittleEndian; break;
                case "big-endian": order = ByteOrder.BigEndian; break;
                default: throw new ArgumentException($"Unknown byte-order option [{byteOrderString}]. Supported options are: big-endian, little-endian.");
            }
            
            return new DotNettyTransportSettings(
                transportMode: transportMode == "tcp" ? TransportMode.Tcp : TransportMode.Udp,
                enableSsl: config.GetBoolean("enable-ssl", false),
                connectTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                hostname: host,
                publicHostname: !string.IsNullOrEmpty(publicHost) ? publicHost : host,
                port: config.GetInt("port", 2552),
                serverSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("server-socket-worker-pool")),
                clientSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("client-socket-worker-pool")),
                maxFrameSize: ToNullableInt(config.GetByteSize("maximum-frame-size")) ?? 128000,
                ssl: config.HasPath("ssl") ? SslSettings.Create(config.GetConfig("ssl")) : SslSettings.Empty,
                dnsUseIpv6: config.GetBoolean("dns-use-ipv6", false),
                tcpReuseAddr: config.GetBoolean("tcp-reuse-addr", true),
                tcpKeepAlive: config.GetBoolean("tcp-keepalive", true),
                tcpNoDelay: config.GetBoolean("tcp-nodelay", true),
                backlog: config.GetInt("backlog", 4096),
                enforceIpFamily: RuntimeDetector.IsMono || config.GetBoolean("enforce-ip-family", false),
                receiveBufferSize: ToNullableInt(config.GetByteSize("receive-buffer-size") ?? 256000),
                sendBufferSize: ToNullableInt(config.GetByteSize("send-buffer-size") ?? 256000),
                writeBufferHighWaterMark: ToNullableInt(config.GetByteSize("write-buffer-high-water-mark")),
                writeBufferLowWaterMark: ToNullableInt(config.GetByteSize("write-buffer-low-water-mark")),
                backwardsCompatibilityModeEnabled: config.GetBoolean("enable-backwards-compatibility", false),
                logTransport: config.HasPath("log-transport") && config.GetBoolean("log-transport"),
                byteOrder: order);
        }

        private static int? ToNullableInt(long? value) => value.HasValue && value.Value > 0 ? (int?)value.Value : null;

        private static int ComputeWorkerPoolSize(Config config)
        {
            if (config == null) return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

            return ThreadPoolConfig.ScaledPoolSize(
                floor: config.GetInt("pool-size-min"),
                scalar: config.GetDouble("pool-size-factor"),
                ceiling: config.GetInt("pool-size-max"));
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

        public DotNettyTransportSettings(TransportMode transportMode, bool enableSsl, TimeSpan connectTimeout, string hostname,  string publicHostname,
            int port, int serverSocketWorkerPoolSize, int clientSocketWorkerPoolSize, int maxFrameSize, SslSettings ssl,
            bool dnsUseIpv6, bool tcpReuseAddr, bool tcpKeepAlive, bool tcpNoDelay, int backlog, bool enforceIpFamily,
            int? receiveBufferSize, int? sendBufferSize, int? writeBufferHighWaterMark, int? writeBufferLowWaterMark, bool backwardsCompatibilityModeEnabled, bool logTransport, ByteOrder byteOrder)
        {
            if (maxFrameSize < 32000) throw new ArgumentException("maximum-frame-size must be at least 32000 bytes", nameof(maxFrameSize));

            TransportMode = transportMode;
            EnableSsl = enableSsl;
            ConnectTimeout = connectTimeout;
            Hostname = hostname;
            PublicHostname = publicHostname;
            Port = port;
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
            if (config == null) throw new ArgumentNullException(nameof(config), "DotNetty SSL HOCON config was not found (default path: `akka.remote.dot-netty.Ssl`)");

            var flagsRaw = config.GetStringList("certificate.flags");
            var flags = flagsRaw.Aggregate(X509KeyStorageFlags.DefaultKeySet, (flag, str) => flag | ParseKeyStorageFlag(str));

            return new SslSettings(
                certificatePath: config.GetString("certificate.path"),
                certificatePassword: config.GetString("certificate.password"),
                flags: flags,
                suppressValidation: config.GetBoolean("suppress-validation", false));
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

        public SslSettings(string certificatePath, string certificatePassword, X509KeyStorageFlags flags, bool suppressValidation)
        {
            if (string.IsNullOrEmpty(certificatePath))
                throw new ArgumentNullException(nameof(certificatePath), "Path to SSL certificate was not found (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.path`)");

            Certificate = new X509Certificate2(certificatePath, certificatePassword, flags);
            SuppressValidation = suppressValidation;
        }
    }
}