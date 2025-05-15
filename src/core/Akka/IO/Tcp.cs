//-----------------------------------------------------------------------
// <copyright file="Tcp.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.IO.Buffers;
using Akka.Util;

namespace Akka.IO
{
    /// <summary>
    /// The set of TCP capabilities for Akka.IO are exposed via this extension.
    /// </summary>
    public sealed class Tcp : ExtensionIdProvider<TcpExt>
    {
        // TODO: refactor this in v1.6 to use a `.For` method with the correct ExtensionId provider setup
        private static readonly Tcp PluginInstance = new();
        
        /// <summary>
        /// Fetches the TCP manager actor for the given actor system.
        /// </summary>
        public static IActorRef Manager(ActorSystem system)
        {
            return PluginInstance.Apply(system).Manager;
        }
        
        internal static TcpExt For(ActorSystem system)
        {
            return PluginInstance.Apply(system);
        }
        
        public override TcpExt CreateExtension(ExtendedActorSystem system)
        {
            return new TcpExt(system);
        }

        #region internal connection messages
        
        internal abstract class SocketCompleted : INoSerializationVerificationNeeded, IDeadLetterSuppression 
        { }
        
        internal sealed class SocketConnected : SocketCompleted
        {
            public static readonly SocketConnected Instance = new();
            private SocketConnected() { }
        }

        #endregion
        

        /// <summary>
        /// Akka.IO Tcp messages are all derived from this class.
        /// </summary>
        public class Message : INoSerializationVerificationNeeded { }

        #region user commands
        
        /// <summary>
        /// Queries against Akka.IO.Tcp class types
        /// </summary>
        public interface ITcpQuery : INoSerializationVerificationNeeded{}

        /// <summary>
        /// Subscribe to receive ongoing statistics from a TCP listener. See <see cref="TcpListenerStatistics" />
        /// </summary>
        public sealed record SubscribeToTcpListenerStats(IActorRef Subscriber) : ITcpQuery;
        
        /// <summary>
        /// Unsubscribe from receiving ongoing statistics from a TCP listener.
        /// </summary>
        public sealed record UnsubscribeFromTcpListenerStats(IActorRef Subscriber) : ITcpQuery;
        
        /// <summary>
        /// A set of statistics from a specific TCP listener.
        /// </summary>
        /// <remarks>
        /// These are ongoing, rolling statistics that are updated as the listener
        /// processes incoming connections. They will not reset unless the listener is killed.
        /// </remarks>
        public sealed record TcpListenerStatistics : ITcpQuery
        {
            /// <summary>
            /// Total number of accepted incoming connections
            /// </summary>
            public long AcceptedIncomingConnections { get; init; }
            
            /// <summary>
            /// Incoming connections that could not be accepted
            /// </summary>
            public long FailedIncomingConnections { get; init; }
            
            /// <summary>
            /// Incoming connections that had to be retried
            /// </summary>
            public long RetriedIncomingConnections { get; init; }
            
            /// <summary>
            /// Total number of incoming connections that were closed
            /// </summary>
            public long IncomingConnectionsClosed { get; init; }
        }

        // COMMANDS
        
        /// <summary>
        /// All Akka.IO.Tcp commands inherit from this class.
        /// </summary>
        public abstract class Command : Message
        {
            /// <summary>
            /// A predefined failure message which can be used to indicate that a command
            /// failed during processing.
            /// </summary>
            public CommandFailed FailureMessage => new CommandFailed(this);
        }

        /// <summary>
        /// The Connect message is sent to the TCP manager actor, which is obtained via
        /// <see cref="TcpExt.Manager" />. Either the manager replies with a <see cref="CommandFailed" />
        /// or the actor handling the new connection replies with a <see cref="Connected" />
        /// message.
        /// </summary>
        public sealed class Connect : Command
        {
            
            /// <summary>
            /// Connect to a remote TCP endpoint.
            /// </summary>
            /// <param name="remoteAddress">The remote endpoint</param>
            /// <param name="localAddress">An optional local endpoint address to bind to. Most users don't specify this.</param>
            /// <param name="options">A set of socket options.</param>
            /// <param name="timeout">An optional connect timeout. Will result in a <see cref="Tcp.CommandFailed"/> message being returned if we exceed this value.</param>
            /// <param name="pullMode">Specifies whether we're running in "pull mode" or not.</param>
            public Connect(EndPoint remoteAddress,
                EndPoint localAddress = null,
                IEnumerable<Inet.SocketOption> options = null,
                TimeSpan? timeout = null,
                bool pullMode = false)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
                Options = options ?? [];
                Timeout = timeout;
                PullMode = pullMode;
            }
            
