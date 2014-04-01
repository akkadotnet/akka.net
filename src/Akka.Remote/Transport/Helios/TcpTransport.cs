using System;
using System.Configuration;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Remote.Transport.Helios
{
    abstract class TransportMode { }

    class Tcp : TransportMode
    {
        public override string ToString()
        {
            return "tcp";
        }
    }

    class Udp : TransportMode
    {
        public override string ToString()
        {
            return "udp";
        }
    }

    class TcpTransportException : RemoteTransportException
    {
        public TcpTransportException(string message, Exception cause = null) : base(message, cause)
        {
        }
    }

    class HeliosTransportSettings
    {
        private readonly Config _config;

        public HeliosTransportSettings(Config config)
        {
            _config = config;
            Init();
        }

        private void Init()
        {
            //TransportMode
            var protocolString = _config.GetString("transport-protocol");
            if (protocolString.Equals("tcp")) TransportMode = new Tcp();
            else if (protocolString.Equals("udp")) TransportMode = new Udp();
            else throw new ConfigurationException(string.Format("Unknown transport {0}", protocolString));
            EnableSsl = _config.GetBoolean("enable-ssl");
            ConnectTimeout = _config.GetMillisDuration("connection-timeout");
            WriteBufferHighWaterMark = OptionSize("write-buffer-high-water-mark");
            WriteBufferLowWaterMark = OptionSize("write-buffer-low-water-mark");
            SendBufferSize = OptionSize("send-buffer-size");
            ReceiveBufferSize = OptionSize("receive-buffer-size");
            var size = OptionSize("maximum-frame-size");
            if(size == null || size < 32000) throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
            MaxFrameSize = (int)size;
            Backlog = _config.GetInt("backlog");
            TcpNoDelay = _config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = _config.GetBoolean("tcp-keepalive");
            TcpReuseAddr = _config.GetBoolean("tcp-reuse-addr");
            var configHost = _config.GetString("hostname");
            Hostname = string.IsNullOrEmpty(configHost) ? IPHelper.DefaultIp : configHost;
            ServerSocketWorkerPoolSize = ComputeWps(_config.GetConfig("server-socket-worker-pool"));
            ClientSocketWorkerPoolSize = ComputeWps(_config.GetConfig("client-socket-worker-pool"));
        }

        public TransportMode TransportMode { get; private set; }

        public bool EnableSsl { get; private set; }

        public TimeSpan ConnectTimeout { get; private set; }

        public long? WriteBufferHighWaterMark { get; private set; }

        public long? WriteBufferLowWaterMark { get; private set; }

        public long? SendBufferSize { get; private set; }

        public long? ReceiveBufferSize { get; private set; }

        public int MaxFrameSize { get; private set; }

        public int Backlog { get; private set; }

        public bool TcpNoDelay { get; private set; }

        public bool TcpKeepAlive { get; private set; }

        public bool TcpReuseAddr { get; private set; }

        public string Hostname { get; private set; }

        public int ServerSocketWorkerPoolSize { get; private set; }

        public int ClientSocketWorkerPoolSize { get; private set; }

        #region Internal methods

        private long? OptionSize(string s)
        {
            var bytes = _config.GetByteSize(s);
            if (bytes == null || bytes == 0) return null;
            if(bytes < 0) throw new ConfigurationException(string.Format("Setting {0} must be 0 or positive", s));
            return bytes;
        }

        private int ComputeWps(Config config)
        {
            return ThreadPoolConfig.ScaledPoolSize(config.GetInt("pool-size-min"), config.GetDouble("pool-size-factor"),
                config.GetInt("pool-size-max"));
        }

        #endregion
    }
}