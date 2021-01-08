//-----------------------------------------------------------------------
// <copyright file="UdpConnected.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO.Buffers;

namespace Akka.IO
{
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// UDP Extension for Akka’s IO layer.
    ///
    /// This extension implements the connectionless UDP protocol with
    /// calling `connect` on the underlying sockets, i.e. with restricting
    /// from whom data can be received. For "unconnected" UDP mode see <see cref="Udp"/>.
    ///
    /// For a full description of the design and philosophy behind this IO
    /// implementation please refer to <see href="http://getakka.net/">the Akka online documentation</see>.
    /// </summary>
    public class UdpConnected : ExtensionIdProvider<UdpConnectedExt>
    {
        #region internal connection messages

        internal abstract class SocketCompleted
        {
            public readonly SocketAsyncEventArgs EventArgs;

            protected SocketCompleted(SocketAsyncEventArgs eventArgs)
            {
                EventArgs = eventArgs;
            }
        }

        internal sealed class SocketSent : SocketCompleted
        {
            public SocketSent(SocketAsyncEventArgs eventArgs) : base(eventArgs)
            {
            }
        }

        internal sealed class SocketReceived : SocketCompleted
        {
            public SocketReceived(SocketAsyncEventArgs eventArgs) : base(eventArgs)
            {
            }
        }

        internal sealed class SocketAccepted : SocketCompleted
        {
            public SocketAccepted(SocketAsyncEventArgs eventArgs) : base(eventArgs)
            {
            }
        }

        internal sealed class SocketConnected : SocketCompleted
        {
            public SocketConnected(SocketAsyncEventArgs eventArgs) : base(eventArgs)
            {
            }
        }

        #endregion

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
        public abstract class Command : Message
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
            public object FailureMessage { get; }
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
            public object Token { get; }
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
            [Obsolete("Akka.IO.Udp.Send public constructors are obsolete. Use `Send.Create` or `Send(ByteString, EndPoint, Event)` instead.")]
            public Send(IEnumerator<ByteBuffer> payload, Event ack)
                : this(ByteString.FromBuffers(payload), ack)
            {
            }

            /// <summary>
            /// Creates a new send request to be executed via UDP socket to a addressed to an endpoint known by the connected UDP actor.
            /// Once send completes, this request will acknowledged back on the sender side with an <paramref name="ack"/>
            /// object.
            /// </summary>
            /// <param name="payload">Binary payload to be send.</param>
            /// <param name="ack">Acknowledgement send back to the sender, once <paramref name="payload"/> has been send through a socket.</param>
            public Send(ByteString payload, object ack)
            {
                Payload = payload;
                Ack = ack ?? throw new ArgumentNullException(nameof(ack), "ack must be non-null. Use NoAck if you don't want acks.");
            }

            /// <summary>
            /// A binary payload to be send to an endpoint known by connected UDP actor. It must fit into a single UDP datagram.
            /// </summary>
            public ByteString Payload { get; }

            /// <summary>
            /// Acknowledgement send back to the sender, once <see cref="Payload"/> has been send through a socket.
            /// If it's <see cref="NoAck"/>, then no acknowledgement will be send.
            /// </summary>
            public object Ack { get; }

            /// <summary>
            /// Flag determining is a message sender is interested in receving send acknowledgement.
            /// </summary>
            public bool WantsAck => !(Ack is NoAck);

            /// <summary>
            /// Creates a new send request to be executed via UDP socket to a addressed to an endpoint known by the connected UDP actor.
            /// Once send completes, this request will not be acknowledged on by the sender side.
            /// object.
            /// </summary>
            /// <param name="payload">Binary payload to be send.</param>
            public static Send Create(ByteString payload) => new Send(payload, NoAck.Instance);
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
            public IActorRef Handler { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint RemoteAddress { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint LocalAddress { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Inet.SocketOption> Options { get; }
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
            public ByteString Data { get; }
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
            public Command Cmd { get; }
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
        public UdpConnectedExt(ExtendedActorSystem system)
            : this(system, UdpSettings.Create(system.Settings.Config.GetConfig("akka.io.udp-connected")))
        {
            
        }

        public UdpConnectedExt(ExtendedActorSystem system, UdpSettings settings)
        {
            var bufferPoolConfig = system.Settings.Config.GetConfig(settings.BufferPoolConfigPath);
            if (bufferPoolConfig.IsNullOrEmpty())
                throw new ConfigurationException($"Cannot retrieve UDP buffer pool configuration: {settings.BufferPoolConfigPath} configuration node not found");

            Settings = settings;
            BufferPool = CreateBufferPool(system, bufferPoolConfig);
            SocketEventArgsPool = new PreallocatedSocketEventAgrsPool(Settings.InitialSocketAsyncEventArgs, OnComplete);
            Manager = system.SystemActorOf(
                props: Props.Create(() => new UdpConnectedManager(this)).WithDeploy(Deploy.Local),
                name: "IO-UDP-CONN");
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRef Manager { get; }

        /// <summary>
        /// A buffer pool used by current plugin.
        /// </summary>
        public IBufferPool BufferPool { get; }

        internal ISocketEventArgsPool SocketEventArgsPool { get; }
        internal UdpSettings Settings { get; }

        private IBufferPool CreateBufferPool(ExtendedActorSystem system, Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<IBufferPool>();

            var type = Type.GetType(config.GetString("class", null), true);

            if (!typeof(IBufferPool).IsAssignableFrom(type))
                throw new ArgumentException($"Buffer pool of type {type} doesn't implement {nameof(IBufferPool)} interface");

            try
            {
                // try to construct via `BufferPool(ExtendedActorSystem, Config)` ctor
                return (IBufferPool)Activator.CreateInstance(type, system, config);
            }
            catch
            {
                // try to construct via `BufferPool(ExtendedActorSystem)` ctor
                return (IBufferPool)Activator.CreateInstance(type, system);
            }
        }

        private void OnComplete(object sender, SocketAsyncEventArgs e)
        {
            var actorRef = e.UserToken as IActorRef;
            actorRef?.Tell(ResolveMessage(e));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private UdpConnected.SocketCompleted ResolveMessage(SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                case SocketAsyncOperation.ReceiveFrom:
                    return new UdpConnected.SocketReceived(e);
                case SocketAsyncOperation.Send:
                case SocketAsyncOperation.SendTo:
                    return new UdpConnected.SocketSent(e);
                case SocketAsyncOperation.Accept:
                    return new UdpConnected.SocketAccepted(e);
                case SocketAsyncOperation.Connect:
                    return new UdpConnected.SocketConnected(e);
                default:
                    throw new NotSupportedException($"Socket operation {e.LastOperation} is not supported");
            }
        }
    }
}
