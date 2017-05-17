//-----------------------------------------------------------------------
// <copyright file="Udp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    /// <summary>
    /// UDP Extension for Akka’s IO layer.
    ///
    /// This extension implements the connectionless UDP protocol without
    /// calling `connect` on the underlying sockets, i.e. without restricting
    /// from whom data can be received. For "connected" UDP mode see <see cref="UdpConnected"/>.
    ///
    /// For a full description of the design and philosophy behind this IO
    /// implementation please refer to <see href="http://doc.akka.io/">the Akka online documentation</see>.
    /// </summary>
    public class Udp : ExtensionIdProvider<UdpExt>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Udp Instance = new Udp();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef Manager(ActorSystem system)
        {
            return Instance.Apply(system).Manager;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override UdpExt CreateExtension(ExtendedActorSystem system)
        {
            return new UdpExt(system);
        }

        /// <summary>The common interface for <see cref="Command"/> and <see cref="Event"/>.</summary>
        public abstract class Message { }

        /// <summary>The common type of all commands supported by the UDP implementation.</summary>
        public abstract class Command : Message, SelectionHandler.IHasFailureMessage
        {
            private object _failureMessage;

            /// <summary>
            /// TBD
            /// </summary>
            public object FailureMessage
            {
                get { return _failureMessage = _failureMessage ?? new CommandFailed(this); }
            }
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
        /// This message is understood by the "simple sender" which can be obtained by
        /// sending the <see cref="SimpleSender"/> query to the <see cref="UdpExt.Manager"/> as well as by
        /// the listener actors which are created in response to <see cref="Bind"/>. It will send
        /// the given payload data as one UDP datagram to the given target address. The
        /// UDP actor will respond with <see cref="CommandFailed"/> if the send could not be
        /// enqueued to the O/S kernel because the send buffer was full. If the given
        /// `ack` is not of type <see cref="NoAck"/> the UDP actor will reply with the given
        /// object as soon as the datagram has been successfully enqueued to the O/S
        /// kernel.
        ///
        /// The sending UDP socket’s address belongs to the "simple sender" which does
        /// not handle inbound datagrams and sends from an ephemeral port; therefore
        /// sending using this mechanism is not suitable if replies are expected, use
        /// <see cref="Bind"/> in that case.
        /// </summary>
        public sealed class Send : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="payload">TBD</param>
            /// <param name="target">TBD</param>
            /// <param name="ack">TBD</param>
            /// <exception cref="ArgumentNullException">TBD</exception>
            public Send(ByteString payload, EndPoint target, Event ack)
            {
                if (ack == null)
                    throw new ArgumentNullException(nameof(ack), "ack must be non-null. Use NoAck if you don't want acks.");
                Payload = payload;
                Target = target;
                Ack = ack;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ByteString Payload { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint Target { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Event Ack { get; private set; }

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
            /// <param name="target">TBD</param>
            /// <returns>TBD</returns>
            public static Send Create(ByteString data, EndPoint target)
            {
                return new Send(data, target, NoAck.Instance);
            }
        }

        /// <summary>
        ///  Send this message to the <see cref="UdpExt.Manager"/> in order to bind to the given
        ///  local port (or an automatically assigned one if the port number is zero).
        ///  The listener actor for the newly bound port will reply with a <see cref="Bound"/>
        ///  message, or the manager will reply with a <see cref="CommandFailed"/> message.
        /// </summary>
        public sealed class Bind : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handler">TBD</param>
            /// <param name="localAddress">TBD</param>
            /// <param name="options">TBD</param>
            public Bind(IActorRef handler, EndPoint localAddress, IEnumerable<Inet.SocketOption> options = null)
            {
                Handler = handler;
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
            public EndPoint LocalAddress { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Inet.SocketOption> Options { get; private set; }
        }

        /// <summary>
        /// Send this message to the listener actor that previously sent a <see cref="Bound"/>
        /// message in order to close the listening socket. The recipient will reply
        /// with an <see cref="Unbound"/> message.
        /// </summary>
        public class Unbind : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Unbind Instance = new Unbind();

            private Unbind() { }
        }

        /// <summary>
        /// Retrieve a reference to a "simple sender" actor of the UDP extension.
        /// The newly created "simple sender" will reply with the <see cref="SimpleSenderReady" /> notification.
        ///
        /// The "simple sender" is a convenient service for being able to send datagrams
        /// when the originating address is meaningless, i.e. when no reply is expected.
        ///
        /// The "simple sender" will not stop itself, you will have to send it a <see cref="Akka.Actor.PoisonPill"/>
        /// when you want to close the socket.
        /// </summary>
        public class SimpleSender : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly SimpleSender Instance = new SimpleSender();

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="options">TBD</param>
            public SimpleSender(IEnumerable<Inet.SocketOption> options = null)
            {
                Options = options ?? Enumerable.Empty<Inet.SocketOption>();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Inet.SocketOption> Options { get; private set; }
        }

        /// <summary>
        /// Send this message to a listener actor (which sent a <see cref="Bound"/> message) to
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
        ///  This message must be sent to the listener actor to re-enable reading from
        ///  the socket after a `SuspendReading` command.
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

        /// <summary>The common type of all events emitted by the UDP implementation.</summary>
        public abstract class Event : Message { }

        /// <summary>
        ///  When a listener actor receives a datagram from its socket it will send
        ///  it to the handler designated in the <see cref="Bind"/> message using this message type.
        /// </summary>
        public sealed class Received : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="data">TBD</param>
            /// <param name="sender">TBD</param>
            public Received(ByteString data, EndPoint sender)
            {
                Data = data;
                Sender = sender;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ByteString Data { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint Sender { get; private set; }
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
        /// This message is sent by the listener actor in response to a <see cref="Bind"/> command.
        /// If the address to bind to specified a port number of zero, then this message
        /// can be inspected to find out which port was automatically assigned.
        /// </summary>
        public sealed class Bound : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="localAddress">TBD</param>
            public Bound(EndPoint localAddress)
            {
                LocalAddress = localAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint LocalAddress { get; private set; }
        }

        /// <summary> The "simple sender" sends this message type in response to a <see cref="SimpleSender"/> query. </summary>
        public sealed class SimpleSenderReady : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly SimpleSenderReady Instance = new SimpleSenderReady();

            private  SimpleSenderReady() { }
        }

        /// <summary>
        /// This message is sent by the listener actor in response to an `Unbind` command
        /// after the socket has been closed.
        /// </summary>
        public class Unbound
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Unbound Instance = new Unbound();
            private Unbound() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class SO : Inet.SoForwarders
        {
            /// <summary>
            /// <see cref="Akka.IO.Inet.SocketOption"/> to set the SO_BROADCAST option
            ///
            /// For more information see cref="System.Net.Sockets.Socket.EnableBroadcast"/>
            /// </summary>
            public sealed class Broadcast : Inet.SocketOption
            {
                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="on">TBD</param>
                public Broadcast(bool on)
                {
                    On = on;
                }
                /// <summary>
                /// TBD
                /// </summary>
                public bool On { get; private set; }

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="s">TBD</param>
                public override void BeforeDatagramBind(Socket s)
                {
                    s.EnableBroadcast = On;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class UdpSettings : SelectionHandlerSettings
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="config">TBD</param>
            public UdpSettings(Config config) 
                : base(config)
            {
                NrOfSelectors = config.GetInt("nr-of-selectors");
                DirectBufferSize = config.GetInt("direct-buffer-size");
                MaxDirectBufferPoolSize = config.GetInt("direct-buffer-pool-limit");
                BatchReceiveLimit = config.GetInt("receive-throughput");

                ManagementDispatcher = config.GetString("management-dispatcher");

                MaxChannelsPerSelector = MaxChannels == -1 ? -1 : Math.Max(MaxChannels/NrOfSelectors, 1);
            }

            /// <summary>
            /// TBD
            /// </summary>
            public int NrOfSelectors { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int DirectBufferSize { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int MaxDirectBufferPoolSize { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int BatchReceiveLimit { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public string ManagementDispatcher { get; private set; }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class UdpExt : IOExtension
    {
        private readonly Udp.UdpSettings _settings;
        private readonly IActorRef _manager;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public UdpExt(ExtendedActorSystem system)
        {
            _settings = new Udp.UdpSettings(system.Settings.Config.GetConfig("akka.io.udp"));
            _manager = system.SystemActorOf(
                props: Props.Create(() => new UdpManager(this)).WithDeploy(Deploy.Local), 
                name: "IO-UDP-FF");

            BufferPool = new DirectByteBufferPool(_settings.DirectBufferSize, _settings.MaxDirectBufferPoolSize);
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
        internal Udp.UdpSettings Setting { get { return _settings; } }

        /// <summary>
        /// TBD
        /// </summary>
        internal DirectByteBufferPool BufferPool { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class UdpExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef Udp(this ActorSystem system)
        {
            return IO.Udp.Manager(system);
        }
    }

}
#endif