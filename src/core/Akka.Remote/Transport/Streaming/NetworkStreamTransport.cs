using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;

namespace Akka.Remote.Transport.Streaming
{
    public class NetworkStreamTransportSettings
    {
        private const int DefaultSendBufferSize = 128 * 1024;
        private const int DefaultReceiveBufferSize = 128 * 1024;

        public string Hostname { get; }

        public int Port { get; }

        public int SendBufferSize { get; }

        public int ReceiveBufferSize { get; }

        public NetworkStreamTransportSettings(Config config)
        {
            Hostname = config.GetString("hostname");
            Port = config.GetInt("port");
            SendBufferSize = config.GetInt("send-buffer-size", DefaultSendBufferSize);
            ReceiveBufferSize = config.GetInt("receive-buffer-size", DefaultReceiveBufferSize);
        }

        public void ConfigureSocket(Socket s)
        {
            s.NoDelay = true;

            s.SendBufferSize = SendBufferSize;
            s.ReceiveBufferSize = ReceiveBufferSize;
        }
    }

    public class NetworkStreamTransport : StreamTransport
    {
        public const string ProtocolName = "tcp";

        private readonly Socket _listener;

        private readonly NetworkStreamTransportSettings _settings;

        public override string SchemeIdentifier
        {
            get { return ProtocolName; }
        }

        public NetworkStreamTransport(ActorSystem system, Config config)
            : base(system, config)
        {
            _settings = new NetworkStreamTransportSettings(config);

            _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        protected override Address Initialize()
        {
            _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, _settings.Port));
            _listener.Listen(int.MaxValue);

            return new Address(ProtocolName, System.Name, _settings.Hostname, ((IPEndPoint)_listener.LocalEndPoint).Port);
        }

        protected override void StartAcceptingConnections(IAssociationEventListener listener)
        {
            Task.Run(() => ListenLoop(listener));
        }

        private async Task ListenLoop(IAssociationEventListener listener)
        {
            // Accept will throw when _listener is closed
            while (!CancelToken.IsCancellationRequested)
            {
                try
                {
                    var socket = await Task<Socket>.Factory.FromAsync(_listener.BeginAccept, _listener.EndAccept, null);

                    _settings.ConfigureSocket(socket);

                    IPEndPoint remoteEndPoint = (IPEndPoint)socket.RemoteEndPoint;
                    string host = GetAddressString(remoteEndPoint.Address);

                    var remoteAddress = new Address(ProtocolName, System.Name, host, remoteEndPoint.Port);

                    var networkStream = new NetworkStream(socket, true);

#pragma warning disable 4014
                    CreateInboundAssociation(networkStream, remoteAddress)
                        .ContinueWith(task =>
                        {
                            //TODO Do we need to notify a failure?

                            if (task.Status == TaskStatus.RanToCompletion)
                                listener.Notify(new InboundAssociation(task.Result));
                        });
#pragma warning restore 4014
                }
                catch (Exception)
                {
                    //TODO Log and shutdown if fatal exception
                }
            }
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
        public static IPAddress SafeMapToIPv4(IPAddress address)
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

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            return CreateOutboundAssociation(remoteAddress);
        }

        public override void Cleanup()
        {
            _listener.Close();
        }

        public virtual Task<AssociationHandle> CreateInboundAssociation(Stream stream, Address remoteAddress)
        {
            var association = new StreamAssociationHandle(stream, InboundAddress, remoteAddress);

            // TODO Can ReadHandlerSource.Task fail? If so what do we do

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return Task.FromResult((AssociationHandle)association);
        }

        public async Task<AssociationHandle> CreateOutboundAssociation(Address remoteAddress)
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

            _settings.ConfigureSocket(socket);

            int port = remoteAddress.Port ?? 2552;

            await Task.Factory.FromAsync(socket.BeginConnect, socket.EndConnect, remoteAddress.Host, port, null);

            NetworkStream stream = new NetworkStream(socket, true);

            return await CreateOutboundAssociation(stream, InboundAddress, remoteAddress);
        }

        public virtual Task<AssociationHandle> CreateOutboundAssociation(Stream stream, Address localAddress, Address remoteAddress)
        {
            var association = new StreamAssociationHandle(stream, localAddress, remoteAddress);

            // TODO Can ReadHandlerSource.Task fail? If so what do we do

#pragma warning disable CS4014
            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
#pragma warning restore CS4014

            return Task.FromResult((AssociationHandle)association);
        }
    }
}
