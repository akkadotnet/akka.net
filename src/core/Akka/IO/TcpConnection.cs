//-----------------------------------------------------------------------
// <copyright file="TcpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Util.Internal;

namespace Akka.IO
{
    using static Akka.IO.Tcp;
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// INTERNAL API: Base class for TcpIncomingConnection and TcpOutgoingConnection.
    /// 
    /// TcpConnection is an actor abstraction over single connection between TCP server and client. 
    /// Since actors are processing messages in synchronous fashion, they are way to provide thread 
    /// safety over sockets and <see cref="SocketAsyncEventArgs"/>.
    /// 
    /// Every TcpConnection gets assigned a single socket fields and pair of <see cref="SocketAsyncEventArgs"/>,
    /// allocated once per lifetime of the connection actor:
    /// 
    /// - <see cref="ReceiveArgs"/> used only for receiving data. It has assigned buffer, rent from 
    ///   <see cref="TcpExt"/> once and recycled back upon actor termination. Once data has been received, it's 
    ///   copied to a separate <see cref="ByteString"/> object (so it's NOT a zero-copy operation).
    /// - <see cref="SendArgs"/> used only for sending data. Unlike receive args, it doesn't have any buffer 
    ///   assigned. Instead it uses treats incoming data as a buffer (it's safe due to immutable nature of
    ///   <see cref="ByteString"/> object). Therefore writes don't allocate any byte buffers.
    /// 
    /// Similar approach can be found on other networking libraries (i.e. System.IO.Pipelines and EventStore).
    /// Both buffers and <see cref="SocketAsyncEventArgs"/> are pooled to reduce GC pressure.
    /// </summary>
    internal abstract class TcpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        [Flags]
        enum ConnectionStatus
        {
            /// <summary>
            /// Marks that connection has invoked <see cref="Socket.ReceiveAsync"/> and that 
            /// <see cref="TcpConnection.ReceiveArgs"/> are currently trying to receive data.
            /// </summary>
            Receiving         = 1,

            /// <summary>
            /// Marks that connection has invoked <see cref="Socket.SendAsync"/> and that 
            /// <see cref="TcpConnection.SendArgs"/> are currently sending data. It's important as 
            /// <see cref="SocketAsyncEventArgs"/> will throw exception if another socket operations will
            /// be called over it as it's performing send request. For that reason we cannot release send args
            /// back to pool if it's sending (another connection actor could aquire that buffer and try to 
            /// use it while it's sending the data).
            /// </summary>
            Sending = 1 << 1,

            /// <summary>
            /// Marks that current connection has suspended reading.
            /// </summary>
            ReadingSuspended  = 1 << 2,

            /// <summary>
            /// Marks that current connection has suspended writing.
            /// </summary>
            WritingSuspended  = 1 << 3,

            /// <summary>
            /// Marks that current connection has been requested for shutdown. It may not occur immediatelly,
            /// i.e. because underlying <see cref="TcpConnection.SendArgs"/> is actually sending the data.
            /// </summary>
            ShutdownRequested = 1 << 4
        }

        private ConnectionStatus status;
        protected readonly TcpExt Tcp;
        protected readonly Socket Socket;
        protected SocketAsyncEventArgs ReceiveArgs;
        protected SocketAsyncEventArgs SendArgs;

        protected readonly ILoggingAdapter Log = Context.GetLogger();
        private readonly bool pullMode;
        private readonly bool traceLogging;

        private bool isOutputShutdown;

        private PendingWrite pendingWrite = EmptyPendingWrite.Instance;
        private Tuple<IActorRef, object> pendingAck = null;
        private bool peerClosed;
        private IActorRef interestedInResume;
        private CloseInformation closedMessage;  // for ConnectionClosed message in postStop

        private IActorRef watchedActor = Context.System.DeadLetters;

