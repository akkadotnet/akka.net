using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;
using Helios.Exceptions;
using Helios.Net;
using Helios.Net.Bootstrap;
using Helios.Ops;
using Helios.Ops.Executors;
using Helios.Reactor.Bootstrap;
using Helios.Serialization;
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
            else throw new ConfigurationException(string.Format("Unknown transport transport-protocol='{0}'", protocolString));
            EnableSsl = Config.GetBoolean("enable-ssl");
            ConnectTimeout = Config.GetTimeSpan("connection-timeout");
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
            var publicConfigHost = Config.GetString("public-hostname");
            Hostname = string.IsNullOrEmpty(configHost) ? IPAddress.Any.ToString() : configHost;
            PublicHostname = string.IsNullOrEmpty(publicConfigHost) ? configHost : publicConfigHost;
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
    /// Abstract base class for HeliosTransport - has separate child implementations for TCP / UDP respectively
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
                    .SetEncoder(new LengthFieldPrepender(4, false))
                    .SetDecoder(new LengthFieldFrameBasedDecoder(Settings.MaxFrameSize, 0, 4, 0, 4, true))
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
                    .SetEncoder(new LengthFieldPrepender(4, false))
                    .SetDecoder(new LengthFieldFrameBasedDecoder(Settings.MaxFrameSize, 0, 4, 0, 4, true))
                    .BufferSize(Settings.ReceiveBufferSize.HasValue
                        ? (int) Settings.ReceiveBufferSize.Value
                        : NetworkConstants.DEFAULT_BUFFER_SIZE)
                    .WorkersAreProxies(true)
                    .Build());
            }
        }

        protected IConnection NewServer(INode listenAddress)
        {
            if(InternalTransport == TransportType.Tcp)
                return new TcpServerHandler(this, AssociationListenerPromise.Task, ServerFactory.NewReactor(listenAddress).ConnectionAdapter);
            else
                throw new NotImplementedException("Haven't implemented UDP transport at this time");
        }

        protected IConnection NewClient(Address remoteAddress)
        {
            if (InternalTransport == TransportType.Tcp)
                return new TcpClientHandler(this, remoteAddress, ClientFactory.NewConnection(AddressToNode(LocalAddress), AddressToNode(remoteAddress)));
            else
                throw new NotImplementedException("Haven't implemented UDP transport at this time");
        }

        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            var listenAddress = NodeBuilder.BuildNode().Host(Settings.Hostname).WithPort(Settings.Port);
            var publicAddress = NodeBuilder.BuildNode().Host(Settings.PublicHostname).WithPort(Settings.Port);
            var newServerChannel = NewServer(listenAddress);
            newServerChannel.Open();
            publicAddress.Port = newServerChannel.Local.Port; //use the port assigned by the transport

            //Block reads until a handler actor is registered
            //TODO
            ConnectionGroup.TryAdd(newServerChannel);
            ServerChannel = newServerChannel;

            var addr = NodeToAddress(publicAddress, SchemeIdentifier, System.Name, Settings.PublicHostname);
            if(addr == null) throw new HeliosNodeException("Unknown local address type {0}", newServerChannel.Local);
            LocalAddress = addr;
            AssociationListenerPromise.Task.ContinueWith(result => ServerChannel.BeginReceive(),
                TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously);

            return Task.Run(() => Tuple.Create(addr, AssociationListenerPromise));
        }

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (!ServerChannel.IsOpen())
               throw new HeliosConnectionException(ExceptionType.NotOpen, "Transport is not open");

            return await AssociateInternal(remoteAddress);
        }

        public override Task<bool> Shutdown()
        {
            return Task.Run(() =>
            {
                foreach (var channel in ConnectionGroup)
                {
                    channel.StopReceive();
                    channel.Dispose();
                }
                return true;
            });

        }

        protected abstract Task<AssociationHandle> AssociateInternal(Address remoteAddress);

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


    /// <summary>
    /// INTERNAL API
    /// 
    /// Used for accepting inbound connections
    /// </summary>
    abstract class ServerHandler : CommonHandlers
    {
        private Task<IAssociationEventListener> _associationListenerTask;

        protected ServerHandler(HeliosTransport wrappedTransport, Task<IAssociationEventListener> associationListenerTask, IConnection underlyingConnection) : base(underlyingConnection)
        {
            WrappedTransport = wrappedTransport;
            _associationListenerTask = associationListenerTask;
        }

        protected void InitInbound(IConnection connection, INode remoteSocketAddress, NetworkData msg)
        {
            connection.StopReceive();
            _associationListenerTask.ContinueWith(r =>
            {
                var listener = r.Result;
                var remoteAddress = HeliosTransport.NodeToAddress(remoteSocketAddress, WrappedTransport.SchemeIdentifier,
                    WrappedTransport.System.Name);

                if(remoteAddress == null) throw new HeliosNodeException("Unknown inbound remote address type {0}", remoteSocketAddress);
                AssociationHandle handle;
                Init(connection, remoteSocketAddress, remoteAddress, msg, out handle);
                listener.Notify(new InboundAssociation(handle));

            }, TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.NotOnCanceled & TaskContinuationOptions.NotOnFaulted);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Used for creating outbound connections
    /// </summary>
    abstract class ClientHandler : CommonHandlers
    {
        protected readonly TaskCompletionSource<AssociationHandle> StatusPromise = new TaskCompletionSource<AssociationHandle>();
        public Task<AssociationHandle> StatusFuture { get { return StatusPromise.Task; } }

        protected Address RemoteAddress;

        protected ClientHandler(HeliosTransport heliosWrappedTransport, Address remoteAddress, IConnection underlyingConnection) : base(underlyingConnection)
        {
            WrappedTransport = heliosWrappedTransport;
            RemoteAddress = remoteAddress;
        }

        protected void InitOutbound(IConnection channel, INode remoteSocketAddress, NetworkData msg)
        {
            AssociationHandle handle;
            Init(channel, remoteSocketAddress, RemoteAddress, msg, out handle);
            StatusPromise.SetResult(handle);
        }
    }
}