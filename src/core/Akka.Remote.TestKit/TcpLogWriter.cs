using System;
using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.IO;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Used to send semantic log messages to the MultiNodeTestRunner.
    /// </summary>
    public class UdpLogger : ReceiveActor
    {
        private IActorRef _udpLogWriter;

        /// <summary>
        /// Default constructor - takes no arguments.
        /// 
        /// All of the real work is handled by <see cref="TcpLogWriter"/>
        /// internally.
        /// </summary>
        public UdpLogger()
        {
            Receive<InitializeLogger>(initialize =>
            {
                Sender.Tell(new LoggerInitialized());
            });
            ReceiveAny(o =>
            {
                _udpLogWriter.Tell(o.ToString());
            });
        }

        /// <summary>
        /// Initializes the <see cref="TcpLogWriter"/> using arguments passed into
        /// the multi-node test runner via <see cref="CommandLine"/>
        /// </summary>
        protected override void PreStart()
        {
            _udpLogWriter = Context.ActorOf(Props.Create(() => new UdpLogger()), "udpLogWriter");
        }
    }

    internal class TcpLogWriter : ReceiveActor, IWithUnboundedStash
    {
        #region Message classes

        /// <summary>
        /// Used to poll <see cref="TcpLogWriter"/> to determine if it's connected
        /// to the server on the other end of the wire.
        /// </summary>
        public class IsConnected
        {
            public static readonly IsConnected Instance = new IsConnected();
            private IsConnected() { }
        }

        /// <summary>
        /// In situations where the <see cref="TcpLogWriter"/> is started without being told
        /// to connect automatically, a user can send <see cref="ConnectNow"/> to force it to connect now.
        /// </summary>
        public class ConnectNow
        {
            public static readonly ConnectNow Instance = new ConnectNow();
            private ConnectNow() { }
        }

        #endregion

        private readonly EndPoint _remoteDestination;
        private IActorRef _server;
        private int _timeoutCount = 0;
        private readonly bool _connectAutomatically;

        public const int MaxAllowableTimeouts = 5;

        /// <summary>
        /// Append a 2-byte header to each message describing how long the FQN name is
        /// </summary>
        public const int LengthFrameLength = sizeof(int);

        public static readonly byte[] StringTypeNameAsBytes = Encoding.Unicode.GetBytes(typeof (string).FullName);

        /// <summary>
        /// Constructor used when running inside the MultinodeTestRunner
        /// </summary>
        public TcpLogWriter() : this(CommandLine.GetProperty("multinode.listen-address"), CommandLine.GetInt32("multinode.listen-port")) { }

        public TcpLogWriter(string remoteAddress, int remotePort, bool connectAutomatically = true)
            : this(IPAddress.Parse(remoteAddress), remotePort, connectAutomatically)
        { }

        public TcpLogWriter(IPAddress remoteAddress, int remotePort, bool connectAutomatically = true) : 
            this(new IPEndPoint(remoteAddress, remotePort), connectAutomatically)
        { }

        public TcpLogWriter(EndPoint remoteDestination, bool connectAutomatically = true)
        {
            _remoteDestination = remoteDestination;
            _connectAutomatically = connectAutomatically;
            Disconnected();
        }

        protected override void PreStart()
        {
            if (_connectAutomatically)
            {
                // kick off the connection process immediately
                ConnectToServer();
                SetReceiveTimeout(TimeSpan.FromSeconds(1));
            }
        }

        private void Disconnected()
        {
            Receive<Tcp.Connected>(connect =>
            {
                _server = Sender;
                Sender.Tell(new Tcp.Register(Self));
                BecomeConnected();
            });

            Receive<ConnectNow>(connectNow =>
            {
                ConnectToServer();
                SetReceiveTimeout(TimeSpan.FromSeconds(1));
            });

            Receive<IsConnected>(connected => Sender.Tell(false));

            Receive<ReceiveTimeout>(timeout =>
            {
                if (++_timeoutCount < MaxAllowableTimeouts)
                {
                    ConnectToServer();
                }
                else
                {
                    Context.Stop(Self);

                    // TODO: is there a way we can log this without recursively logging to ourselves
                    // if XUnit2 is capturing all STDOUT output?
                    throw new LoggerInitializationException("Unable to connect to {0} for UDP logging.");
                }
            });

            ReceiveAny(o => Stash.Stash());
        }

        private void BecomeConnected()
        {
            Stash.UnstashAll();
            SetReceiveTimeout(null); //cancel ReceiveTimeout
            Become(Connected);
        }

        private void Connected()
        {
            Receive<IsConnected>(connected => Sender.Tell(true));
            Receive<string>(o =>
            {
                _server.Tell(Tcp.Write.Create(ByteString.FromString(o)));
            });
        }

        /// <summary>
        /// Attempts to connect to the UDP listener on the other side of the wire
        /// </summary>
        private void ConnectToServer()
        {
           Tcp.Instance.Apply(Context.System).Manager.Tell(new Tcp.Connect(_remoteDestination), Self);
        }

        public IStash Stash { get; set; }
    }
}
