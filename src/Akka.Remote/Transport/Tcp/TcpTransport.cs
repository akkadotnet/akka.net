using System;
using System.Configuration;
using System.Net;
using Akka.Configuration;

namespace Akka.Remote.Transport.Tcp
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

    class TcpTransportSettings
    {
        private Config _config;

        public TcpTransportSettings(Config config)
        {
            _config = config;
        }

        private static readonly TransportMode _mode = new Tcp();
        public TransportMode TransportMode
        {
            get { return _mode; }
        }

        public bool EnableSsl
        {
            get { return _config.GetBoolean("enable-ssl"); }
        }

        public TimeSpan ConnectTimeout
        {
            get { return _config.GetMillisDuration("connection-timeout"); }
        }

        public long? WriteBufferHighWaterMark
        {
            get { return OptionSize("write-buffer-high-water-mark"); }
        }

        public long? WriteBufferLowWaterMark
        {
            get { return OptionSize("write-buffer-low-water-mark"); }
        }

        public long? SendBufferSize
        {
            get { return OptionSize("send-buffer-size"); }
        }

        public long? ReceiveBufferSize
        {
            get { return OptionSize("receive-buffer-siz"); }
        }

        public int MaxFrameSize
        {
            get
            {
                var size = OptionSize("maximum-frame-size");
                if(size == null || size < 32000) throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
                return (int) size;
            }
        }

        public int Backlog
        {
            get { return _config.GetInt("backlog"); }
        }

        public bool NoDelay
        {
            get { return _config.GetBoolean("tcp-nodelay"); }
        }

        public bool KeepAlive
        {
            get { return _config.GetBoolean("tcp-keepalive"); }
        }

        public bool ReuseAddr
        {
            get { return _config.GetBoolean("tcp-reuse-addr"); }
        }

        public string Hostname
        {
            get
            {
                var configHost = _config.GetString("hostname");
                if (string.IsNullOrEmpty(configHost))
                {
                    return IPHelper.DefaultIp;
                }
                return configHost;
            }
        }

        #region Internal methods

        private long? OptionSize(string s)
        {
            var bytes = _config.GetByteSize(s);
            if (bytes == null || bytes == 0) return null;
            if(bytes < 0) throw new ConfigurationException(string.Format("Setting {0} must be 0 or positive", s));
            return bytes;
        }

        #endregion
    }
}