            public EndPoint RemoteAddress { get; }
            
            public EndPoint LocalAddress { get; }
    
            public IEnumerable<Inet.SocketOption> Options { get; }
            
            public TimeSpan? Timeout { get; }
            public bool PullMode { get; }
            
            /// <summary>
            /// Optional - allows you to specify TCP settings for the connection.
            ///
            /// Otherwise, the system defaults will be used.
            /// </summary>
            /// <example>
            /// var tcpSettings = TcpSettings.Create(ActorSystem);
            /// var tcpSettingsWithDifferentBufferSizes = tcpSettings with { SendBufferSize = 8192, ReceiveBufferSize = 8192 };
            /// </example>
            public TcpSettings? TcpSettings { get; set; }

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
            /// Bind a TCP listener to a local endpoint.
            /// </summary>
            /// <param name="handler">The actor who will be handling the TCP listener.</param>
            /// <param name="localAddress">The local endpoint we are binding to.</param>
            /// <param name="backlog">TCP backlog - the number of pending connections that the queue will hold. Defaults to 1024.</param>
            /// <param name="options">A set of socket options.</param>
            /// <param name="pullMode">Specifies whether we're running in "pull mode" or not.</param>
            public Bind(IActorRef handler,
                EndPoint localAddress,
                int backlog = 1024,
                IEnumerable<Inet.SocketOption> options = null,
                bool pullMode = false)
            {
                Handler = handler;
                LocalAddress = localAddress;
                Backlog = backlog;
                Options = options ?? [];
                PullMode = pullMode;
            }

            public IActorRef Handler { get; }

            public EndPoint LocalAddress { get; }

            public int Backlog { get; }

            public IEnumerable<Inet.SocketOption> Options { get; }

            public bool PullMode { get; }
            
            /// <summary>
            /// Optional - allows you to specify TCP settings for the connection.
            ///
            /// Otherwise, the system defaults will be used.
            /// </summary>
            /// <example>
            /// var tcpSettings = TcpSettings.Create(ActorSystem);
            /// var tcpSettingsWithDifferentBufferSizes = tcpSettings with { SendBufferSize = 8192, ReceiveBufferSize = 8192 };
            /// </example>
            public TcpSettings? TcpSettings { get; set; }

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
            /// Registers an actor to handle an outgoing or incoming TCP connection that has been established.
            /// </summary>
            /// <param name="handler">The actor who will be handling the TCP communication.</param>
            /// <param name="keepOpenOnPeerClosed">Keep the connection open if the peer is closed</param>
            /// <param name="useResumeWriting">Use resume / pause writing semantics once buffer gets full</param>
            public Register(IActorRef handler, bool keepOpenOnPeerClosed = false, bool useResumeWriting = true)
            {
                Handler = handler;
                KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
                UseResumeWriting = useResumeWriting;
            }

    
            public IActorRef Handler { get; }
      
            public bool KeepOpenOnPeerClosed { get; }
 
            public bool UseResumeWriting { get; }

            public override string ToString() =>
                $"Register(handler: {Handler}, keepOpenOnPeerClosed: {KeepOpenOnPeerClosed}, resumeWriting: {UseResumeWriting})";
        }

        /// <summary>
        /// To close down a listening socket, send this message to that socket’s
        /// actor (that is the actor which previously had sent the <see cref="Bound" /> message). The
        /// listener socket actor will reply with a <see cref="Unbound" /> message.
        /// </summary>
        public class Unbind : Command
        {
            public static readonly Unbind Instance = new();

            private Unbind()
            { }
        }

        /// <summary>
        /// Common interface for all commands which aim to close down an open connection.
        /// </summary>
        public abstract class CloseCommand : Command, IDeadLetterSuppression
        {
            /// <summary>
            /// The event to return in response to this command
            /// </summary>
            public abstract ConnectionClosed Event { get; }
        }

        /// <summary>
        /// A normal close operation will first flush pending writes and then close the
        /// socket. The sender of this command and the registered handler for incoming
        /// data will both be notified once the socket is closed using a <see cref="Closed" />
        /// message.
        /// </summary>
        public sealed class Close : CloseCommand
        {
            public static readonly Close Instance = new();

