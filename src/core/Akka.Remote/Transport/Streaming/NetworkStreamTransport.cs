using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

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

            Hostname = config.GetString("hostname");
            Port = config.GetInt("port");

            string bindHostname = config.GetString("bind-hostname");

            if (string.IsNullOrEmpty(bindHostname))
                bindHostname = Hostname;

            IPAddress ip;
            if (Hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                BindIp = IPAddress.Loopback;
            }
            else if (IPAddress.TryParse(bindHostname, out ip))
            {
                // The socket is using DualMode, if ipv6 is supported, listening on IPv6Any
                // will work for any address (ipv4 and ipv6)
                if (IPAddress.Any.Equals(ip) && Socket.OSSupportsIPv6)
                    ip = IPAddress.IPv6Any;

                BindIp = ip;
            }
            else
            {
                // If hostname is not localhost or an ip, bind to IPAny
                BindIp = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
            }

            string bindPortString = config.GetString("bind-port");

            BindPort = string.IsNullOrEmpty(bindPortString) ? Port : int.Parse(bindPortString);

            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SendBufferSize = GetByteSize(config, "send-buffer-size");
            ReceiveBufferSize = GetByteSize(config, "receive-buffer-size");
            
            Backlog = config.GetInt("backlog");
            TcpNoDelay = config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = config.GetBoolean("tcp-keepalive");
        }

        public void ConfigureSocket(Socket s)
        {
            s.SendBufferSize = SendBufferSize;
            s.ReceiveBufferSize = ReceiveBufferSize;
            s.NoDelay = TcpNoDelay;

            if (TcpKeepAlive)
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
        }

        public IPEndPoint GetBindEndPoint()
        {
            return new IPEndPoint(BindIp, BindPort);
        }
    }

    public class NetworkStreamTransport : StreamTransport
    {
        public const string ProtocolName = "tcp";

        private readonly Socket _listener;

        protected new NetworkStreamTransportSettings Settings { get; }

        public override string SchemeIdentifier => ProtocolName;

        public NetworkStreamTransport(ActorSystem system, Config config)
            :this(system, new NetworkStreamTransportSettings(config))
        { }

        public NetworkStreamTransport(ActorSystem system, NetworkStreamTransportSettings settings)
            : base(system, settings)
        {
            Settings = settings;
            _listener = CreateDualModeTcpSocket();
        }

        protected override Address Initialize()
        {
            _listener.Bind(Settings.GetBindEndPoint());
            _listener.Listen(Settings.Backlog);

            return new Address(ProtocolName, System.Name, Settings.Hostname, ((IPEndPoint)_listener.LocalEndPoint).Port);
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

                    var remoteAddress = new Address(ProtocolName, System.Name, host, remoteEndPoint.Port);

                    var networkStream = new NetworkStream(socket, true);

#pragma warning disable 4014
                    CreateInboundAssociation(networkStream, remoteAddress)
                        .ContinueWith(task =>
                        {
                            if (task.Status == TaskStatus.RanToCompletion)
                                listener.Notify(new InboundAssociation(task.Result));
                        });
#pragma warning restore 4014
                }
                catch (SocketException ex)
                when (ex.SocketErrorCode == SocketError.ConnectionReset && !ShutdownToken.IsCancellationRequested)
                {
                    // Tcp handshake failed on new connection, not fatal
                    //TODO Log Info

                    socket?.Close();
                }
                catch (Exception)
                {
                    if (ShutdownToken.IsCancellationRequested)
                        return;

                    // Should not happen if listen on Loopback or IPAny
                    // TODO Otherwise might need to rebind if network adapter was disabled/enabled
                    //TODO Log Warning

                    socket?.Close();
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

        protected virtual Task<AssociationHandle> CreateInboundAssociation(Stream stream, Address remoteAddress)
        {
            var association = new StreamAssociationHandle(Settings, stream, InboundAddress, remoteAddress);
            RegisterAssociation(association);

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return Task.FromResult((AssociationHandle)association);
        }

        protected async Task<AssociationHandle> CreateOutboundAssociation(Address remoteAddress)
        {
            Socket socket = CreateDualModeTcpSocket();
            Settings.ConfigureSocket(socket);

            //TODO Does it even work if the port is not set?
            int port = remoteAddress.Port ?? 2552;

            var connectTask = Task.Factory.FromAsync(socket.BeginConnect, socket.EndConnect, remoteAddress.Host, port, null);

            var completedTask = await Task.WhenAny(connectTask, Task.Delay(Settings.ConnectionTimeout));

            if (completedTask != connectTask)
                throw new TimeoutException($"Unable to establish connection in {(int)Settings.ConnectionTimeout.TotalSeconds}s");

            NetworkStream stream = new NetworkStream(socket, true);

            int localPort = ((IPEndPoint) socket.LocalEndPoint).Port;

            var association =  await CreateOutboundAssociation(stream, InboundAddress.WithPort(localPort), remoteAddress);

            return association;
        }

        protected virtual Task<AssociationHandle> CreateOutboundAssociation(Stream stream, Address localAddress, Address remoteAddress)
        {
            var association = new StreamAssociationHandle(Settings, stream, localAddress, remoteAddress);
            RegisterAssociation(association);

#pragma warning disable CS4014
            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
#pragma warning restore CS4014

            return Task.FromResult((AssociationHandle)association);
        }

        private static Socket CreateDualModeTcpSocket()
        {
            // DualMode works with IPv6 and IPv4
            return new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        private static string GetAddressString(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
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
