using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport.Helios;
using Akka.Util.Internal;

namespace Akka.Remote.Transport.Streaming
{
    public class NetworkStreamTransportSettings : StreamTransportSettings
    {
        public string Hostname { get; }

        public int Port { get; }

        public IPAddress BindIp { get; }

        public int BindPort { get; }

        public TimeSpan ConnectionTimeout { get; }

        public int SendBufferSize { get; }

        public int ReceiveBufferSize { get; }

        public int Backlog { get; }

        public bool TcpNoDelay { get; }

        public bool TcpKeepAlive { get; }

        public NetworkStreamTransportSettings(Config config)
            : base(config)
        {
            Hostname = config.GetString("hostname");

            if (string.IsNullOrEmpty(Hostname))
                Hostname = "localhost";

            Port = config.GetInt("port");

            string bindHostname = config.GetString("bind-hostname");

            if (string.IsNullOrEmpty(bindHostname))
                bindHostname = Hostname;

            BindIp = GetBindIp(bindHostname, dualMode: true);

            string bindPortString = config.GetString("bind-port");
            BindPort = string.IsNullOrEmpty(bindPortString) ? Port : int.Parse(bindPortString);

            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SendBufferSize = GetByteSize(config, "send-buffer-size");
            ReceiveBufferSize = GetByteSize(config, "receive-buffer-size");
            
            Backlog = config.GetInt("backlog");
            TcpNoDelay = config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = config.GetBoolean("tcp-keepalive");
        }

        internal NetworkStreamTransportSettings(HeliosTransportSettings heliosSettings)
            : base(heliosSettings)
        {
            Hostname = heliosSettings.PublicHostname;

            BindIp = GetBindIp(heliosSettings.Hostname, dualMode:false);
            Port = BindPort = heliosSettings.Port;

            ConnectionTimeout = heliosSettings.ConnectTimeout;
            SendBufferSize = (int)(heliosSettings.SendBufferSize ?? 256000);
            ReceiveBufferSize = (int)(heliosSettings.ReceiveBufferSize ?? 256000);

            Backlog = heliosSettings.Backlog;
            TcpNoDelay = heliosSettings.TcpNoDelay;
            TcpKeepAlive = heliosSettings.TcpKeepAlive;
        }

        private static IPAddress GetBindIp(string hostname, bool dualMode)
        {
            if (hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return IPAddress.Loopback;

            IPAddress ip;

            if (IPAddress.TryParse(hostname, out ip))
            {
                // The socket is using DualMode, if ipv6 is supported, listening on IPv6Any
                // will work for any address (ipv4 and ipv6)
                if (IPAddress.Any.Equals(ip) && Socket.OSSupportsIPv6 && dualMode)
                    ip = IPAddress.IPv6Any;

                return ip;
            }

            // If hostname is not localhost or an ip, bind to IPAny
            return (Socket.OSSupportsIPv6 && dualMode) ? IPAddress.IPv6Any : IPAddress.Any;
        }

        public void ConfigureSocket(Socket s)
        {
            s.SendBufferSize = SendBufferSize;
            s.ReceiveBufferSize = ReceiveBufferSize;
            s.NoDelay = TcpNoDelay;

            if (TcpKeepAlive)
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
        }

        public override bool ShutdownStreamGracefully(Stream stream, object state)
        {
            Socket socket = (Socket)state;

            try
            {
                if (socket.Connected)
                    socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                //Not fatal
                //TODO Log
                return false;
            }

            return true;
        }

        public override void CloseStream(Stream stream, object state)
        {
            CloseSocket((Socket)state);
        }

        public IPEndPoint GetBindEndPoint()
        {
            return new IPEndPoint(BindIp, BindPort);
        }

        internal void CloseSocket(Socket socket)
        {
            if (socket != null)
            {
                try
                {
                    if (socket.Connected)
                        socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception)
                {
                    //Not fatal
                }

                try
                {
                    socket.Close();
                }
                catch (Exception)
                {
                    //Not fatal
                }
            }
        }
    }

    public class NetworkStreamTransport : StreamTransport
    {
        public const string ProtocolName = "tcp";

        private readonly Socket _listener;

        public int ListeningPort { get; private set; }

        public new NetworkStreamTransportSettings Settings { get; }

        public override string SchemeIdentifier => ProtocolName;

        public NetworkStreamTransport(ActorSystem system, Config config)
            :this(system, new NetworkStreamTransportSettings(config))
        { }