            private Close()
            {
            }
            
            public override ConnectionClosed Event => Closed.Instance;
        }

        /// <summary>
        /// A confirmed close operation will flush pending writes and half-close the
        /// connection, waiting for the peer to close the other half. The sender of this
        /// command and the registered handler for incoming data will both be notified
        /// once the socket is closed using a <see cref="ConfirmedClosed" /> message.
        /// </summary>
        public sealed class ConfirmedClose : CloseCommand
        {
            public static readonly ConfirmedClose Instance = new();

            private ConfirmedClose()
            {
            }
            
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
            public static readonly Abort Instance = new();

            private Abort()
            {
            }
            
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
            public static readonly NoAck Instance = new(null);
            
            public NoAck(object token)
            {
                Token = token;
            }

            /// <summary>
            /// A correlation id which can be used to identify a specific write operation.
            /// </summary>
            public object Token { get; }

            public override string ToString() =>
                $"NoAck({Token})";
        }

        /// <summary>
        /// All write commands inherit from this class.
        /// </summary>
        public abstract class WriteCommand : Command
        {
            /// <summary>
            /// Prepend another write before this one.
            /// </summary>
            /// <param name="other">The other write to prepend</param>
            /// <returns>A compound write consisting of multiple byte buffers of non-contiguous memory</returns>
            public CompoundWrite Prepend(SimpleWriteCommand other)
            {
                return new CompoundWrite(other, this);
            }
            
            /// <summary>
            /// The number of bytes that will be written to the socket.
            /// </summary>
            public abstract long Bytes { get; }

            /// <summary>
            /// Prepend a group of writes before this one.
            /// </summary>
            /// <param name="writes">The set of writes that will preceed this one.</param>
            /// <returns>A compound write consisting of multiple byte buffers of non-contiguous memory</returns>
            public WriteCommand Prepend(IEnumerable<WriteCommand> writes)
            {
                return writes.Reverse().Aggregate(this, (b, a) =>
                {
                    return a switch
                    {
                        SimpleWriteCommand simple => b.Prepend(simple),
                        CompoundWrite compound => b.Prepend(compound),
                        _ => throw new ArgumentException(
                            "The supplied WriteCommand is invalid. Only SimpleWriteCommand and CompoundWrite WriteCommands are supported.")
                    };
                });
            }
            
            public static WriteCommand Create(IEnumerable<WriteCommand> writes)
            {
                return Write.Empty.Prepend(writes);
            }
            
            public static WriteCommand Create(params WriteCommand[] writes)
            {
                return Create((IEnumerable<WriteCommand>)writes);
            }
        }

        /// <summary>
        /// A non-compounded write
        /// </summary>
        public abstract class SimpleWriteCommand : WriteCommand
        {
            /// <summary>
            /// An optional acknowledgment event which will be sent to the sender of this command
            /// </summary>
            public abstract Event Ack { get; }

            /// <summary>
            /// Indicates whether this message needs to be ACK'd to the handler.
            /// </summary>
            public bool WantsAck => Ack is not NoAck;

            /// <summary>
            /// Appends a write after this one.
            /// </summary>
            /// <param name="that">The next write to append.</param>
            /// <returns>A compound write of non-contiguous memory.</returns>
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
        public sealed class Write : SimpleWriteCommand
        {
            /// <summary>
            /// Write with no data and <see cref="NoAck"/>
            /// </summary>
            public static readonly Write Empty = new(ByteString.Empty, NoAck.Instance);

            /// <summary>
            /// The data we are going to write.
            /// </summary>
            public ByteString Data { get; }

            /// <summary>
            /// The optional acknowledgment event which will be sent to the sender of this command.
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
            /// Creates a write from a <see cref="ByteString"/>
            /// </summary>
            /// <param name="data">The data to return.</param>
            public static Write Create(ByteString data)
            {
                return data.IsEmpty ? Empty : new Write(data, NoAck.Instance);
            }

            /// <summary>
            /// Creates a write from a <see cref="ByteString"/>
            /// </summary>
            /// <param name="data">The data to return.</param>
            /// <param name="ack">The acknowledgement message we're receive once this write is complete.</param>
            public static Write Create(ByteString data, Event ack)
            {
                return new Write(data, ack);
            }

            public override long Bytes => Data.Count;
        }
        
