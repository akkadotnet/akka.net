//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.IO.Buffers;

namespace Akka.IO
{
    /// <summary>
    /// The set of TCP capabilities for Akka.IO are exposed via this extension.
    /// </summary>
    public class Tcp : ExtensionIdProvider<TcpExt>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Tcp Instance = new Tcp();

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
        public override TcpExt CreateExtension(ExtendedActorSystem system)
        {
            return new TcpExt(system);
        }

        #region internal connection messages
        
        internal abstract class SocketCompleted { }

        internal sealed class SocketSent : SocketCompleted
        {
            public static readonly SocketSent Instance = new SocketSent();
            private SocketSent() { }
        }

        internal sealed class SocketReceived : SocketCompleted
        {
            public static readonly SocketReceived Instance = new SocketReceived();
            private SocketReceived() { }
        }

        internal sealed class SocketAccepted : SocketCompleted
        {
            public static readonly SocketAccepted Instance = new SocketAccepted();
            private SocketAccepted() { }
        }

        internal sealed class SocketConnected : SocketCompleted
        {
            public static readonly SocketConnected Instance = new SocketConnected();
            private SocketConnected() { }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public class Message : INoSerializationVerificationNeeded { }

        #region user commands
        
        // COMMANDS
        /// <summary>
        /// TBD
        /// </summary>
        public abstract class Command : Message
        {
            private readonly CommandFailed _failureMessage;

            /// <summary>
            /// TBD
            /// </summary>
            protected Command()
            {
                _failureMessage = new CommandFailed(this);
            }

            /// <summary>
            /// TBD
            /// </summary>
            public CommandFailed FailureMessage => _failureMessage;
        }