        public NetworkStreamTransport(ActorSystem system, NetworkStreamTransportSettings settings)
            : base(system, settings)
        {
            Settings = settings;
            _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        protected override Address Initialize()
        {
            _listener.Bind(Settings.GetBindEndPoint());
            _listener.Listen(Settings.Backlog);

            ListeningPort = ((IPEndPoint)_listener.LocalEndPoint).Port;

            return new Address(ProtocolName, System.Name, Settings.Hostname, ListeningPort);
        }

        protected override void StartAcceptingConnections(IAssociationEventListener listener)
        {
            Task.Run(() => ListenLoop(listener));
        }

        private async Task ListenLoop(IAssociationEventListener listener)
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                Socket socket = null;
                try
                {
                    // Accept will throw when _listener is closed
                    socket = await Task<Socket>.Factory.FromAsync(_listener.BeginAccept, _listener.EndAccept, null);

                    Settings.ConfigureSocket(socket);

                    IPEndPoint remoteEndPoint = (IPEndPoint)socket.RemoteEndPoint;
                    string host = GetAddressString(remoteEndPoint.Address);

                    Settings.Log.Info("ListenLoop: Accepted new connection from '{0}:{1}'", host, remoteEndPoint.Port);

                    var remoteAddress = new Address(ProtocolName, System.Name, host, remoteEndPoint.Port);

                    var networkStream = new NetworkStream(socket, false);

                    CreateInboundAssociation(networkStream, remoteAddress, socket)
                        .ContinueWith(task =>
                        {
                            if (task.Status == TaskStatus.RanToCompletion)
                                listener.Notify(new InboundAssociation(task.Result));
                        }).IgnoreResult();
                }
                catch (SocketException ex)
                    when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                          ex.SocketErrorCode == SocketError.TimedOut)
                {
                    Settings.CloseSocket(socket);

                    if (ShutdownToken.IsCancellationRequested)
                        return;

                    // Tcp handshake failed on new connection, not fatal
                    Settings.Log.Warning("SocketException occured while accepting connection");
                }
                catch (Exception ex)
                {
                    Settings.CloseSocket(socket);

                    if (ShutdownToken.IsCancellationRequested)
                        return;

                    // Should not happen if listen on Loopback or IPAny
                    //TODO Otherwise might need to rebind if network adapter was disabled/enabled

                    Settings.Log.Warning("Unexpected ListenLoop exception\n{0}", ex);
                }
            }
        }

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            return CreateOutboundAssociation(remoteAddress);
        }

        protected override void Cleanup()
        {
            _listener.Close();
        }

        protected virtual Task<AssociationHandle> CreateInboundAssociation(Stream stream, Address remoteAddress, Socket socket)
        {
            var association = new StreamAssociationHandle(Settings, stream, InboundAddress, remoteAddress, socket);
            RegisterAssociation(association);

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return Task.FromResult((AssociationHandle)association);
        }

        protected async Task<AssociationHandle> CreateOutboundAssociation(Address remoteAddress)
        {
            if (remoteAddress.Port == null)
                throw new ArgumentException("Port must be specified.", nameof(remoteAddress));

            int port = remoteAddress.Port.Value;
            var endpoint = GetEndPoint(remoteAddress.Host, port);
            Socket socket = CreateSocket(endpoint);
            Settings.ConfigureSocket(socket);

            await Task.Factory.FromAsync(socket.BeginConnect, socket.EndConnect, remoteAddress.Host, port, null)
                .WithTimeout(Settings.ConnectionTimeout);

            NetworkStream stream = new NetworkStream(socket, false);

            int localPort = ((IPEndPoint) socket.LocalEndPoint).Port;

            var association =  await CreateOutboundAssociation(stream, InboundAddress.WithPort(localPort), remoteAddress, socket);

            return association;
        }

        protected virtual Task<AssociationHandle> CreateOutboundAssociation(Stream stream, Address localAddress, Address remoteAddress, Socket socket)
        {
            var association = new StreamAssociationHandle(Settings, stream, localAddress, remoteAddress, socket);
            RegisterAssociation(association);

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return Task.FromResult((AssociationHandle)association);
        }

        private static Socket CreateSocket(EndPoint endpoint)
        {
            if (endpoint.AddressFamily == AddressFamily.Unspecified && Socket.OSSupportsIPv6)
            {
                // DualMode works with IPv6 and IPv4
                return new Socket(SocketType.Stream, ProtocolType.Tcp);
            }

            return new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        private static EndPoint GetEndPoint(string host, int port)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return new IPEndPoint(IPAddress.Loopback, port);

            IPAddress address;
            if (IPAddress.TryParse(host, out address))
                return new IPEndPoint(address, port);

            return new DnsEndPoint(host, port);
        }

        private static string GetAddressString(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();

            if (ip.IsIPv4MappedToIPv6)
                return SafeMapToIPv4(ip).ToString();

            // IPv6 must be wrapped inside bracket in uri
            return "[" + ip.ToString() + "]";
        }

        /// <summary>
        /// IPAddress.MapToIPv4 is bugged.
        /// http://stackoverflow.com/questions/23608829/why-does-ipaddress-maptoipv4-throw-argumentoutofrangeexception
        /// </summary>
        private static IPAddress SafeMapToIPv4(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return address;

            byte[] bytes = address.GetAddressBytes();
            ushort[] numbers = new ushort[8];

            for (int i = 0; i < 8; i++)
                numbers[i] = (ushort)(bytes[i * 2] * 256 + bytes[i * 2 + 1]);

            // Cast the ushort values to a uint and mask with unsigned literal before bit shifting.
            // Otherwise, we can end up getting a negative value for any IPv4 address that ends with
            // a byte higher than 127 due to sign extension of the most significant 1 bit.
            long addressValue = ((((uint)numbers[6] & 0x0000FF00u) >> 8) | (((uint)numbers[6] & 0x000000FFu) << 8)) |
                (((((uint)numbers[7] & 0x0000FF00u) >> 8) | (((uint)numbers[7] & 0x000000FFu) << 8)) << 16);

            return new IPAddress(addressValue);
        }
    }
}
