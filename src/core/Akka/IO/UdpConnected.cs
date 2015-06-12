using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;

namespace Akka.IO
{
    /// <summary>
    /// UDP Extension for Akka’s IO layer.
    ///
    /// This extension implements the connectionless UDP protocol with
    /// calling `connect` on the underlying sockets, i.e. with restricting
    /// from whom data can be received. For “unconnected” UDP mode see <see cref="Udp"/>.
    ///
    /// For a full description of the design and philosophy behind this IO
    /// implementation please refer to <see href="http://doc.akka.io/">the Akka online documentation</see>.
    /// </summary>
    public class UdpConnected : ExtensionIdProvider<UdpConnectedExt>
    {
        public static readonly UdpConnected Instance = new UdpConnected();

        public override UdpConnectedExt CreateExtension(ExtendedActorSystem system)
        {
            return new UdpConnectedExt(system);
        }

        /// <summary>
        /// The common interface for <see cref="Command"/> and <see cref="Event"/>.
        /// </summary>
        public abstract class Message { }

        /// <summary>
        /// The common type of all commands supported by the UDP implementation.
        /// </summary>
        public abstract class Command : Message, SelectionHandler.IHasFailureMessage
        {
            protected Command()
            {
                FailureMessage = new CommandFailed(this);
            }

            public object FailureMessage { get; private set; }
        }

        /// <summary>
        /// Each <see cref="Send"/> can optionally request a positive acknowledgment to be sent
        /// to the commanding actor. If such notification is not desired the <see cref="Send.Ack"/>
        /// must be set to an instance of this class. The token contained within can be used
        /// to recognize which write failed when receiving a <see cref="CommandFailed"/> message.
        /// </summary>
        public class NoAck : Event
        {
            /// <summary>
            /// Default <see cref="NoAck"/> instance which is used when no acknowledgment information is
            /// explicitly provided. Its “token” is `null`.
            /// </summary>
            public static readonly NoAck Instance = new NoAck(null);

            public NoAck(object token)
            {
                Token = token;
            }

            public object Token { get; private set; }
        }

        /// <summary>
        /// This message is understood by the connection actors to send data to their
        /// designated destination. The connection actor will respond with
        /// <see cref="CommandFailed"/> if the send could not be enqueued to the O/S kernel
        /// because the send buffer was full. If the given `ack` is not of type <see cref="NoAck"/>
        /// the connection actor will reply with the given object as soon as the datagram
        /// has been successfully enqueued to the O/S kernel.
        /// </summary>
        public sealed class Send : Command
        {
            public Send(ByteString payload, object ack)
            {
                if(ack == null)
                    throw new ArgumentNullException("ack", "ack must be non-null. Use NoAck if you don't want acks.");

                Payload = payload;
                Ack = ack;
            }

            public ByteString Payload { get; private set; }
            public object Ack { get; private set; }

            public bool WantsAck
            {
                get { return !(Ack is NoAck); }
            }

            public static Send Create(ByteString data)
            {
                return new Send(data, NoAck.Instance);
            }
        }

        /// <summary>
        /// Send this message to the <see cref="UdpExt.Manager"/> in order to bind to a local
        /// port (optionally with the chosen `localAddress`) and create a UDP socket
        /// which is restricted to sending to and receiving from the given `remoteAddress`.
        /// All received datagrams will be sent to the designated `handler` actor.
        /// </summary>
        public sealed class Connect : Command
        {
            public Connect(IActorRef handler, 
                           IPEndPoint remoteAddress,
                           IPEndPoint localAddress = null, 
                           IEnumerable<Inet.SocketOption> options = null)
            {
                Handler = handler;
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
                Options = options ?? Enumerable.Empty<Inet.SocketOption>();
            }

            public IActorRef Handler { get; private set; }
            public IPEndPoint RemoteAddress { get; private set; }
            public IPEndPoint LocalAddress { get; private set; }
            public IEnumerable<Inet.SocketOption> Options { get; private set; }
        }

        /// <summary>
        /// Send this message to a connection actor (which had previously sent the
        /// <see cref="Connected"/> message) in order to close the socket. The connection actor
        /// will reply with a <see cref="Disconnected"/> message.
        /// </summary>
        public class Disconnect : Command
        {
            public static readonly Disconnect Instance = new Disconnect();

            private Disconnect()
            {
                
            }
        }

        /// <summary>
        /// Send this message to a listener actor (which sent a <see cref="Udp.Bound"/> message) to
        /// have it stop reading datagrams from the network. If the O/S kernel’s receive
        /// buffer runs full then subsequent datagrams will be silently discarded.
        /// Re-enable reading from the socket using the `ResumeReading` command.
        /// </summary>
        public class SuspendReading : Command
        {
            public static readonly SuspendReading Instance = new SuspendReading();

            private SuspendReading()
            { }
        }

        /// <summary>
        /// This message must be sent to the listener actor to re-enable reading from
        /// the socket after a `SuspendReading` command.
        /// </summary>
        public class ResumeReading : Command
        {
            public static readonly ResumeReading Instance = new ResumeReading();

            private ResumeReading()
            { }
        }

        /// <summary>
        /// The common type of all events emitted by the UDP implementation.
        /// </summary>
        public abstract class Event : Message { }

        /// <summary>
        /// When a connection actor receives a datagram from its socket it will send
        /// it to the handler designated in the <see cref="Udp.Bind"/> message using this message type.
        /// </summary>
        public sealed class Received : Event
        {
            public Received(ByteString data)
            {
                Data = data;
            }

            public ByteString Data { get; private set; }
        }

        /// <summary>
        /// When a command fails it will be replied to with this message type,
        /// wrapping the failing command object.
        /// </summary>
        public sealed class CommandFailed : Event
        {
            public CommandFailed(Command cmd)
            {
                Cmd = cmd;
            }

            public Command Cmd { get; private set; }
        }

        /// <summary>
        /// This message is sent by the connection actor to the actor which sent the
        /// <see cref="Connect"/> message when the UDP socket has been bound to the local and
        /// remote addresses given.
        /// </summary>
        public class Connected : Event
        {
            public static readonly Connected Instance = new Connected();

            private Connected()
            { }
        }

        /// <summary>
        /// This message is sent by the connection actor to the actor which sent the
        /// `Disconnect` message when the UDP socket has been closed.
        /// </summary>
        public class Disconnected : Event
        {
            public static readonly Disconnected Instance = new Disconnected();

            private Disconnected()
            { }
        }

    }

    public class UdpConnectedExt : Extension
    {
        private readonly IActorRef _manager;

        public UdpConnectedExt(ExtendedActorSystem system)
        {
            Settings = new Udp.UdpSettings(system.Settings.Config.GetConfig("akka.io.udp-connected"));
            BufferPool = new DirectByteBufferPool(Settings.DirectBufferSize, Settings.MaxDirectBufferPoolSize);
            _manager = system.SystemActorOf(
                props: Props.Create(() => new UdpConnectedManager(this)).WithDeploy(Deploy.Local),
                name: "IO-UDP-CONN");
        }

        public override IActorRef Manager
        {
            get { return _manager; }
        }

        internal IBufferPool BufferPool { get; private set; }
        internal Udp.UdpSettings Settings { get; private set; }
    }
}