        /// <summary>
        /// A write command which aggregates two other write commands. Using this construct
        /// you can chain a number of <see cref="Akka.IO.Tcp.Write" /> commands together in a way
        /// that allows them to be handled as a single write which gets written out to the
        /// network as quickly as possible.
        /// If the sub commands contain `ack` requests they will be honored as soon as the
        /// respective write has been written completely.
        /// </summary>
        public sealed class CompoundWrite : WriteCommand, IEnumerable<SimpleWriteCommand>
        {
            public CompoundWrite(SimpleWriteCommand head, WriteCommand tailCommand)
            {
                Head = head;
                TailCommand = tailCommand;
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
                    if (current is CompoundWrite compound)
                    {
                        current = compound.TailCommand;
                        yield return compound.Head;
                    }

                    if (current is not SimpleWriteCommand simple) continue;
                    current = null;
                    yield return simple;
                }
            }
            
            public SimpleWriteCommand Head { get; }

            public WriteCommand TailCommand { get; }

            public override string ToString() =>
                $"CompoundWrite({Head}, {TailCommand})";

            public override long Bytes => Head.Bytes + TailCommand.Bytes;
        }

        /// <summary>
        /// When `useResumeWriting` is in effect as was indicated in the <see cref="Register" /> message
        /// then this command needs to be sent to the connection actor in order to re-enable
        /// writing after a <see cref="CommandFailed" /> event. All <see cref="WriteCommand" /> processed by the
        /// connection actor between the first <see cref="CommandFailed" /> and subsequent reception of
        /// this message will also be rejected with <see cref="CommandFailed" />.
        /// </summary>
        public sealed class ResumeWriting : Command
        {
            public static readonly ResumeWriting Instance = new();

            private ResumeWriting()
            {
            }
        }

        /// <summary>
        /// Sending this command to the connection actor will disable reading from the TCP
        /// socket. TCP flow-control will then propagate backpressure to the sender side
        /// as buffers fill up on either end. To re-enable reading send <see cref="ResumeReading" />.
        /// </summary>
        public sealed class SuspendReading : Command
        {
            public static readonly SuspendReading Instance = new();

            private SuspendReading()
            {
            }
        }

        /// <summary>
        /// This command needs to be sent to the connection actor after a <see cref="SuspendReading" />
        /// command in order to resume reading from the socket.
        /// </summary>
        public sealed class ResumeReading : Command
        {
            public static readonly ResumeReading Instance = new();

            private ResumeReading()
            {
            }
        }

        /// <summary>
        /// This message enables the accepting of the next connection if read throttling is enabled
        /// for connection actors.
        /// </summary>
        public sealed class ResumeAccepting : Command
        {
            /// <summary>
            /// The number of connections to accept before resuming read throttling.
            /// </summary>
            public int BatchSize { get; }
            
            public ResumeAccepting(int batchSize)
            {
                BatchSize = batchSize;
            }

            public override string ToString() =>
                $"ResumeAccepting(BatchSize: {BatchSize})";
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
            public Received(ByteString data)
            {
                Data = data;
            }
            
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
            public Connected(EndPoint remoteAddress, EndPoint localAddress)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
            }

            /// <summary>
            /// The remote endpoint of the connection.
            /// </summary>
            public EndPoint RemoteAddress { get; }
            
            /// <summary>
            /// The local endpoint of the connection.
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
            public CommandFailed(Command cmd, Option<Exception> ex)
            {
                Cmd = cmd;
                Cause = ex;
            }

            public CommandFailed(Command cmd) : this(cmd, Option<Exception>.None)
            {
            }

            /// <summary>
            /// The original command which failed.
            /// </summary>
            public Command Cmd { get; }

            /// <summary>
            /// Optionally contains the cause why the command failed.
            /// </summary>
            public Option<Exception> Cause { get; }

            /// <summary>
            /// Creates a copy of this object with a new cause set.
            /// </summary>
            [InternalApi]
            public CommandFailed WithCause(Exception cause)
            {
                // Needs to be added with a mutable property for compatibility reasons
                return new CommandFailed(Cmd, cause);
            }

            [InternalApi]
            public string CauseString => Cause.HasValue ? $" because of {Cause.Value.Message}" : string.Empty;

