//-----------------------------------------------------------------------
// <copyright file="UdpConnected.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
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
    /// from whom data can be received. For "unconnected" UDP mode see <see cref="Udp"/>.
    ///
    /// For a full description of the design and philosophy behind this IO
    /// implementation please refer to <see href="http://doc.akka.io/">the Akka online documentation</see>.
    /// </summary>
    public class UdpConnected : ExtensionIdProvider<UdpConnectedExt>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly UdpConnected Instance = new UdpConnected();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
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
            /// <summary>
            /// TBD
            /// </summary>
            protected Command()
            {
                FailureMessage = new CommandFailed(this);
            }

            /// <summary>
            /// TBD
            /// </summary>
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
            /// explicitly provided. Its "token" is <see langword="null"/>.
            /// </summary>
            public static readonly NoAck Instance = new NoAck(null);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="token">TBD</param>
            public NoAck(object token)
            {
                Token = token;
            }

            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="payload">TBD</param>
            /// <param name="ack">TBD</param>
            /// <exception cref="ArgumentNullException">TBD</exception>
            public Send(ByteString payload, object ack)
            {
                if(ack == null)
                    throw new ArgumentNullException(nameof(ack), "ack must be non-null. Use NoAck if you don't want acks.");

                Payload = payload;
                Ack = ack;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ByteString Payload { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public object Ack { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool WantsAck
            {
                get { return !(Ack is NoAck); }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="data">TBD</param>
            /// <returns>TBD</returns>
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
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handler">TBD</param>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="localAddress">TBD</param>
            /// <param name="options">TBD</param>
            public Connect(IActorRef handler, 
                           EndPoint remoteAddress,
                           EndPoint localAddress = null, 
                           IEnumerable<Inet.SocketOption> options = null)
            {
                Handler = handler;
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
                Options = options ?? Enumerable.Empty<Inet.SocketOption>();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Handler { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint RemoteAddress { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint LocalAddress { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Inet.SocketOption> Options { get; private set; }
        }

        /// <summary>
        /// Send this message to a connection actor (which had previously sent the
        /// <see cref="Connected"/> message) in order to close the socket. The connection actor
        /// will reply with a <see cref="Disconnected"/> message.
        /// </summary>
        public class Disconnect : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="data">TBD</param>
            public Received(ByteString data)
            {
                Data = data;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ByteString Data { get; private set; }
        }

        /// <summary>
        /// When a command fails it will be replied to with this message type,
        /// wrapping the failing command object.
        /// </summary>
        public sealed class CommandFailed : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cmd">TBD</param>
            public CommandFailed(Command cmd)
            {
                Cmd = cmd;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Command Cmd { get; private set; }
        }

        /// <summary>
        /// This message is sent by the connection actor to the actor which sent the
        /// <see cref="Connect"/> message when the UDP socket has been bound to the local and
        /// remote addresses given.
        /// </summary>
        public class Connected : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Disconnected Instance = new Disconnected();

            private Disconnected()
            { }
        }

    }

    /// <summary>
    /// TBD
    /// </summary>
    public class UdpConnectedExt : IOExtension
    {
        private readonly IActorRef _manager;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public UdpConnectedExt(ExtendedActorSystem system)
        {
            Settings = new Udp.UdpSettings(system.Settings.Config.GetConfig("akka.io.udp-connected"));
            BufferPool = new DirectByteBufferPool(Settings.DirectBufferSize, Settings.MaxDirectBufferPoolSize);
            _manager = system.SystemActorOf(
                props: Props.Create(() => new UdpConnectedManager(this)).WithDeploy(Deploy.Local),
                name: "IO-UDP-CONN");
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRef Manager
        {
            get { return _manager; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal IBufferPool BufferPool { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        internal Udp.UdpSettings Settings { get; private set; }
    }
}
#endif