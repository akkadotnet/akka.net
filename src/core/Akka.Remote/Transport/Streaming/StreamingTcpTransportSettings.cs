// //-----------------------------------------------------------------------
// // <copyright file="StreamingTcpTransportSettings.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Net;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Transport.DotNetty;

namespace Akka.Remote.Transport.Streaming
{
    public class StreamingTcpTransportSettings
    {
        public StreamingTcpTransportSettings(bool enableSsl,
            TimeSpan connectTimeout,
            string hostName,
            string publicHostName,
            int port,
            int? publicPort,
            int maxFrameSize,
            SslSettings sslSettings,
            bool dnsUseIpv6,
            bool tcpReuseAddr,
            int connectionBacklog,
            int socketReceiveBufferSize,
            int socketSendBufferSize,
            int transportReceiveBufferSize, int batchPumpInputMinBufferSize,
            int batchPumpInputMaxBufferSize, int socketStageInputMinBufferSize,int socketStageInputMaxBufferSize,
            int sendStreamQueueSize, int batchGroupMaxCount, int batchGroupMaxBytes,
            int batchGroupMaxMillis, string materializerDispatcher,
            string ioDispatcher)
        {
            EnableSsl = enableSsl;
            ConnectTimeout = connectTimeout;
            ConnectionBacklog = connectionBacklog;
            Port = port;
            Hostname = hostName;
            PublicHostname = publicHostName;
            PublicPort = publicPort;
            MaxFrameSize = maxFrameSize;
            SslSettings = sslSettings;
            DnsUseIpv6 = dnsUseIpv6;
            SocketSendBufferSize = socketSendBufferSize;
            SocketReceiveBufferSize = socketReceiveBufferSize;
            TransportReceiveBufferSize = transportReceiveBufferSize;
            BatchPumpInputMinBufferSize = batchPumpInputMinBufferSize;
            BatchPumpInputMaxBufferSize = batchPumpInputMaxBufferSize;
            SocketStageInputMinBufferSize =
                socketStageInputMinBufferSize;
            SocketStageInputMaxBufferSize =
                socketStageInputMaxBufferSize;
            SendStreamQueueSize = sendStreamQueueSize;
            BatchGroupMaxCount = batchGroupMaxCount;
            BatchGroupMaxBytes = batchGroupMaxBytes;
            BatchGroupMaxMillis = batchGroupMaxMillis;
            MaterializerDispatcher = materializerDispatcher;
            IODispatcher = ioDispatcher;
        }

        public string IODispatcher { get; }

        public TimeSpan ConnectTimeout { get; set; }

        public bool EnableSsl { get; set; }

        public SslSettings SslSettings { get; }
        public int MaxFrameSize { get; }
        public int SocketReceiveBufferSize { get; }
        public int SocketSendBufferSize { get; set; }
        public int TransportReceiveBufferSize { get; }
        public int Port { get; }
        public string Hostname { get; }
        
        public string PublicHostname { get; set; }
        public int    BatchPumpInputMinBufferSize { get; }
        public int    BatchPumpInputMaxBufferSize { get; }
        public int    SocketStageInputMinBufferSize { get; }
        public int    SocketStageInputMaxBufferSize { get; }
        public int    SendStreamQueueSize { get; }
        public int    BatchGroupMaxCount { get; }
        public int    BatchGroupMaxBytes { get; }
        public double BatchGroupMaxMillis { get; }
        public int ConnectionBacklog { get; }
        public int? PublicPort { get; }
        public bool DnsUseIpv6 { get; }
        public string MaterializerDispatcher { get; }

        public static StreamingTcpTransportSettings Create(Config config)
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

            return new StreamingTcpTransportSettings(
                enableSsl: config.GetBoolean("enable-ssl", false),
                connectTimeout: config.GetTimeSpan("connection-timeout",
                    TimeSpan.FromSeconds(15)),
                hostName: host,
                publicHostName: !string.IsNullOrEmpty(publicHost)
                    ? publicHost
                    : host,
                port: config.GetInt("port", 2552),
                publicPort: publicPort > 0 ? publicPort : (int?)null,
                maxFrameSize: (int)(config.GetByteSize("maximum-frame-size",
                    null) ?? 128000),
                sslSettings: config.HasPath("ssl")
                    ? SslSettings.Create(config.GetConfig("ssl"))
                    : SslSettings.Empty,
                dnsUseIpv6: config.GetBoolean("dns-use-ipv6", false),
                tcpReuseAddr: DotNettyTransportSettings
                    .ResolveTcpReuseAddrOption(
                        config.GetString("tcp-reuse-addr", "off-for-windows")),
                connectionBacklog: config.GetInt("backlog", 4096),
                socketReceiveBufferSize: (int)(config.GetByteSize(
                    "receive-buffer-size",
                    null) ?? 256000),
                socketSendBufferSize: (int)(config.GetByteSize(
                                                "send-buffer-size", null) ??
                                            256000),
                transportReceiveBufferSize: (int)(config.GetByteSize(
                    "transport-receive-buffer-size",
                    null) ?? 65536),
                batchPumpInputMaxBufferSize: config.GetInt("batch-pump-input-max-buf-size", 128),
                batchPumpInputMinBufferSize: config.GetInt("batch-pump-input-min-buf-size", 128),
                socketStageInputMaxBufferSize: config.GetInt("socket-stage-input-max-buf-size", 1),
                socketStageInputMinBufferSize: config.GetInt("socket-stage-input-min-buf-size", 1)
                , sendStreamQueueSize: config.GetInt("send-stream-queue-size", 64),
                batchGroupMaxCount: config.GetInt("batch-max-count", 128),
                batchGroupMaxBytes: config.GetInt("batch-max-bytes", 32*1024),
                batchGroupMaxMillis: config.GetInt("batch-max-delay-ms", 20),
                materializerDispatcher: config.GetString("materializer-dispatcher","akka.actor.default-remote-dispatcher"),
                ioDispatcher: config.GetString("io-dispatcher","akka.actor.default-remote-dispatcher")
            );
        }
    }
}