            public override string ToString() => $"CommandFailed({Cmd}){CauseString}";
        }

        /// <summary>
        /// When `useResumeWriting` is in effect as indicated in the <see cref="Register" /> message,
        /// the <see cref="ResumeWriting" /> command will be acknowledged by this message type, upon
        /// which it is safe to send at least one write. This means that all writes preceding
        /// the first <see cref="CommandFailed" /> message have been enqueued to the O/S kernel at this
        /// point.
        /// </summary>
        public sealed class WritingResumed : Event
        {
            public static readonly WritingResumed Instance = new();

            private WritingResumed()
            {
            }
        }

        /// <summary>
        /// The sender of a <see cref="Bind" /> command will—in case of success—receive confirmation
        /// in this form. If the bind address indicated a 0 port number, then the contained
        /// `localAddress` can be used to find out which port was automatically assigned.
        /// </summary>
        public sealed class Bound : Event
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
        public sealed class Unbound : Event
        {
            /// <summary>
            /// Singleton instance
            /// </summary>
            public static readonly Unbound Instance = new();

            private Unbound()
            {
            }
        }

        /// <summary>
        /// This is the common interface for all events which indicate that a connection
        /// has been closed or half-closed.
        /// </summary>
        public class ConnectionClosed : Event, IDeadLetterSuppression
        {
            /// <summary>
            /// Was the connection closed normally?
            /// </summary>
            public virtual bool IsAborted => false;

            /// <summary>
            /// Can we confirm that the connection was open in the first place?
            /// </summary>
            public virtual bool IsConfirmed => false;

            /// <summary>
            /// Is our remote peer closed too?
            /// </summary>
            public virtual bool IsPeerClosed => false;

            /// <summary>
            /// Did the connection close due to an IO error?
            /// </summary>
            public virtual bool IsErrorClosed => false;

            /// <summary>
            /// Was there a given cause for why the connection was closed?
            /// </summary>
            public virtual string? Cause => null;
        }

        /// <summary>
        /// The connection has been closed normally in response to a <see cref="Close" /> command.
        /// </summary>
        public sealed class Closed : ConnectionClosed
        {
            public static readonly Closed Instance = new();

            private Closed()
            {
            }
        }

        /// <summary>
        /// The connection has been aborted in response to an <see cref="Abort" /> command.
        /// </summary>
        public sealed class Aborted : ConnectionClosed
        {
            public static readonly Aborted Instance = new();

            private Aborted()
            {
            }
            
            public override bool IsAborted => true;
        }

        /// <summary>
        /// The connection has been half-closed by us and then half-close by the peer
        /// in response to a <see cref="ConfirmedClose" /> command.
        /// </summary>
        public sealed class ConfirmedClosed : ConnectionClosed
        {
            public static readonly ConfirmedClosed Instance = new();

            private ConfirmedClosed()
            {
            }
            
            public override bool IsConfirmed => true;
        }

        /// <summary>
        /// The peer has closed its writing half of the connection.
        /// </summary>
        public sealed class PeerClosed : ConnectionClosed
        {
            public static readonly PeerClosed Instance = new();

            private PeerClosed()
            {
            }
            
            public override bool IsPeerClosed => true;
        }

        /// <summary>
        /// The connection has been closed due to an IO error.
        /// </summary>
        public sealed class ErrorClosed : ConnectionClosed
        {
            public ErrorClosed(string? cause)
            {
                Cause = cause;
            }

            public override bool IsErrorClosed => true;
            
            public override string? Cause { get; }

            public override string ToString() =>
                $"ErrorClosed('{Cause}')";
        }

        #endregion

        private sealed class ConnectionSupervisorStrategyImp : OneForOneStrategy
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
    /// Akka.IO TCP extension - provides an actor-based API for TCP socket communication.
    /// </summary>
    public sealed class TcpExt : IOExtension
    {
        public TcpExt(ExtendedActorSystem system) : this(system, TcpSettings.Create(system)) { }

        internal TcpExt(ExtendedActorSystem system, TcpSettings settings)
        {
            Settings = settings;
            Manager = system.SystemActorOf(
                props: Props.Create(() => new TcpManager(this)).WithDispatcher(Settings.ManagementDispatcher).WithDeploy(Deploy.Local),
                name: "IO-TCP");
        }

        /// <summary>
        /// Gets reference to a TCP manager actor.
        /// </summary>
        public override IActorRef Manager { get; }
        
        /// <summary>
        /// The settings used by this extension.
        /// </summary>
        public TcpSettings Settings { get; }
    }

    /// <summary>
    /// Helpers for generating TCP messages.
    /// </summary>
    public static class TcpMessage
    {
        /// <summary>
        /// Connect to a remote TCP endpoint.
        /// </summary>
        /// <param name="remoteAddress">The remote endpoint</param>
        /// <param name="localAddress">An optional local endpoint address to bind to. Most users don't specify this.</param>
        /// <param name="options">A set of socket options.</param>
        /// <param name="timeout">An optional connect timeout. Will result in a <see cref="Tcp.CommandFailed"/> message being returned if we exceed this value.</param>
        /// <param name="pullMode">Specifies whether we're running in "pull mode" or not.</param>
        public static Tcp.Command Connect(EndPoint remoteAddress,
            EndPoint? localAddress,
            IEnumerable<Inet.SocketOption> options,
            TimeSpan? timeout,
            bool pullMode)
        {
            return new Tcp.Connect(remoteAddress, localAddress, options, timeout, pullMode);
        }

        /// <summary>
        /// Connect to a remote TCP endpoint.
        /// </summary>
        /// <param name="remoteAddress">The remote endpoint</param>
        public static Tcp.Command Connect(EndPoint remoteAddress)
        {
            return Connect(remoteAddress, null, [], null, false);
        }

        /// <summary>
        /// Bind a TCP listener to a local endpoint.
        /// </summary>
        /// <param name="handler">The actor who will be handling the TCP listener.</param>
        /// <param name="endpoint">The local endpoint we are binding to.</param>
        /// <param name="backlog">TCP backlog - the number of pending connections that the queue will hold.</param>
        /// <param name="options">A set of socket options.</param>
        /// <param name="pullMode">Specifies whether we're running in "pull mode" or not for all subsequent client connections.</param>
        public static Tcp.Command Bind(IActorRef handler,
            EndPoint endpoint,
            int backlog,
            IEnumerable<Inet.SocketOption> options,
            bool pullMode)
        {
            return new Tcp.Bind(handler, endpoint, backlog, options, pullMode);
        }

        /// <summary>
        /// Bind a TCP listener to a local endpoint.
        /// </summary>
        /// <param name="handler">The actor who will be handling the TCP listener.</param>
        /// <param name="endpoint">The local endpoint we are binding to.</param>
        /// <param name="backlog">TCP backlog - the number of pending connections that the queue will hold.</param>
        public static Tcp.Command Bind(IActorRef handler, EndPoint endpoint, int backlog)
        {
            return new Tcp.Bind(handler, endpoint, backlog);
        }

        /// <summary>
        /// Registers an actor to handle an outgoing or incoming TCP connection that has been established.
        /// </summary>
        /// <param name="handler">The actor who will be handling the TCP communication.</param>
        /// <param name="keepOpenOnPeerClosed">Keep the connection open if the peer is closed</param>
        /// <param name="useResumeWriting">Use resume / pause writing semantics once buffer gets full</param>
        public static Tcp.Command Register(IActorRef handler, bool keepOpenOnPeerClosed = false,
            bool useResumeWriting = true)
        {
            return new Tcp.Register(handler, keepOpenOnPeerClosed, useResumeWriting);
        }

        /// <summary>
        /// Unbinds a previously bound TCP listener.
        /// </summary>
        public static Tcp.Command Unbind()
        {
            return Tcp.Unbind.Instance;
        }

        /// <summary>
        /// Closes an open TCP connection.
        /// </summary>
        public static Tcp.Command Close()
        {
            return Tcp.Close.Instance;
        }

        /// <summary>
        /// Closes a confirmed-to-have-been-previously-running TCP connection.
        /// </summary>
        public static Tcp.Command ConfirmedClose()
        {
            return Tcp.ConfirmedClose.Instance;
        }

        /// <summary>
        /// Aborts a TCP connection without flushing pending writes.
        /// </summary>
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
    /// Convenience methods for using the Akka.IO.Tcp extension.
    /// </summary>
    public static class TcpExtensions
    {
        /// <summary>
        /// Returns the <see cref="ActorSystem"/>-specific <see cref="Tcp"/> instance for TCP connectivity.
        /// </summary>
        /// <param name="system">The current actor system.</param>
        public static IActorRef Tcp(this ActorSystem system)
        {
            return IO.Tcp.Manager(system);
        }
    }
}
