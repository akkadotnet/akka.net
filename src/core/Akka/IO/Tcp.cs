//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    public class Tcp : ExtensionIdProvider<TcpExt>
    {
        public static readonly Tcp Instance = new Tcp();

        public override TcpExt CreateExtension(ExtendedActorSystem system)
        {
            return new TcpExt(system);
        }

        public class Message : INoSerializationVerificationNeeded
        {

        }

        // COMMANDS
        public class Command : Message, SelectionHandler.IHasFailureMessage
        {
            private readonly CommandFailed _failureMessage;

            public Command()
            {
                _failureMessage = new CommandFailed(this);
            }

            public CommandFailed FailureMessage
            {
                get { return _failureMessage; }
            }

            object SelectionHandler.IHasFailureMessage.FailureMessage
            {
                get { return _failureMessage; }
            }
        }

        /// <summary>
        /// The Connect message is sent to the TCP manager actor, which is obtained via
        /// <see cref="TcpExt.Manager" />. Either the manager replies with a <see cref="CommandFailed" />
        /// or the actor handling the new connection replies with a <see cref="Connected" />
        /// message.
        /// </summary>
        public class Connect : Command
        {
            public Connect(EndPoint remoteAddress,
                EndPoint localAddress = null,
                IEnumerable<Inet.SocketOption> options = null,
                TimeSpan? timeout = null,
                bool pullMode = false)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
                Options = options ?? Enumerable.Empty<Inet.SocketOption>();
                Timeout = timeout;
                PullMode = pullMode;
            }

            public EndPoint RemoteAddress { get; private set; }
            public EndPoint LocalAddress { get; private set; }
            public IEnumerable<Inet.SocketOption> Options { get; private set; }
            public TimeSpan? Timeout { get; private set; }
            public bool PullMode { get; private set; }
        }

        /// <summary>
        /// The Bind message is send to the TCP manager actor, which is obtained via
        /// <see cref="TcpExt.Manager" /> in order to bind to a listening socket. The manager
        /// replies either with a <see cref="CommandFailed" /> or the actor handling the listen
        /// socket replies with a <see cref="Bound" /> message. If the local port is set to 0 in
        /// the Bind message, then the <see cref="Bound" /> message should be inspected to find
        /// the actual port which was bound to.
        /// </summary>
        public class Bind : Command
        {
            public Bind(IActorRef handler,
                EndPoint localAddress,
                int backlog = 100,
                IEnumerable<Inet.SocketOption> options = null,
                bool pullMode = false)
            {
                Handler = handler;
                LocalAddress = localAddress;
                Backlog = backlog;
                Options = options;
                PullMode = pullMode;
            }

            public IActorRef Handler { get; set; }
            public EndPoint LocalAddress { get; set; }
            public int Backlog { get; set; }
            public IEnumerable<Inet.SocketOption> Options { get; set; }
            public bool PullMode { get; set; }
        }

        /// <summary>
        /// This message must be sent to a TCP connection actor after receiving the
        /// <see cref="Connected" /> message. The connection will not read any data from the
        /// socket until this message is received, because this message defines the
        /// actor which will receive all inbound data.
        /// </summary>
        public class Register : Command
        {
            public Register(IActorRef handler, bool keepOpenonPeerClosed = false, bool useResumeWriting = true)
            {
                Handler = handler;
                KeepOpenonPeerClosed = keepOpenonPeerClosed;
                UseResumeWriting = useResumeWriting;
            }

            public IActorRef Handler { get; private set; }
            public bool KeepOpenonPeerClosed { get; private set; }
            public bool UseResumeWriting { get; private set; }
        }

        /// <summary>
        /// In order to close down a listening socket, send this message to that socket’s
        /// actor (that is the actor which previously had sent the <see cref="Bound" /> message). The
        /// listener socket actor will reply with a <see cref="Unbound" /> message.
        /// </summary>
        public class Unbind : Command
        {
            public static readonly Unbind Instance = new Unbind();

            private Unbind()
            { }
        }

        /// <summary>
        /// Common interface for all commands which aim to close down an open connection.
        /// </summary>
        public abstract class CloseCommand : Command
        {
            public abstract ConnectionClosed Event { get; }
        }

        /// <summary>
        /// A normal close operation will first flush pending writes and then close the
        /// socket. The sender of this command and the registered handler for incoming
        /// data will both be notified once the socket is closed using a <see cref="Closed" />
        /// message.
        /// </summary>
        public class Close : CloseCommand
        {
            public static readonly Close Instance = new Close();

            private Close()
            {
            }

            public override ConnectionClosed Event
            {
                get { return Closed.Instance; }
            }
        }

        /// <summary>
        /// A confirmed close operation will flush pending writes and half-close the
        /// connection, waiting for the peer to close the other half. The sender of this
        /// command and the registered handler for incoming data will both be notified
        /// once the socket is closed using a <see cref="ConfirmedClosed" /> message.
        /// </summary>
        public class ConfirmedClose : CloseCommand
        {
            public static readonly ConfirmedClose Instance = new ConfirmedClose();

            private ConfirmedClose()
            {
            }

            public override ConnectionClosed Event
            {
                get { return ConfirmedClosed.Instance; }
            }
        }

        /// <summary>
        /// An abort operation will not flush pending writes and will issue a TCP ABORT
        /// command to the O/S kernel which should result in a TCP_RST packet being sent
        /// to the peer. The sender of this command and the registered handler for
        /// incoming data will both be notified once the socket is closed using a
        /// <see cref="Aborted" /> message.
        /// </summary>
        public class Abort : CloseCommand
        {
            public static readonly Abort Instance = new Abort();

            private Abort()
            {
            }

            public override ConnectionClosed Event
            {
                get { return Aborted.Instance; }
            }
        }

        /// <summary>
        /// Each <see cref="WriteCommand" /> can optionally request a positive acknowledgment to be sent
        /// to the commanding actor. If such notification is not desired the <see cref="WriteCommand#ack" />
        /// must be set to an instance of this class. The token contained within can be used
        /// to recognize which write failed when receiving a <see cref="CommandFailed" /> message.
        /// </summary>
        public class NoAck : Event
        {
            public static readonly NoAck Instance = new NoAck(null);

            public NoAck(object token)
            {
                Token = token;
            }

            public object Token { get; private set; }
        }

        public abstract class WriteCommand : Command
        {
            public CompoundWrite Prepend(SimpleWriteCommand other)
            {
                return new CompoundWrite(other, this);
            }

            public WriteCommand Prepend(IEnumerable<WriteCommand> writes)
            {
                return writes.Reverse().Aggregate(this, (b, a) =>
                {
                    var simple = a as SimpleWriteCommand;
                    if (simple != null)
                        return b.Prepend(simple);

                    var compound = a as CompoundWrite;
                    if (compound != null)
                        return b.Prepend(compound);

                    throw new Exception();
                });
            }

            public static WriteCommand Create(IEnumerable<WriteCommand> writes)
            {
                return Write.Empty.Prepend(writes);
            }

            public static WriteCommand Create(params WriteCommand[] writes)
            {
                return Create((IEnumerable<WriteCommand>) writes);
            }
        }

        public abstract class SimpleWriteCommand : WriteCommand
        {
            public abstract Event Ack { get; }

            public bool WantsAck
            {
                get { return !(Ack is NoAck); }
            }

            public CompoundWrite Append(WriteCommand that)
            {
                return that.Prepend(this);
            }
        }

        /// <summary>
        /// Write data to the TCP connection. If no ack is needed use the special
        /// `NoAck` object. The connection actor will reply with a <see cref="CommandFailed" />
        /// message if the write could not be enqueued. If <see cref="WriteCommand#wantsAck" />
        /// returns true, the connection actor will reply with the supplied <see cref="WriteCommand#ack" />
        /// token once the write has been successfully enqueued to the O/S kernel.
        /// <b>Note that this does not in any way guarantee that the data will be
        /// or have been sent!</b> Unfortunately there is no way to determine whether
        /// a particular write has been sent by the O/S.
        /// </summary>
        public class Write : SimpleWriteCommand
        {
            private readonly Event _ack;
            public ByteString Data { get; private set; }

            public override Event Ack
            {
                get { return _ack; }
            }

            public Write(ByteString data, Event ack)
            {
                _ack = ack;
                Data = data;
            }

            public static Write Create(ByteString data)
            {
                return data.IsEmpty ? Empty : new Write(data, NoAck.Instance);
            }

            public static Write Create(ByteString data, Event ack)
            {
                return new Write(data, ack);
            }

            public static readonly Write Empty = new Write(ByteString.Empty, NoAck.Instance);
        }

        /// <summary>
        /// A write command which aggregates two other write commands. Using this construct
        /// you can chain a number of <see cref="Write" /> and/or [[WriteFile]] commands together in a way
        /// that allows them to be handled as a single write which gets written out to the
        /// network as quickly as possible.
        /// If the sub commands contain `ack` requests they will be honored as soon as the
        /// respective write has been written completely.
        /// </summary>
        public class CompoundWrite : WriteCommand, IEnumerable<SimpleWriteCommand>
        {
            private readonly SimpleWriteCommand _head;
            private readonly WriteCommand _tailCommand;

            public CompoundWrite(SimpleWriteCommand head, WriteCommand tailCommand)
            {
                _head = head;
                _tailCommand = tailCommand;
            }

            public IEnumerator<SimpleWriteCommand> GetEnumerator()
            {
                return Enumerable().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private IEnumerable<SimpleWriteCommand> Enumerable()
            {
                WriteCommand current = this;
                while (current != null)
                {
                    var compound = current as CompoundWrite;
                    if (compound != null)
                    {
                        current = compound.TailCommand;
                        yield return compound.Head;
                    }

                    var simple = current as SimpleWriteCommand;
                    if (simple != null)
                    {
                        current = null;
                        yield return simple;
                    }
                }
            }

            public SimpleWriteCommand Head
            {
                get { return _head; }
            }

            public WriteCommand TailCommand
            {
                get { return _tailCommand; }
            }
        }

        /// <summary>
        /// When `useResumeWriting` is in effect as was indicated in the <see cref="Register" /> message
        /// then this command needs to be sent to the connection actor in order to re-enable
        /// writing after a <see cref="CommandFailed" /> event. All <see cref="WriteCommand" /> processed by the
        /// connection actor between the first <see cref="CommandFailed" /> and subsequent reception of
        /// this message will also be rejected with <see cref="CommandFailed" />.
        /// </summary>
        public class ResumeWriting : Command
        {
            public static readonly ResumeWriting Instance = new ResumeWriting();
        }

        /// <summary>
        /// Sending this command to the connection actor will disable reading from the TCP
        /// socket. TCP flow-control will then propagate backpressure to the sender side
        /// as buffers fill up on either end. To re-enable reading send <see cref="ResumeReading" />.
        /// </summary>
        public class SuspendReading : Command
        {
            public static SuspendReading Instance = new SuspendReading();

            private SuspendReading()
            {
            }
        }

        /// <summary>
        /// This command needs to be sent to the connection actor after a <see cref="SuspendReading" />
        /// command in order to resume reading from the socket.
        /// </summary>
        public class ResumeReading : Command
        {
            public static ResumeReading Instance = new ResumeReading();

            private ResumeReading()
            {
            }
        }

        /// <summary>
        /// This message enables the accepting of the next connection if read throttling is enabled
        /// for connection actors.
        /// </summary>
        public class ResumeAccepting : Command
        {
            public int BatchSize { get; private set; }

            public ResumeAccepting(int batchSize)
            {
                BatchSize = batchSize;
            }
        }

        // EVENTS

        /// <summary>
        /// Common interface for all events generated by the TCP layer actors.
        /// </summary>
        public class Event : Message
        {

        }

        /// <summary>
        /// Whenever data are read from a socket they will be transferred within this
        /// class to the handler actor which was designated in the <see cref="Register" /> message.
        /// </summary>
        public sealed class Received
        {
            public Received(ByteString data)
            {
                Data = data;
            }

            public ByteString Data { get; private set; }
        }

        /// <summary>
        /// The connection actor sends this message either to the sender of a <see cref="Connect" />
        /// command (for outbound) or to the handler for incoming connections designated
        /// in the <see cref="Bind" /> message. The connection is characterized by the `remoteAddress`
        /// and `localAddress` TCP endpoints.
        /// </summary>
        public sealed class Connected
        {
            public Connected(EndPoint remoteAddress, EndPoint localAddress)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
            }

            public EndPoint RemoteAddress { get; private set; }
            public EndPoint LocalAddress { get; private set; }
        }

        /// <summary>
        /// Whenever a command cannot be completed, the queried actor will reply with
        /// this message, wrapping the original command which failed.
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
        /// When `useResumeWriting` is in effect as indicated in the <see cref="Register" /> message,
        /// the <see cref="ResumeWriting" /> command will be acknowledged by this message type, upon
        /// which it is safe to send at least one write. This means that all writes preceding
        /// the first <see cref="CommandFailed" /> message have been enqueued to the O/S kernel at this
        /// point.
        /// </summary>
        public class WritingResumed : Event
        {
            public static WritingResumed Instance = new WritingResumed();
        }

        /// <summary>
        /// The sender of a <see cref="Bind" /> command will—in case of success—receive confirmation
        /// in this form. If the bind address indicated a 0 port number, then the contained
        /// `localAddress` can be used to find out which port was automatically assigned.
        /// </summary>
        public class Bound : Event
        {
            public EndPoint LocalAddress { get; private set; }

            public Bound(EndPoint localAddress)
            {
                LocalAddress = localAddress;
            }
        }

        /// <summary>
        /// The sender of an <see cref="Unbind" /> command will receive confirmation through this
        /// message once the listening socket has been closed.
        /// </summary>
        public class Unbound : Event
        {
            public static Unbound Instance = new Unbound();
        }

        /// <summary>
        /// This is the common interface for all events which indicate that a connection
        /// has been closed or half-closed.
        /// </summary>
        public class ConnectionClosed : Event
        {
            public virtual bool IsAborted
            {
                get { return false; }
            }

            public virtual bool IsConfirmed
            {
                get { return false; }
            }

            public virtual bool IsPeerClosed
            {
                get { return false; }
            }

            public virtual bool IsErrorClosed
            {
                get { return false; }
            }

            public virtual string GetErrorCause()
            {
                return null;
            }
        }

        /// <summary>
        /// The connection has been closed normally in response to a <see cref="Close" /> command.
        /// </summary>
        public class Closed : ConnectionClosed
        {
            public static readonly Closed Instance = new Closed();

            private Closed()
            {
            }
        }

        /// <summary>
        /// The connection has been aborted in response to an <see cref="Abort" /> command.
        /// </summary>
        public class Aborted : ConnectionClosed
        {
            public static Aborted Instance = new Aborted();

            private Aborted()
            {
            }

            public override bool IsAborted
            {
                get { return true; }
            }
        }

        /// <summary>
        /// The connection has been half-closed by us and then half-close by the peer
        /// in response to a <see cref="ConfirmedClose" /> command.
        /// </summary>
        public class ConfirmedClosed : ConnectionClosed
        {
            public static ConfirmedClosed Instance = new ConfirmedClosed();

            private ConfirmedClosed()
            {
            }

            public override bool IsConfirmed
            {
                get { return true; }
            }
        }

        /// <summary>
        /// The peer has closed its writing half of the connection.
        /// </summary>
        public class PeerClosed : ConnectionClosed
        {
            public static PeerClosed Instance = new PeerClosed();

            private PeerClosed()
            {
            }

            public override bool IsPeerClosed
            {
                get { return true; }
            }
        }

        /// <summary>
        /// The connection has been closed due to an IO error.
        /// </summary>
        public class ErrorClosed : ConnectionClosed
        {
            private readonly string _cause;

            public ErrorClosed(string cause)
            {
                _cause = cause;
            }

            public override bool IsErrorClosed
            {
                get { return true; }
            }

            public override string GetErrorCause()
            {
                return _cause;
            }
        }
    }

    public class TcpExt : Extension
    {
        private readonly TcpSettings _settings;
        private readonly IActorRef _manager;
        private readonly IBufferPool _bufferPool;

        public class TcpSettings : SelectionHandlerSettings
        {
            public TcpSettings(Config config)
                : base(config)
            {
                //TODO: requiring, check defaults
                NrOfSelectors = config.GetInt("nr-of-selectors", 1);
                BatchAcceptLimit = config.GetInt("batch-accept-limit");
                DirectBufferSize = config.GetInt("direct-buffer-size");
                MaxDirectBufferPoolSize = config.GetInt("direct-buffer-pool-limit");
                RegisterTimeout = config.GetTimeSpan("register-timeout");
                ReceivedMessageSizeLimit = config.GetString("max-received-message-size") == "unlimited"
                    ? int.MaxValue
                    : config.GetInt("max-received-message-size");
                ManagementDispatcher = config.GetString("management-dispatcher");
                MaxChannelsPerSelector = MaxChannels == -1 ? -1 : Math.Max(MaxChannels/NrOfSelectors, 1);
                FinishConnectRetries = config.GetInt("finish-connect-retries", 3);
            }

            public int NrOfSelectors { get; private set; }
            public int BatchAcceptLimit { get; private set; }
            public int DirectBufferSize { get; private set; }
            public int MaxDirectBufferPoolSize { get; private set; }
            public TimeSpan? RegisterTimeout { get; private set; }
            public int ReceivedMessageSizeLimit { get; private set; }
            public string ManagementDispatcher { get; private set; }
            public int FinishConnectRetries { get; private set; }
        }

        public TcpExt(ExtendedActorSystem system)
        {
            _settings = new TcpSettings(system.Settings.Config.GetConfig("akka.io.tcp"));
            _bufferPool = new DirectByteBufferPool(_settings.DirectBufferSize, _settings.MaxDirectBufferPoolSize);
            _manager = system.SystemActorOf(
                props: Props.Create(() => new TcpManager(this))
                                            .WithDispatcher(_settings.ManagementDispatcher)
                                            .WithDeploy(Deploy.Local),
                name: "IO-TCP");
        }

        public override IActorRef Manager
        {
            get { return _manager; }
        }

        public IActorRef GetManager()
        {
            return _manager;
        }

        public TcpSettings Settings
        {
            get { return _settings; }
        }

        internal IBufferPool BufferPool
        {
            get { return _bufferPool; }
        }
    }

    public class TcpMessage
    {
        public static Tcp.Command Connect(EndPoint remoteAddress,
            EndPoint localAddress,
            IEnumerable<Inet.SocketOption> options,
            TimeSpan? timeout,
            bool pullMode)
        {
            return new Tcp.Connect(remoteAddress, localAddress, options, timeout, pullMode);
        }

        public static Tcp.Command Connect(EndPoint remoteAddress)
        {
            return Connect(remoteAddress, null, null, null, false);
        }

        public static Tcp.Command Bind(IActorRef handler,
            EndPoint endpoint,
            int backlog,
            IEnumerable<Inet.SocketOption> options,
            bool pullMode)
        {
            return new Tcp.Bind(handler, endpoint, backlog, options, pullMode);
        }

        public static Tcp.Command Bind(IActorRef handler, EndPoint endpoint, int backlog)
        {
            return new Tcp.Bind(handler, endpoint, backlog);
        }

        public static Tcp.Command Register(IActorRef handler, bool keepOpenOnPeerClosed = false,
            bool useResumeWriting = true)
        {
            return new Tcp.Register(handler, keepOpenOnPeerClosed, useResumeWriting);
        }

        public static Tcp.Command Unbind()
        {
            return Tcp.Unbind.Instance;
        }

        public static Tcp.Command Close()
        {
            return Tcp.Close.Instance;
        }

        public static Tcp.Command ConfirmedClose()
        {
            return Tcp.ConfirmedClose.Instance;
        }

        public static Tcp.Command Abort()
        {
            return Tcp.Abort.Instance;
        }

        public static Tcp.NoAck NoAck(object token = null)
        {
            return new Tcp.NoAck(token);
        }

        public static Tcp.Command Write(ByteString data, Tcp.Event ack = null)
        {
            return new Tcp.Write(data, ack);
        }

        public static Tcp.Command ResumeWriting()
        {
            return Tcp.ResumeWriting.Instance;
        }

        public static Tcp.Command SuspendReading()
        {
            return Tcp.SuspendReading.Instance;
        }

        public static Tcp.Command ResumeReading()
        {
            return Tcp.ResumeReading.Instance;
        }

        public static Tcp.Command ResumeAccepting(int batchSize)
        {
            return new Tcp.ResumeAccepting(batchSize);
        }
    }
}
