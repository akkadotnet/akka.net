using System;
using System.Net;
using Akka.Actor;
using Akka.IO;
using Akka.Serialization;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    public class UdpLogger : ReceiveActor, IWithUnboundedStash
    {
        #region Message classes

        /// <summary>
        /// Used to poll <see cref="UdpLogger"/> to determine if it's connected
        /// to the server on the other end of the wire.
        /// </summary>
        public class IsConnected
        {
            public static readonly IsConnected Instance = new IsConnected();
            private IsConnected() { }
        }

        /// <summary>
        /// In situations where the <see cref="UdpLogger"/> is started without being told
        /// to connect automatically, a user can send <see cref="ConnectNow"/> to force it to connect now.
        /// </summary>
        public class ConnectNow
        {
            public static readonly ConnectNow Instance = new ConnectNow();
            private ConnectNow() { }
        }

        #endregion

        private readonly IPEndPoint _remoteDestination;
        private IActorRef _server;
        private int timeoutCount = 0;
        private readonly Serializer _serializer;
        private readonly bool _connectAutomatically;

        public const int MaxAllowableTimeouts = 5;

        public UdpLogger(IPEndPoint remoteDestination, bool connectAutomatically = true)
        {
            _remoteDestination = remoteDestination;
            _connectAutomatically = connectAutomatically;
            _serializer = Context.System.Serialization.FindSerializerForType(typeof (SpecPass));
            
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
            Receive<UdpConnected.Connect>(connect =>
            {
                _server = Sender;
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
                if (++timeoutCount < MaxAllowableTimeouts)
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
            ReceiveAny(o =>
            {
                ByteString data = ToByteString(o);
                _server.Tell(UdpConnected.Send.Create(data));
            });
        }

        /// <summary>
        /// Attempts to connect to the UDP listener on the other side of the wire
        /// </summary>
        private void ConnectToServer()
        {
            UdpConnected.Instance.Apply(Context.System).Manager.Tell(new UdpConnected.Connect(Self, _remoteDestination));
        }

        private ByteString ToByteString(object o)
        {
            var bytes = _serializer.ToBinary(o);
            return ByteString.FromByteBuffer(ByteBuffer.Wrap(bytes));
        }

        public IStash Stash { get; set; }
    }
}