        protected TcpConnection(TcpExt tcp, Socket socket, bool pullMode)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));

            Tcp = tcp;
            Socket = socket;
            this.pullMode = pullMode;
            if (pullMode) SetStatus(ConnectionStatus.ReadingSuspended);
            traceLogging = tcp.Settings.TraceLogging;
        }

        private bool IsWritePending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return !HasStatus(ConnectionStatus.Sending) && !ReferenceEquals(EmptyPendingWrite.Instance, pendingWrite) ; }
        }

        protected void SignDeathPact(IActorRef actor)
        {
            UnsignDeathPact();
            watchedActor = actor;
            Context.Watch(actor);
        }

        protected void UnsignDeathPact()
        {
            if (!ReferenceEquals(watchedActor, Context.System.DeadLetters)) Context.Unwatch(watchedActor);
        }

        // STATES

        /// <summary>
        /// Connection established, waiting for registration from user handler.
        /// </summary>
        private Receive WaitingForRegistration(IActorRef commander)
        {
            return message =>
            {
                var register = message as Register;
                if (register != null)
                {
                    // up to this point we've been watching the commander,
                    // but since registration is now complete we only need to watch the handler from here on
                    if (!Equals(register.Handler, commander))
                    {
                        Context.Unwatch(commander);
                        Context.Watch(register.Handler);
                    }

                    if (traceLogging) Log.Debug("[{0}] registered as connection handler", register.Handler);

                    var info = new ConnectionInfo(register.Handler, register.KeepOpenOnPeerClosed, register.UseResumeWriting);

                    // if we have resumed reading from pullMode while waiting for Register then read
                    if (pullMode && !HasStatus(ConnectionStatus.ReadingSuspended)) ResumeReading();
                    else if (!pullMode) ReceiveAsync();

                    Context.SetReceiveTimeout(null);
                    Context.Become(Connected(info));
                }
                else if (message is ResumeReading) ClearStatus(ConnectionStatus.ReadingSuspended);
                else if (message is SuspendReading) SetStatus(ConnectionStatus.ReadingSuspended);
                else if (message is CloseCommand)
                {
                    var cmd = (CloseCommand)message;

                    var info = new ConnectionInfo(commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
                    HandleClose(info, Sender, cmd.Event);
                }
                else if (message is ReceiveTimeout)
                {
                    // after sending `Register` user should watch this actor to make sure
                    // it didn't die because of the timeout
                    Log.Debug("Configured registration timeout of [{0}] expired, stopping", Tcp.Settings.RegisterTimeout);
                    Context.Stop(Self);
                }
                else return false;
                return true;
            };
        }

        /// <summary>
        /// Normal connected state.
        /// </summary>
        private Receive Connected(ConnectionInfo info)
        {
            var handleWrite = HandleWriteMessages(info);
            return message =>
            {
                if (handleWrite(message)) return true;
                if (message is SuspendReading) SuspendReading();
                else if (message is ResumeReading) ResumeReading();
                else if (message is SocketReceived) DoRead(info, null);
                else if (message is CloseCommand) HandleClose(info, Sender, ((CloseCommand)message).Event);
                else return false;
                return true;
            };
        }

        /// <summary>
        /// The peer sent EOF first, but we may still want to send 
        /// </summary>
        private Receive PeerSentEOF(ConnectionInfo info)
        {
            var handleWrite = HandleWriteMessages(info);
            return message =>
            {
                if (handleWrite(message)) return true;
                var cmd = message as CloseCommand;
                if (cmd != null)
                {
                    HandleClose(info, Sender, cmd.Event);
                    return true;
                }
                if (message is ResumeReading) return true;
                return false;
            };
        }

        /// <summary>
        /// Connection is closing but a write has to be finished first
        /// </summary>
        private Receive ClosingWithPendingWrite(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            return message =>
            {
                if (message is SuspendReading) SuspendReading();
                else if (message is ResumeReading) ResumeReading();
                else if (message is SocketReceived) DoRead(info, closeCommander);
                else if (message is SocketSent)
                {
                    AcknowledgeSent();

                    if (IsWritePending) DoWrite(info);
                    else HandleClose(info, closeCommander, closedEvent);
                }
                else if (message is UpdatePendingWriteAndThen)
                {
                    var updatePendingWrite = (UpdatePendingWriteAndThen)message;
                    pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();

                    if (IsWritePending) DoWrite(info);
                    else HandleClose(info, closeCommander, closedEvent);
                }
                else if (message is WriteFileFailed) HandleError(info.Handler, ((WriteFileFailed)message).Cause); // rethrow exception from dispatcher task
                else if (message is Abort) HandleClose(info, Sender, Aborted.Instance);
                else return false;
                return true;
            };
        }

        /** connection is closed on our side and we're waiting from confirmation from the other side */
        private Receive Closing(ConnectionInfo info, IActorRef closeCommandor)
        {
            return message =>
            {
                if (message is SuspendReading) SuspendReading();
                else if (message is ResumeReading) ResumeReading();
                else if (message is SocketReceived) DoRead(info, closeCommandor);
                else if (message is Abort) HandleClose(info, Sender, Aborted.Instance);
                else return false;
                return true;
            };
        }

        private Receive HandleWriteMessages(ConnectionInfo info)
        {
            return message =>
            {
                if (message is SocketSent)
                {
                    AcknowledgeSent();

                    if (IsWritePending)
                    {
                        DoWrite(info);
                        if (!IsWritePending && interestedInResume != null)
                        {
                            interestedInResume.Tell(WritingResumed.Instance);
                            interestedInResume = null;
                        }
                    }
                }
                else if (message is WriteCommand)
                {
                    var write = (WriteCommand)message;
                    if (HasStatus(ConnectionStatus.WritingSuspended))
                    {
                        if (traceLogging) Log.Debug("Dropping write because writing is suspended");
                        Sender.Tell(write.FailureMessage);
                    }
                    else if (IsWritePending)
                    {
                        if (traceLogging) Log.Debug("Dropping write because queue is full");
                        Sender.Tell(write.FailureMessage);
                        if (info.UseResumeWriting) SetStatus(ConnectionStatus.WritingSuspended);
                    }
                    else
                    {
                        pendingWrite = CreatePendingWrite(Sender, write, info);
                        if (IsWritePending) DoWrite(info);
                    }
                }
                else if (message is ResumeWriting)
                {
                    /*
                     * If more than one actor sends Writes then the first to send this
                     * message might resume too early for the second, leading to a Write of
                     * the second to go through although it has not been resumed yet; there
                     * is nothing we can do about this apart from all actors needing to
                     * register themselves and us keeping track of them, which sounds bad.
                     *
                     * Thus it is documented that useResumeWriting is incompatible with
                     * multiple writers. But we fail as gracefully as we can.
                     */
                    ClearStatus(ConnectionStatus.WritingSuspended);
                    if (IsWritePending)
                    {
                        if (interestedInResume == null) interestedInResume = Sender;
                        else Sender.Tell(new CommandFailed(ResumeWriting.Instance));
                    }
                    else Sender.Tell(WritingResumed.Instance);
                }
                else if (message is UpdatePendingWriteAndThen)
                {
                    var updatePendingWrite = (UpdatePendingWriteAndThen)message;

                    pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();
                    if (IsWritePending) DoWrite(info);
                }
                else if (message is WriteFileFailed)
                {
                    HandleError(info.Handler, ((WriteFileFailed)message).Cause);
                }
                else return false;
                return true;
            };
        }

        // AUXILIARIES and IMPLEMENTATION

        /// <summary>
        /// Used in subclasses to start the common machinery above once a channel is connected
        /// </summary>
        protected void CompleteConnect(IActorRef commander, IEnumerable<Inet.SocketOption> options)
        {
            // Turn off Nagle's algorithm by default
            try
            {
                Socket.NoDelay = true;
            }
            catch (SocketException e)
            {
                Log.Debug("Could not enable TcpNoDelay: {0}", e.Message);
            }

            foreach (var option in options)
            {
                option.AfterConnect(Socket);
            }

            commander.Tell(new Connected(Socket.RemoteEndPoint, Socket.LocalEndPoint));

            Context.SetReceiveTimeout(Tcp.Settings.RegisterTimeout);
            Context.Become(WaitingForRegistration(commander));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SuspendReading()
        {
            SetStatus(ConnectionStatus.ReadingSuspended);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResumeReading()
        {
            ClearStatus(ConnectionStatus.ReadingSuspended);
            ReceiveAsync();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Tuple<IActorRef, object> SetPendingAcknowledgement(Tuple<IActorRef, object> pending)
        {
            return Interlocked.Exchange(ref pendingAck, pending);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AcknowledgeSent()
        {
            var ack = SetPendingAcknowledgement(null);
            ack?.Item1.Tell(ack.Item2);
            ClearStatus(ConnectionStatus.Sending);
        }

        private void DoRead(ConnectionInfo info, IActorRef closeCommander)
        {
            //TODO: What should we do if reading is suspended with an oustanding read - this will discard the read
            //      Should probably have an 'oustanding read'
            if (!HasStatus(ConnectionStatus.ReadingSuspended))
            {
                try
                {
                    var read = InnerRead(info, Tcp.Settings.ReceivedMessageSizeLimit, ReceiveArgs);
                    ClearStatus(ConnectionStatus.Receiving);
                    switch (read.Type)
                    {
                        case ReadResultType.AllRead:
                            if (!pullMode)
                                ReceiveAsync();
                            break;
                        case ReadResultType.EndOfStream:
                            if (isOutputShutdown)
                            {
                                if (traceLogging) Log.Debug("Read returned end-of-stream, our side already closed");
                                DoCloseConnection(info, closeCommander, ConfirmedClosed.Instance);
                            }
                            else
                            {
                                if (traceLogging) Log.Debug("Read returned end-of-stream, our side not yet closed");
                                HandleClose(info, closeCommander, PeerClosed.Instance);
                            }
                            break;
                        case ReadResultType.ReadError:
                            HandleError(info.Handler, new SocketException((int)read.Error));
                            break;
                    }
                }
                catch (SocketException cause)
                {
                    HandleError(info.Handler, cause);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadResult InnerRead(ConnectionInfo info, int remainingLimit, SocketAsyncEventArgs ea)
        {
            if (remainingLimit > 0)
            {
                //var maxBufferSpace = Math.Min(_tcp.Settings.DirectBufferSize, remainingLimit);
                var readBytes = ea.BytesTransferred;

                if (traceLogging) Log.Debug("Read [{0}] bytes.", readBytes);
                if (ea.SocketError == SocketError.Success && readBytes > 0)
                    info.Handler.Tell(new Received(ByteString.CopyFrom(ea.Buffer, ea.Offset, ea.BytesTransferred)));

                //if (ea.SocketError == SocketError.ConnectionReset) return ReadResult.EndOfStream;
                if (ea.SocketError != SocketError.Success) return new ReadResult(ReadResultType.ReadError, ea.SocketError);
                if (readBytes > 0) return ReadResult.AllRead;
                if (readBytes == 0) return ReadResult.EndOfStream;

                throw new IllegalStateException($"Unexpected value returned from read: {readBytes}");
            }
            return ReadResult.AllRead;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoWrite(ConnectionInfo info)
        {
            pendingWrite = pendingWrite.DoWrite(info);
        }

        private void HandleClose(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            SetStatus(ConnectionStatus.ShutdownRequested);
            
            if (closedEvent is Aborted)
            {
                if (traceLogging) Log.Debug("Got Abort command. RESETing connection.");
                DoCloseConnection(info, closeCommander, closedEvent);
            }
            else if (closedEvent is PeerClosed && info.KeepOpenOnPeerClosed)
            {
                // report that peer closed the connection
                info.Handler.Tell(PeerClosed.Instance);
                // used to check if peer already closed its side later
                peerClosed = true;
                Context.Become(PeerSentEOF(info));
            }
            else if (IsWritePending)   // finish writing first
            {
                UnsignDeathPact();
                if (traceLogging) Log.Debug("Got Close command but write is still pending.");
                Context.Become(ClosingWithPendingWrite(info, closeCommander, closedEvent));
            }
            else if (closedEvent is ConfirmedClosed) // shutdown output and wait for confirmation
            {
                if (traceLogging) Log.Debug("Got ConfirmedClose command, sending FIN.");

                // If peer closed first, the socket is now fully closed.
                // Also, if shutdownOutput threw an exception we expect this to be an indication
                // that the peer closed first or concurrently with this code running.
                if (peerClosed || !SafeShutdownOutput())
                    DoCloseConnection(info, closeCommander, closedEvent);
                else Context.Become(Closing(info, closeCommander));
            }
            // close gracefully now
            else
            {
                if (traceLogging) Log.Debug("Got Close command, closing connection.");
                Socket.Shutdown(SocketShutdown.Both);
                DoCloseConnection(info, closeCommander, closedEvent);
            }
        }

        private void DoCloseConnection(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            if (closedEvent is Aborted) Abort();
            else
            {
                CloseSocket();
            }

            var notifications = new HashSet<IActorRef>();
            if (info.Handler != null) notifications.Add(info.Handler);
            if (closeCommander != null) notifications.Add(closeCommander);
            StopWith(new CloseInformation(notifications, closedEvent));
        }

        private void HandleError(IActorRef handler, SocketException exception)
        {
            Log.Debug("Closing connection due to IO error {0}", exception);
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] { handler }), new ErrorClosed(exception.Message)));
        }

        private bool SafeShutdownOutput()
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Send);
                isOutputShutdown = true;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        protected void AcquireSocketAsyncEventArgs()
        {
            if (ReceiveArgs != null) throw new InvalidOperationException($"Cannot acquire receive SocketAsyncEventArgs. It's already has been initialized");
            if (SendArgs != null) throw new InvalidOperationException($"Cannot acquire send SocketAsyncEventArgs. It's already has been initialized");

            ReceiveArgs = Tcp.SocketEventArgsPool.Acquire(Self);
            var buffer = Tcp.BufferPool.Rent();
            ReceiveArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

            SendArgs = Tcp.SocketEventArgsPool.Acquire(Self);
        }

        private void ReleaseSocketAsyncEventArgs()
        {
            if (ReceiveArgs != null)
            {
                var buffer = new ByteBuffer(ReceiveArgs.Buffer, ReceiveArgs.Offset, ReceiveArgs.Count);
                Tcp.SocketEventArgsPool.Release(ReceiveArgs);
                Tcp.BufferPool.Release(buffer);
                ReceiveArgs = null;
            }

            if (SendArgs != null)
            {
                Tcp.SocketEventArgsPool.Release(SendArgs);
                SendArgs = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CloseSocket()
        {
            Socket.Close();
            isOutputShutdown = true;
            ReleaseSocketAsyncEventArgs();
        }

        private void Abort()
        {
            try
            {
                Socket.LingerState = new LingerOption(true, 0);  // causes the following close() to send TCP RST
            }
            catch (Exception e)
            {
                if (traceLogging) Log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }

            CloseSocket();
        }

        protected void StopWith(CloseInformation closeInfo)
        {
            closedMessage = closeInfo;
            Context.Stop(Self);
        }

        private void ReceiveAsync()
        {
            if (!HasStatus(ConnectionStatus.Receiving))
            {
                if (!Socket.ReceiveAsync(ReceiveArgs))
                    Self.Tell(SocketReceived.Instance);

                SetStatus(ConnectionStatus.Receiving);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasStatus(ConnectionStatus connectionStatus)
        {
            // don't use Enum.HasFlag - it's using reflection underneat
            var s = (int) connectionStatus;
            return ((int) status & s) == s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStatus(ConnectionStatus connectionStatus) => status |= connectionStatus;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearStatus(ConnectionStatus connectionStatus) => status &= ~connectionStatus;

        protected override void PostStop()
        {
            if (Socket.Connected) Abort();

            if (IsWritePending)
            {
                pendingWrite.Release(); // we should release ConnectionInfo event args (if they're not released already)
                ReleaseSocketAsyncEventArgs();
            }

            if (closedMessage != null)
            {
                var interestedInClose = IsWritePending
                    ? closedMessage.NotificationsTo.Union(new[] { pendingWrite.Commander })
                    : closedMessage.NotificationsTo;

                foreach (var listener in interestedInClose)
                {
                    listener.Tell(closedMessage.ClosedEvent);
                }
            }
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }

        private PendingWrite CreatePendingWrite(IActorRef commander, WriteCommand write, ConnectionInfo info)
        {
            var head = write;
            WriteCommand tail = Write.Empty;
            while (true)
            {
                if (head == Write.Empty)
                {
                    if (tail == Write.Empty) return EmptyPendingWrite.Instance;
                    else
                    {
                        head = tail;
                        tail = Write.Empty;
                        continue;
                    }
                }

                var w = head as Write;
                if (w != null && !w.Data.IsEmpty)
                {
                    return CreatePendingBufferWrite(commander, w.Data, w.Ack, tail);
                }
                //TODO: there's no TransmitFile API - .NET Core doesn't support it at all
                var cwrite = head as CompoundWrite;
                if (cwrite != null)
                {
                    head = cwrite.Head;
                    tail = cwrite.TailCommand;
                }
                else if (w != null)  // empty write with either an ACK or a non-standard NoACK
                {
                    if (w.WantsAck) commander.Tell(w.Ack);

                    head = tail;
                    tail = Write.Empty;
                }
                else throw new InvalidOperationException("Non reachable code");
            }
        }

        private PendingWrite CreatePendingBufferWrite(IActorRef commander, ByteString data, Tcp.Event ack, WriteCommand tail)
        {
            return new PendingBufferWrite(this, SendArgs, Self, commander, data.Buffers.GetEnumerator(), ack, tail);
        }

        //TODO: Port File IO - currently .NET Core doesn't support TransmitFile API

        private enum ReadResultType
        {
            EndOfStream,
            AllRead,
            ReadError,
        }

        private struct ReadResult
        {
            public static readonly ReadResult EndOfStream = new ReadResult(ReadResultType.EndOfStream, SocketError.Success);
            public static readonly ReadResult AllRead = new ReadResult(ReadResultType.AllRead, SocketError.Success);

            public readonly ReadResultType Type;
            public readonly SocketError Error;

            public ReadResult(ReadResultType type, SocketError error)
            {
                Type = type;
                Error = error;
            }
        }

        /// <summary>
        /// Used to transport information to the postStop method to notify
        /// interested party about a connection close.
        /// </summary>
        protected sealed class CloseInformation
        {
            /// <summary>
            /// TBD
            /// </summary>
            public ISet<IActorRef> NotificationsTo { get; }
            public Tcp.Event ClosedEvent { get; }

            public CloseInformation(ISet<IActorRef> notificationsTo, Tcp.Event closedEvent)
            {
                NotificationsTo = notificationsTo;
                ClosedEvent = closedEvent;
            }
        }

        /// <summary>
        /// Groups required connection-related data that are only available once the connection has been fully established.
        /// </summary>
        private sealed class ConnectionInfo
        {
            public readonly IActorRef Handler;
            public readonly bool KeepOpenOnPeerClosed;
            public readonly bool UseResumeWriting;

            public ConnectionInfo(IActorRef handler, bool keepOpenOnPeerClosed, bool useResumeWriting)
            {
                Handler = handler;
                KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
                UseResumeWriting = useResumeWriting;
            }
        }

        // INTERNAL MESSAGES
        private sealed class UpdatePendingWriteAndThen : INoSerializationVerificationNeeded
        {
            public PendingWrite RemainingWrite { get; }
            public Action Work { get; }

            public UpdatePendingWriteAndThen(PendingWrite remainingWrite, Action work)
            {
                RemainingWrite = remainingWrite;
                Work = work;
            }
        }

        private sealed class WriteFileFailed
        {
            public SocketException Cause { get; }

            public WriteFileFailed(SocketException cause)
            {
                Cause = cause;
            }
        }

        private abstract class PendingWrite
        {
            public IActorRef Commander { get; }
            public object Ack { get; }

            protected PendingWrite(IActorRef commander, object ack)
            {
                Commander = commander;
                Ack = ack;
            }

            public abstract PendingWrite DoWrite(ConnectionInfo info);
            public abstract void Release();
        }

        private sealed class EmptyPendingWrite : PendingWrite
        {
            public static readonly PendingWrite Instance = new EmptyPendingWrite();
            private EmptyPendingWrite() : base(ActorRefs.NoSender, NoAck.Instance) { }
            public override PendingWrite DoWrite(ConnectionInfo info) => this;

            public override void Release() { }
        }

        private sealed class PendingBufferWrite : PendingWrite
        {
            private readonly TcpConnection connection;
            private readonly IActorRef self;
            private readonly IEnumerator<ByteBuffer> remainingData;
            private readonly WriteCommand tail;
            private readonly SocketAsyncEventArgs sendArgs;
            private bool hasData;
            
            public PendingBufferWrite(
                TcpConnection connection,
                SocketAsyncEventArgs sendArgs,
                IActorRef self,
                IActorRef commander,
                IEnumerator<ByteBuffer> remainingData,
                object ack,
                WriteCommand tail) : base(commander, ack)
            {
                this.connection = connection;
                this.sendArgs = sendArgs;
                this.self = self;
                this.remainingData = remainingData;
                this.tail = tail;

                // start immediatelly as we'll need to cover the case if 
                // after buffer write request, the remaining enumerator is empty
                hasData = this.remainingData.MoveNext();
            }

            public override PendingWrite DoWrite(ConnectionInfo info)
            {
                try
                {
                    if (hasData)
                    {
                        connection.SetStatus(ConnectionStatus.Sending);
                        var buffer = remainingData.Current;
                        sendArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

                        //TODO: there's probably a better way to do this. We're potentially 
                        //      sending many byte buffers -> see: _connection.Socket.SendPacketsAsync
                        if (!connection.Socket.SendAsync(sendArgs))
                            self.Tell(SocketSent.Instance);

                        if (connection.traceLogging)
                            connection.Log.Debug("Wrote [{0}] bytes to channel", sendArgs.Count);
                    }

                    // check if there's more data awaiting
                    if ((hasData = remainingData.MoveNext()) == true)
                    {
                        return this;
                    }
                    else
                    {
                        var ack = Ack == NoAck.Instance ? null : Tuple.Create(Commander, Ack);
                        connection.SetPendingAcknowledgement(ack);
                        return connection.CreatePendingWrite(Commander, tail, info);
                    }
                }
                catch (SocketException e)
                {
                    connection.HandleError(info.Handler, e);
                    return this;
                }
            }

            public override void Release() { }
        }
    }
}
#endif