        /// <summary>
        /// The Connect message is sent to the TCP manager actor, which is obtained via
        /// <see cref="TcpExt.Manager" />. Either the manager replies with a <see cref="CommandFailed" />
        /// or the actor handling the new connection replies with a <see cref="Connected" />
        /// message.
        /// </summary>
        public class Connect : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="localAddress">TBD</param>
            /// <param name="options">TBD</param>
            /// <param name="timeout">TBD</param>
            /// <param name="pullMode">TBD</param>
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
            /// <summary>
            /// TBD
            /// </summary>
            public TimeSpan? Timeout { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool PullMode { get; }

            public override string ToString() =>
                $"Connect(remote: {RemoteAddress}, local: {LocalAddress}, timeout: {Timeout}, pullMode: {PullMode})";
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
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handler">TBD</param>
            /// <param name="localAddress">TBD</param>
            /// <param name="backlog">TBD</param>
            /// <param name="options">TBD</param>
            /// <param name="pullMode">TBD</param>
            public Bind(IActorRef handler,
                EndPoint localAddress,
                int backlog = 100,
                IEnumerable<Inet.SocketOption> options = null,
                bool pullMode = false)
            {
                Handler = handler;
                LocalAddress = localAddress;
                Backlog = backlog;
                Options = options ?? Enumerable.Empty<Inet.SocketOption>();
                PullMode = pullMode;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Handler { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint LocalAddress { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public int Backlog { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Inet.SocketOption> Options { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool PullMode { get; }

            public override string ToString() =>
                $"Bind(addr: {LocalAddress}, handler: {Handler}, backlog: {Backlog}, pullMode: {PullMode})";
        }

        /// <summary>
        /// This message must be sent to a TCP connection actor after receiving the
        /// <see cref="Connected" /> message. The connection will not read any data from the
        /// socket until this message is received, because this message defines the
        /// actor which will receive all inbound data.
        /// </summary>
        public class Register : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="handler">TBD</param>
            /// <param name="keepOpenOnPeerClosed">TBD</param>
            /// <param name="useResumeWriting">TBD</param>
            public Register(IActorRef handler, bool keepOpenOnPeerClosed = false, bool useResumeWriting = true)
            {
                Handler = handler;
                KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
                UseResumeWriting = useResumeWriting;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Handler { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool KeepOpenOnPeerClosed { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool UseResumeWriting { get; }

            public override string ToString() =>
                $"Register(handler: {Handler}, keepOpenOnPeerClosed: {KeepOpenOnPeerClosed}, resumeWriting: {UseResumeWriting})";
        }

        /// <summary>
        /// In order to close down a listening socket, send this message to that socket’s
        /// actor (that is the actor which previously had sent the <see cref="Bound" /> message). The
        /// listener socket actor will reply with a <see cref="Unbound" /> message.
        /// </summary>
        public class Unbind : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Unbind Instance = new Unbind();

            private Unbind()
            { }
        }

        /// <summary>
        /// Common interface for all commands which aim to close down an open connection.
        /// </summary>
        public abstract class CloseCommand : Command, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Close Instance = new Close();

            private Close()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override ConnectionClosed Event => Closed.Instance;
        }

        /// <summary>
        /// A confirmed close operation will flush pending writes and half-close the
        /// connection, waiting for the peer to close the other half. The sender of this
        /// command and the registered handler for incoming data will both be notified
        /// once the socket is closed using a <see cref="ConfirmedClosed" /> message.
        /// </summary>
        public class ConfirmedClose : CloseCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ConfirmedClose Instance = new ConfirmedClose();

            private ConfirmedClose()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override ConnectionClosed Event => ConfirmedClosed.Instance;
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
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Abort Instance = new Abort();

            private Abort()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override ConnectionClosed Event => Aborted.Instance;
        }

        /// <summary>
        /// Each <see cref="WriteCommand" /> can optionally request a positive acknowledgment to be sent
        /// to the commanding actor. If such notification is not desired the <see cref="Write.Ack" />
        /// must be set to an instance of this class. The token contained within can be used
        /// to recognize which write failed when receiving a <see cref="CommandFailed" /> message.
        /// </summary>
        public class NoAck : Event
        {
            /// <summary>
            /// TBD
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

            public override string ToString() =>
                $"NoAck({Token})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class WriteCommand : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="other">TBD</param>
            /// <returns>TBD</returns>
            public CompoundWrite Prepend(SimpleWriteCommand other)
            {
                return new CompoundWrite(other, this);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="writes">TBD</param>
            /// <returns>TBD</returns>
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

                    throw new ArgumentException("The supplied WriteCommand is invalid. Only SimpleWriteCommand and CompoundWrite WriteCommands are supported.");
                });
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="writes">TBD</param>
            /// <returns>TBD</returns>
            public static WriteCommand Create(IEnumerable<WriteCommand> writes)
            {
                return Write.Empty.Prepend(writes);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="writes">TBD</param>
            /// <returns>TBD</returns>
            public static WriteCommand Create(params WriteCommand[] writes)
            {
                return Create((IEnumerable<WriteCommand>)writes);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class SimpleWriteCommand : WriteCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public abstract Event Ack { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool WantsAck => !(Ack is NoAck);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="that">TBD</param>
            /// <returns>TBD</returns>
            public CompoundWrite Append(WriteCommand that)
            {
                return that.Prepend(this);
            }
        }

        /// <summary>
        /// Write data to the TCP connection. If no ack is needed use the special
        /// `NoAck` object. The connection actor will reply with a <see cref="CommandFailed" />
        /// message if the write could not be enqueued. If <see cref="SimpleWriteCommand.WantsAck">Write.WantsAck</see>
        /// returns true, the connection actor will reply with the supplied <see cref="Write.Ack" />
        /// token once the write has been successfully enqueued to the O/S kernel.
        /// <b>Note that this does not in any way guarantee that the data will be
        /// or have been sent!</b> Unfortunately there is no way to determine whether
        /// a particular write has been sent by the O/S.
        /// </summary>
        public class Write : SimpleWriteCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Write Empty = new Write(ByteString.Empty, NoAck.Instance);

            /// <summary>
            /// TBD
            /// </summary>
            public ByteString Data { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public override Event Ack { get; }

            private Write(ByteString data, Event ack)
            {
                Ack = ack ?? NoAck.Instance;
                Data = data;
            }

            public override string ToString() =>
                $"Write(bytes: {Data.Count}, ack: {Ack})";

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="data">TBD</param>
            /// <returns>TBD</returns>
            public static Write Create(ByteString data)
            {
                return data.IsEmpty ? Empty : new Write(data, NoAck.Instance);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="data">TBD</param>
            /// <param name="ack">TbD</param>
            /// <returns>TBD</returns>
            public static Write Create(ByteString data, Event ack)
            {
                return new Write(data, ack);
            }
        }

        /*
        /// <summary>
        /// Write `count` bytes starting at `position` from file at `filePath` to the connection.
        /// The count must be &gt; 0. The connection actor will reply with a <see cref="CommandFailed"/>
        /// message if the write could not be enqueued. If <see cref="SimpleWriteCommand.WantsAck"/>
        /// returns true, the connection actor will reply with the supplied <see cref="SimpleWriteCommand.Ack"/>
        /// token once the write has been successfully enqueued to the O/S kernel.
        /// <b>Note that this does not in any way guarantee that the data will be
        /// or have been sent!</b> Unfortunately there is no way to determine whether
        /// a particular write has been sent by the O/S.
        /// </summary>
        public class WriteFile : SimpleWriteCommand
        {
            private readonly Event _ack;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="filePath">TBD</param>
            /// <param name="position">TBD</param>
            /// <param name="count">TBD</param>
            /// <param name="ack">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            public WriteFile(string filePath, long position, long count, Event ack)
            {
                if (position < 0) throw new ArgumentException("WriteFile.position must be >= 0", nameof(position));
                if (count <= 0) throw new ArgumentException("WriteFile.count must be > 0", nameof(count));

                _ack = ack;
                FilePath = filePath;
                Position = position;
                Count = count;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string FilePath { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public long Position { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public long Count { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public override Event Ack => _ack;

            public override string ToString() =>
                $"WriteFile(path: {FilePath}, position: {Position}, count: {Count}, ack: {Ack})";
        }
        */
        /// <summary>
        /// A write command which aggregates two other write commands. Using this construct
        /// you can chain a number of <see cref="Akka.IO.Tcp.Write" /> commands together in a way
        /// that allows them to be handled as a single write which gets written out to the
        /// network as quickly as possible.
        /// If the sub commands contain `ack` requests they will be honored as soon as the
        /// respective write has been written completely.
        /// </summary>
        public class CompoundWrite : WriteCommand, IEnumerable<SimpleWriteCommand>
        {
            private readonly SimpleWriteCommand _head;
            private readonly WriteCommand _tailCommand;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="head">TBD</param>
            /// <param name="tailCommand">TBD</param>
            public CompoundWrite(SimpleWriteCommand head, WriteCommand tailCommand)
            {
                _head = head;
                _tailCommand = tailCommand;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
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

            /// <summary>
            /// TBD
            /// </summary>
            public SimpleWriteCommand Head => _head;

            /// <summary>
            /// TBD
            /// </summary>
            public WriteCommand TailCommand => _tailCommand;

            public override string ToString() =>
                $"CompoundWrite({Head}, {TailCommand})";
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
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ResumeWriting Instance = new ResumeWriting();
        }

        /// <summary>
        /// Sending this command to the connection actor will disable reading from the TCP
        /// socket. TCP flow-control will then propagate backpressure to the sender side
        /// as buffers fill up on either end. To re-enable reading send <see cref="ResumeReading" />.
        /// </summary>
        public class SuspendReading : Command
        {
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            public int BatchSize { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="batchSize">TBD</param>
            public ResumeAccepting(int batchSize)
            {
                BatchSize = batchSize;
            }

            public override string ToString() =>
                $"ResumeAccepting(batchSize: {BatchSize})";
        }

        #endregion

        #region user events
        
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

            public override string ToString() =>
                $"Received(bytes: {Data.Count})";
        }

        /// <summary>
        /// The connection actor sends this message either to the sender of a <see cref="Connect" />
        /// command (for outbound) or to the handler for incoming connections designated
        /// in the <see cref="Bind" /> message. The connection is characterized by the `remoteAddress`
        /// and `localAddress` TCP endpoints.
        /// </summary>
        public sealed class Connected : Event
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="localAddress">TBD</param>
            public Connected(EndPoint remoteAddress, EndPoint localAddress)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint RemoteAddress { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public EndPoint LocalAddress { get; }

            public override string ToString() =>
                $"Connected(local: {LocalAddress}, remote: {RemoteAddress})";
        }

        /// <summary>
        /// Whenever a command cannot be completed, the queried actor will reply with
        /// this message, wrapping the original command which failed.
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

            public override string ToString() =>
                $"CommandFailed({Cmd})";
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
            /// <summary>
            /// TBD
            /// </summary>
            public static WritingResumed Instance = new WritingResumed();
        }

        /// <summary>
        /// The sender of a <see cref="Bind" /> command will—in case of success—receive confirmation
        /// in this form. If the bind address indicated a 0 port number, then the contained
        /// `localAddress` can be used to find out which port was automatically assigned.
        /// </summary>
        public class Bound : Event
        {
            /// <summary>
            /// The local listening endpoint of the bound socket.
            /// </summary>
            public EndPoint LocalAddress { get; }

            /// <summary>
            /// Creates a new bound message.
            /// </summary>
            /// <param name="localAddress">The local listening endpoint of the bound socket.</param>
            public Bound(EndPoint localAddress)
            {
                LocalAddress = localAddress;
            }

            public override string ToString() =>
                $"Bound({LocalAddress})";
        }

        /// <summary>
        /// The sender of an <see cref="Unbind" /> command will receive confirmation through this
        /// message once the listening socket has been closed.
        /// </summary>
        public class Unbound : Event
        {
            /// <summary>
            /// Singleton instance
            /// </summary>
            public static Unbound Instance = new Unbound();
        }

        /// <summary>
        /// This is the common interface for all events which indicate that a connection
        /// has been closed or half-closed.
        /// </summary>
        public class ConnectionClosed : Event, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public virtual bool IsAborted => false;

            /// <summary>
            /// TBD
            /// </summary>
            public virtual bool IsConfirmed => false;

            /// <summary>
            /// TBD
            /// </summary>
            public virtual bool IsPeerClosed => false;

            /// <summary>
            /// TBD
            /// </summary>
            public virtual bool IsErrorClosed => false;

            /// <summary>
            /// TBD
            /// </summary>
            public virtual string Cause => null;
        }

        /// <summary>
        /// The connection has been closed normally in response to a <see cref="Close" /> command.
        /// </summary>
        public class Closed : ConnectionClosed
        {
            /// <summary>
            /// TBD
            /// </summary>
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
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Aborted Instance = new Aborted();

            private Aborted()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override bool IsAborted => true;
        }

        /// <summary>
        /// The connection has been half-closed by us and then half-close by the peer
        /// in response to a <see cref="ConfirmedClose" /> command.
        /// </summary>
        public class ConfirmedClosed : ConnectionClosed
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ConfirmedClosed Instance = new ConfirmedClosed();

            private ConfirmedClosed()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override bool IsConfirmed => true;
        }

        /// <summary>
        /// The peer has closed its writing half of the connection.
        /// </summary>
        public class PeerClosed : ConnectionClosed
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly PeerClosed Instance = new PeerClosed();

            private PeerClosed()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override bool IsPeerClosed => true;
        }

        /// <summary>
        /// The connection has been closed due to an IO error.
        /// </summary>
        public sealed class ErrorClosed : ConnectionClosed
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public ErrorClosed(string cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override bool IsErrorClosed => true;

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string Cause { get; }

            public override string ToString() =>
                $"ErrorClosed('{Cause}')";
        }

        #endregion

        private class ConnectionSupervisorStrategyImp : OneForOneStrategy
        {
            public ConnectionSupervisorStrategyImp()
                : base(StoppingStrategy.Decider)
            { }

            protected override void LogFailure(IActorContext context, IActorRef child, Exception cause, Directive directive)
            {
                if (cause is DeathPactException)
                {
                    try
                    {
                        context.System.EventStream.Publish(new Debug(child.Path.ToString(), GetType(), "Closed after handler termination"));
                    }
                    catch (Exception) { }
                }
                else base.LogFailure(context, child, cause, directive);
            }
        }
        public static readonly SupervisorStrategy ConnectionSupervisorStrategy = new ConnectionSupervisorStrategyImp();

    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class TcpExt : IOExtension
    {
        public TcpExt(ExtendedActorSystem system) : this(system, TcpSettings.Create(system)) { }

        internal TcpExt(ExtendedActorSystem system, TcpSettings settings)
        {
            var bufferPoolConfig = system.Settings.Config.GetConfig(settings.BufferPoolConfigPath);

            if (bufferPoolConfig.IsNullOrEmpty())
                throw new ConfigurationException($"Cannot retrieve TCP buffer pool configuration: {settings.BufferPoolConfigPath} configuration node not found");

            Settings = settings;
            FileIoDispatcher = system.Dispatchers.Lookup(Settings.FileIODispatcher);
            BufferPool = CreateBufferPool(system, bufferPoolConfig);
            Manager = system.SystemActorOf(
                props: Props.Create(() => new TcpManager(this)).WithDispatcher(Settings.ManagementDispatcher).WithDeploy(Deploy.Local),
                name: "IO-TCP");
        }

        /// <summary>
        /// Gets reference to a TCP manager actor.
        /// </summary>
        public override IActorRef Manager { get; }

        /// <summary>
        /// A buffer pool used by current plugin.
        /// </summary>
        public IBufferPool BufferPool { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TcpSettings Settings { get; }

        /// <summary>
        /// TBD
        /// </summary>
        internal MessageDispatcher FileIoDispatcher { get; }

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
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class TcpMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="pullMode">TDB</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Connect(EndPoint remoteAddress,
            EndPoint localAddress,
            IEnumerable<Inet.SocketOption> options,
            TimeSpan? timeout,
            bool pullMode)
        {
            return new Tcp.Connect(remoteAddress, localAddress, options, timeout, pullMode);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Connect(EndPoint remoteAddress)
        {
            return Connect(remoteAddress, null, null, null, false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="endpoint">TBD</param>
        /// <param name="backlog">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="pullMode">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Bind(IActorRef handler,
            EndPoint endpoint,
            int backlog,
            IEnumerable<Inet.SocketOption> options,
            bool pullMode)
        {
            return new Tcp.Bind(handler, endpoint, backlog, options, pullMode);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="endpoint">TBD</param>
        /// <param name="backlog">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Bind(IActorRef handler, EndPoint endpoint, int backlog)
        {
            return new Tcp.Bind(handler, endpoint, backlog);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="keepOpenOnPeerClosed">TBD</param>
        /// <param name="useResumeWriting">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Register(IActorRef handler, bool keepOpenOnPeerClosed = false,
            bool useResumeWriting = true)
        {
            return new Tcp.Register(handler, keepOpenOnPeerClosed, useResumeWriting);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command Unbind()
        {
            return Tcp.Unbind.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command Close()
        {
            return Tcp.Close.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command ConfirmedClose()
        {
            return Tcp.ConfirmedClose.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command Abort()
        {
            return Tcp.Abort.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="token">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.NoAck NoAck(object token = null)
        {
            return new Tcp.NoAck(token);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="data">TBD</param>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command Write(ByteString data, Tcp.Event ack = null)
        {
            return Tcp.Write.Create(data, ack);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command ResumeWriting()
        {
            return Tcp.ResumeWriting.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command SuspendReading()
        {
            return Tcp.SuspendReading.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Tcp.Command ResumeReading()
        {
            return Tcp.ResumeReading.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="batchSize">TBD</param>
        /// <returns>TBD</returns>
        public static Tcp.Command ResumeAccepting(int batchSize)
        {
            return new Tcp.ResumeAccepting(batchSize);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class TcpExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef Tcp(this ActorSystem system)
        {
            return IO.Tcp.Manager(system);
        }
    }
}
