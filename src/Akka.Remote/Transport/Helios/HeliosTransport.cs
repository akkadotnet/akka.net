using System;
using System.Configuration;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Tools;
using Helios.Net;
using Helios.Net.Bootstrap;
using Helios.Ops;
using Helios.Ops.Executors;
using Helios.Reactor.Bootstrap;
using Helios.Topology;
using AtomicCounter = Helios.Util.AtomicCounter;

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
        internal readonly Config Config;

        public HeliosTransportSettings(Config config)
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
            else throw new ConfigurationException(string.Format("Unknown transport {0}", protocolString));
            EnableSsl = Config.GetBoolean("enable-ssl");
            ConnectTimeout = Config.GetMillisDuration("connection-timeout");
            WriteBufferHighWaterMark = OptionSize("write-buffer-high-water-mark");
            WriteBufferLowWaterMark = OptionSize("write-buffer-low-water-mark");
            SendBufferSize = OptionSize("send-buffer-size");
            ReceiveBufferSize = OptionSize("receive-buffer-size");
            var size = OptionSize("maximum-frame-size");
            if(size == null || size < 32000) throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
            MaxFrameSize = (int)size;
            Backlog = Config.GetInt("backlog");
            TcpNoDelay = Config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = Config.GetBoolean("tcp-keepalive");
            TcpReuseAddr = Config.GetBoolean("tcp-reuse-addr");
            var configHost = Config.GetString("hostname");
            Hostname = string.IsNullOrEmpty(configHost) ? SystemAddressHelper.ConnectedIp.ToString() : configHost;
            ServerSocketWorkerPoolSize = ComputeWps(Config.GetConfig("server-socket-worker-pool"));
            ClientSocketWorkerPoolSize = ComputeWps(Config.GetConfig("client-socket-worker-pool"));
            Port = Config.GetInt("port");
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

        public string Hostname { get; private set; }

        public int ServerSocketWorkerPoolSize { get; private set; }

        public int ClientSocketWorkerPoolSize { get; private set; }

        #region Internal methods

        private long? OptionSize(string s)
        {
            var bytes = Config.GetByteSize(s);
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

    /// <summary>
    /// Abstract base class for HeliosTransport - has separate child implementations for TCP / UDP respsectively
    /// </summary>
    abstract class HeliosTransport : Transport
    {
        protected HeliosTransport(ActorSystem system, Config config)
        {
            Config = config;
            System = system;
            Settings = new HeliosTransportSettings(config);
            Log = Logging.GetLogger(System, GetType());
            Executor = new TryCatchExecutor(exception => Log.Error(exception, "Unknown network error"));
        }

        public override string SchemeIdentifier
        {
            get
            {
                var sslPrefix = (Settings.EnableSsl ? "ssl." : "");
                return string.Format("{0}{1}", sslPrefix, Settings.TransportMode);
            }
        }

        protected LoggingAdapter Log;

        /// <summary>
        /// maintains a list of all established connections, so we can close them easily
        /// </summary>
        internal readonly ConcurrentSet<IConnection> ConnectionGroup = new ConcurrentSet<IConnection>();
        protected volatile Address LocalAddress;
        protected volatile IConnection ServerChannel;

        protected TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();

        public HeliosTransportSettings Settings { get; private set; }

        /// <summary>
        /// the internal executor used
        /// </summary>
        protected readonly IExecutor Executor;

        private TransportType InternalTransport
        {
            get
            {
                if (Settings.TransportMode is Tcp)
                    return TransportType.Tcp;
                return TransportType.Udp;
            }
        }

        private IConnectionFactory _clientFactory;

        /// <summary>
        /// Internal factory used for creating new outbound connection transports
        /// </summary>
        protected IConnectionFactory ClientFactory
        {
            get
            {
                return _clientFactory ?? (_clientFactory = new ClientBootstrap()
                    .Executor(Executor)
                    .SetTransport(InternalTransport)
                    .WorkerThreads(Settings.ClientSocketWorkerPoolSize)
                    .SetOption("receiveBufferSize", Settings.ReceiveBufferSize)
                    .SetOption("sendBufferSize", Settings.SendBufferSize)
                    .SetOption("reuseAddress", Settings.TcpReuseAddr)
                    .SetOption("keepAlive", Settings.TcpKeepAlive)
                    .SetOption("tcpNoDelay", Settings.TcpNoDelay)
                    .SetOption("connectTimeout", Settings.ConnectTimeout)
                    .SetOption("backlog", Settings.Backlog)
                    .Build());
            }
        }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        private IServerFactory _serverFactory;
        /// <summary>
        /// Internal factory used for creating inbound connection listeners
        /// </summary>
        protected IServerFactory ServerFactory
        {
            get
            {
                return _serverFactory ?? (_serverFactory = new ServerBootstrap()
                    .Executor(Executor)
                    .SetTransport(InternalTransport)
                    .WorkerThreads(Settings.ClientSocketWorkerPoolSize)
                    .SetOption("receiveBufferSize", Settings.ReceiveBufferSize)
                    .SetOption("sendBufferSize", Settings.SendBufferSize)
                    .SetOption("reuseAddress", Settings.TcpReuseAddr)
                    .SetOption("keepAlive", Settings.TcpKeepAlive)
                    .SetOption("tcpNoDelay", Settings.TcpNoDelay)
                    .SetOption("connectTimeout", Settings.ConnectTimeout)
                    .SetOption("backlog", Settings.Backlog)
                    .BufferSize(Settings.ReceiveBufferSize.HasValue
                        ? (int) Settings.ReceiveBufferSize.Value
                        : NetworkConstants.DEFAULT_BUFFER_SIZE)
                    .WorkersAreProxies(true)
                    .Build());
            }
        }

        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            var bindingAddress = NodeBuilder.BuildNode().Host(Settings.Hostname).WithPort(Settings.Port);

            var newChannel = ServerFactory.NewConnection(bindingAddress, Node.Empty());
        }

        #region Static Members

        public static INode AddressToNode(Address address)
        {
            if (address == null || string.IsNullOrEmpty(address.Host) || !address.Port.HasValue) throw new ArgumentException("Must have valid host and port information", "address");
            return NodeBuilder.BuildNode().Host(address.Host).WithPort(address.Port.Value);
        }

        public static readonly AtomicCounter UniqueIdCounter = new AtomicCounter(0);

        public static Address NodeToAddress(INode node, string schemeIdentifier, string systemName, string hostName = null)
        {
            if (node == null) return null;
            return new Address(schemeIdentifier, systemName, hostName ?? node.Host.ToString(), node.Port);
        }

        #endregion
    }
}