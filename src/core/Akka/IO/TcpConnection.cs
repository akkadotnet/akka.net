//-----------------------------------------------------------------------
// <copyright file="TcpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.IO.Buffers;
using Akka.Pattern;
using Akka.Util;
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
            Receiving = 1,

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
            ReadingSuspended = 1 << 2,

            /// <summary>
            /// Marks that current connection has suspended writing.
            /// </summary>
            WritingSuspended = 1 << 3,

            /// <summary>
            /// Marks that current connection has been requested for shutdown. It may not occur immediatelly,
            /// i.e. because underlying <see cref="TcpConnection.SendArgs"/> is actually sending the data.
            /// </summary>
            ShutdownRequested = 1 << 4
        }

        private ConnectionStatus _status;
        protected readonly TcpExt Tcp;
        protected readonly Socket Socket;
        protected SocketAsyncEventArgs ReceiveArgs;
        protected SocketAsyncEventArgs SendArgs;

        protected readonly ILoggingAdapter Log = Context.GetLogger();
        private readonly bool _pullMode;
        private readonly PendingSimpleWritesQueue _writeCommandsQueue;
        private readonly bool _traceLogging;

        private bool _isOutputShutdown;

        private readonly ConcurrentQueue<(IActorRef Commander, object Ack)> _pendingAcks = new ConcurrentQueue<(IActorRef, object)>();
        private bool _peerClosed;
        private IActorRef _interestedInResume;
        private CloseInformation _closedMessage;  // for ConnectionClosed message in postStop

        private IActorRef _watchedActor = Context.System.DeadLetters;

        protected TcpConnection(TcpExt tcp, Socket socket, bool pullMode, Option<int> writeCommandsBufferMaxSize)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            
            _pullMode = pullMode;
            _writeCommandsQueue = new PendingSimpleWritesQueue(Log, writeCommandsBufferMaxSize);
            _traceLogging = tcp.Settings.TraceLogging;
            
            Tcp = tcp;
            Socket = socket;
            
            if (pullMode) SetStatus(ConnectionStatus.ReadingSuspended);
        }


        private bool IsWritePending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return !HasStatus(ConnectionStatus.Sending) && !_writeCommandsQueue.IsEmpty; }
        }

        private Option<PendingWrite> GetAllowedPendingWrite() => IsWritePending ? GetNextWrite() : Option<PendingWrite>.None;

        protected void SignDeathPact(IActorRef actor)
        {
            UnsignDeathPact();
            _watchedActor = actor;
            Context.Watch(actor);
        }

        protected void UnsignDeathPact()
        {
            if (!ReferenceEquals(_watchedActor, Context.System.DeadLetters)) Context.Unwatch(_watchedActor);
        }

        // STATES

        /// <summary>
        /// Connection established, waiting for registration from user handler.
        /// </summary>
        private Receive WaitingForRegistration(IActorRef commander)
        {
            return message =>
            {
                switch (message)
                {
                    case Register register:
                        // up to this point we've been watching the commander,
                        // but since registration is now complete we only need to watch the handler from here on
                        if (!Equals(register.Handler, commander))
                        {
                            Context.Unwatch(commander);
                            Context.Watch(register.Handler);
                        }

                        if (_traceLogging) Log.Debug("[{0}] registered as connection handler", register.Handler);

                        var registerInfo = new ConnectionInfo(register.Handler, register.KeepOpenOnPeerClosed, register.UseResumeWriting);

                        // if we have resumed reading from pullMode while waiting for Register then read
                        if (_pullMode && !HasStatus(ConnectionStatus.ReadingSuspended)) ResumeReading();
                        else if (!_pullMode) ReceiveAsync();

                        Context.SetReceiveTimeout(null);
                        Context.Become(Connected(registerInfo));

                        // If there is something buffered before we got Register message - put it all to the socket
                        var bufferedWrite = GetNextWrite();
                        if (bufferedWrite.HasValue)
                        {
                            SetStatus(ConnectionStatus.Sending);
                            DoWrite(registerInfo, bufferedWrite.Value);
                        } 
                        
                        return true;
                    case ResumeReading _: ClearStatus(ConnectionStatus.ReadingSuspended); return true;
                    case SuspendReading _: SetStatus(ConnectionStatus.ReadingSuspended); return true;
                    case CloseCommand cmd:
                        var info = new ConnectionInfo(commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
                        HandleClose(info, Sender, cmd.Event);
                        return true;
                    case ReceiveTimeout _:
                        // after sending `Register` user should watch this actor to make sure
                        // it didn't die because of the timeout
                        Log.Debug("Configured registration timeout of [{0}] expired, stopping", Tcp.Settings.RegisterTimeout);
                        Context.Stop(Self);
                        return true;
                    case WriteCommand write:
                        // When getting Write before regestered handler, have to buffer writes until registration
                        var buffered = _writeCommandsQueue.EnqueueSimpleWrites(write, Sender, out var commandSize);
                        if (!buffered)
                        {
                            var writerInfo = new ConnectionInfo(Sender, false, false);
                            DropWrite(writerInfo, write);
                        }
                        else
                        {
                            Log.Warning("Received Write command before Register command. " +
                                        "It will be buffered until Register will be received (buffered write size is {0} bytes)", commandSize);
                        }
                        
                        return true;
                    default: return false;
                }
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
                switch (message)
                {
                    case SuspendReading _: SuspendReading(); return true;
                    case ResumeReading _: ResumeReading(); return true;
                    case SocketReceived _: DoRead(info, null); return true;
                    case CloseCommand cmd: HandleClose(info, Sender, cmd.Event); return true;
                    default: return false;
                }
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
                switch (message)
                {
                    case SuspendReading _: SuspendReading(); return true;
                    case ResumeReading _: ResumeReading(); return true;
                    case SocketReceived _: DoRead(info, closeCommander); return true;
                    case SocketSent _:
                        AcknowledgeSent();
                        if (IsWritePending)
                            DoWrite(info, GetAllowedPendingWrite());
                        else 
                            HandleClose(info, closeCommander, closedEvent);
                        return true;
                    case UpdatePendingWriteAndThen updatePendingWrite:
                        var nextWrite = updatePendingWrite.RemainingWrite;
                        updatePendingWrite.Work();

                        if (nextWrite.HasValue)
                            DoWrite(info, nextWrite);
                        else 
                            HandleClose(info, closeCommander, closedEvent);
                        return true;
                    case WriteFileFailed fail: HandleError(info.Handler, fail.Cause); return true;
                    case Abort _: HandleClose(info, Sender, Aborted.Instance); return true;
                    default: return false;
                }
            };
        }

        /** connection is closed on our side and we're waiting from confirmation from the other side */
        private Receive Closing(ConnectionInfo info, IActorRef closeCommander)
        {
            return message =>
            {
                switch (message)
                {
                    case SuspendReading _: SuspendReading(); return true;
                    case ResumeReading _: ResumeReading(); return true;
                    case SocketReceived _: DoRead(info, closeCommander); return true;
                    case Abort _: HandleClose(info, Sender, Aborted.Instance); return true;
                    default: return false;
                }
            };
        }

        private Receive HandleWriteMessages(ConnectionInfo info)
        {
            return message =>
            {
                switch (message)
                {
                    case SocketSent _:
                        // Send ack to sender
                        AcknowledgeSent();
                        
                        // If there is something to send - send it
                        var pendingWrite = GetAllowedPendingWrite();
                        if (pendingWrite.HasValue)
                        {
                            SetStatus(ConnectionStatus.Sending);
                            DoWrite(info, pendingWrite);
                        }
                        
                        // If message is fully sent, notify sender who sent ResumeWriting command
                        if (!IsWritePending && _interestedInResume != null)
                        {
                            _interestedInResume.Tell(WritingResumed.Instance);
                            _interestedInResume = null;
                        }
                        
                        return true;
                    case WriteCommand write:
                        if (HasStatus(ConnectionStatus.WritingSuspended))
                        {
                            if (_traceLogging) Log.Debug("Dropping write because writing is suspended");
                            Sender.Tell(write.FailureMessage);
                        }

                        if (HasStatus(ConnectionStatus.Sending))
                        {
                            // If we are sending something right now, just enqueue incoming write
                            if (!_writeCommandsQueue.EnqueueSimpleWrites(write, Sender))
                            {
                                DropWrite(info, write);
                                return true;
                            }
                        }
                        else
                        {
                            Option<PendingWrite> nextWrite;
                            if (_writeCommandsQueue.IsEmpty)
                            {
                                // If writes queue is empty, do not enqueue first write - we will send it immidiately
                                if (!_writeCommandsQueue.EnqueueSimpleWritesExceptFirst(write, Sender, out var simpleWriteCommand))
                                {
                                    DropWrite(info, write);
                                    return true;
                                }
                                
                                nextWrite = GetNextWrite(headCommands: new []{ (simpleWriteCommand, Sender) });
                            }
                            else
                            {
                                _writeCommandsQueue.EnqueueSimpleWrites(write, Sender);
                                nextWrite = GetNextWrite();
                            }
                            
                            // If there is something to send and we are allowed to, lets put the next command on the wire
                            if (nextWrite.HasValue)
                            {
                                SetStatus(ConnectionStatus.Sending);
                                DoWrite(info, nextWrite.Value);
                            } 
                        }
                        
                        return true;
                    case ResumeWriting _:
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
                            if (_interestedInResume == null) _interestedInResume = Sender;
                            else Sender.Tell(new CommandFailed(ResumeWriting.Instance));
                        }
                        else Sender.Tell(WritingResumed.Instance);
                        return true;
                    case UpdatePendingWriteAndThen updatePendingWrite:
                        var updatedWrite = updatePendingWrite.RemainingWrite;
                        updatePendingWrite.Work();
                        if (updatedWrite.HasValue) 
                            DoWrite(info, updatedWrite.Value);
                        return true;
                    case WriteFileFailed fail:
                        HandleError(info.Handler, fail.Cause);
                        return true;
                    default: return false;
                }
            };
        }

        private void DropWrite(ConnectionInfo info, WriteCommand write)
        {
            if (_traceLogging) Log.Debug("Dropping write because queue is full");
            Sender.Tell(write.FailureMessage);
            if (info.UseResumeWriting) SetStatus(ConnectionStatus.WritingSuspended);
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
        private void AcknowledgeSent()
        {
            while (_pendingAcks.TryDequeue(out var ackInfo))
            {
                ackInfo.Commander.Tell(ackInfo.Ack);
            }
           
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
                            if (!_pullMode)
                                ReceiveAsync();
                            break;
                        case ReadResultType.EndOfStream:
                            if (_isOutputShutdown)
                            {
                                if (_traceLogging) Log.Debug("Read returned end-of-stream, our side already closed");
                                DoCloseConnection(info, closeCommander, ConfirmedClosed.Instance);
                            }
                            else
                            {
                                if (_traceLogging) Log.Debug("Read returned end-of-stream, our side not yet closed");
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

                if (_traceLogging) Log.Debug("Read [{0}] bytes.", readBytes);
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
        private void DoWrite(ConnectionInfo info, Option<PendingWrite> write)
        {
            if (!write.HasValue)
                return;

            // Enqueue all acks assigned to this write to be sent once write is finished
            foreach (var pendingAck in write.Value.PendingAcks.Where(ackInfo => !ackInfo.Ack.Equals(NoAck.Instance)))
            {
                _pendingAcks.Enqueue(pendingAck);
            }
            
            write.Value.DoWrite(info);
        }

        private void HandleClose(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            SetStatus(ConnectionStatus.ShutdownRequested);

            if (closedEvent is Aborted)
            {
                if (_traceLogging) Log.Debug("Got Abort command. RESETing connection.");
                DoCloseConnection(info, closeCommander, closedEvent);
            }
            else if (closedEvent is PeerClosed && info.KeepOpenOnPeerClosed)
            {
                // report that peer closed the connection
                info.Handler.Tell(PeerClosed.Instance);
                // used to check if peer already closed its side later
                _peerClosed = true;
                Context.Become(PeerSentEOF(info));
            }
            else if (IsWritePending)   // finish writing first
            {
                UnsignDeathPact();
                if (_traceLogging) Log.Debug("Got Close command but write is still pending.");
                Context.Become(ClosingWithPendingWrite(info, closeCommander, closedEvent));
            }
            else if (closedEvent is ConfirmedClosed) // shutdown output and wait for confirmation
            {
                if (_traceLogging) Log.Debug("Got ConfirmedClose command, sending FIN.");

                // If peer closed first, the socket is now fully closed.
                // Also, if shutdownOutput threw an exception we expect this to be an indication
                // that the peer closed first or concurrently with this code running.
                if (_peerClosed || !SafeShutdownOutput())
                    DoCloseConnection(info, closeCommander, closedEvent);
                else Context.Become(Closing(info, closeCommander));
            }
            // close gracefully now
            else
            {
                if (_traceLogging) Log.Debug("Got Close command, closing connection.");
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
                _isOutputShutdown = true;
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

            ReceiveArgs = CreateSocketEventArgs(Self);
            var buffer = Tcp.BufferPool.Rent();
            ReceiveArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

            SendArgs = CreateSocketEventArgs(Self);
        }

        private void ReleaseSocketAsyncEventArgs()
        {
            if (ReceiveArgs != null)
            {
                var buffer = new ByteBuffer(ReceiveArgs.Buffer, ReceiveArgs.Offset, ReceiveArgs.Count);
                ReleaseSocketEventArgs(ReceiveArgs);
                // TODO: When using DirectBufferPool as a pool impelementation, there is potential risk,
                // that socket was working while released. In that case releasing buffer may not be safe.
                Tcp.BufferPool.Release(buffer);
                ReceiveArgs = null;
            }

            if (SendArgs != null)
            {
                ReleaseSocketEventArgs(SendArgs);
                SendArgs = null;
            }
        }

        protected SocketAsyncEventArgs CreateSocketEventArgs(IActorRef onCompleteNotificationsReceiver)
        {
            SocketCompleted ResolveMessage(SocketAsyncEventArgs e)
            {
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Receive:
                    case SocketAsyncOperation.ReceiveFrom:
                    case SocketAsyncOperation.ReceiveMessageFrom:
                        return SocketReceived.Instance;
                    case SocketAsyncOperation.Send:
                    case SocketAsyncOperation.SendTo:
                    case SocketAsyncOperation.SendPackets:
                        return SocketSent.Instance;
                    case SocketAsyncOperation.Accept:
                        return SocketAccepted.Instance;
                    case SocketAsyncOperation.Connect:
                        return SocketConnected.Instance;
                    default:
                        throw new NotSupportedException($"Socket operation {e.LastOperation} is not supported");
                }
            }
            
            var args = new SocketAsyncEventArgs();
            args.UserToken = onCompleteNotificationsReceiver;
            args.Completed += (sender, e) =>
            {
                var actorRef = e.UserToken as IActorRef;
                var completeMsg = ResolveMessage(e);
                actorRef?.Tell(completeMsg);
            };

            return args;
        }
        
        protected void ReleaseSocketEventArgs(SocketAsyncEventArgs e)
        {
            e.UserToken = null;
            e.AcceptSocket = null;

            try
            {
                e.SetBuffer(null, 0, 0);
                if (e.BufferList != null)
                    e.BufferList = null;
            }
            // it can be that for some reason socket is in use and haven't closed yet
            catch (InvalidOperationException) { }

            e.Dispose();
            
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CloseSocket()
        {
            Socket.Dispose();
            _isOutputShutdown = true;
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
                if (_traceLogging) Log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }

            CloseSocket();
        }

        protected void StopWith(CloseInformation closeInfo)
        {
            _closedMessage = closeInfo;
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
            var s = (int)connectionStatus;
            return ((int)_status & s) == s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStatus(ConnectionStatus connectionStatus) => _status |= connectionStatus;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearStatus(ConnectionStatus connectionStatus) => _status &= ~connectionStatus;

        protected override void PostStop()
        {
            if (Socket.Connected) Abort();
            else CloseSocket();

            // We do never store pending writes between messages anymore, so nothing is acquired and nothing to release 

            // always try to release SocketAsyncEventArgs to avoid memory leaks
            ReleaseSocketAsyncEventArgs();

            if (_closedMessage != null)
            {
                var interestedInClose = _writeCommandsQueue.TryGetNext(out var pending)
                    ? _closedMessage.NotificationsTo.Union(_writeCommandsQueue.DequeueAll().Select(cmd => cmd.Sender))
                    : _closedMessage.NotificationsTo;

                foreach (var listener in interestedInClose)
                {
                    listener.Tell(_closedMessage.ClosedEvent);
                }
            }
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }

        private Option<PendingWrite> GetNextWrite(IEnumerable<(SimpleWriteCommand Command, IActorRef Sender)> headCommands = null)
        {
            headCommands = headCommands ?? ImmutableList<(SimpleWriteCommand Command, IActorRef Sender)>.Empty;
            var writeCommands = new List<(Write Command, IActorRef Sender)>(_writeCommandsQueue.ItemsCount);
            foreach (var commandInfo in headCommands.Concat(_writeCommandsQueue.DequeueAll()))
            {
                switch (commandInfo.Command)
                {
                    case Write w when !w.Data.IsEmpty:
                        // Have real write - go on and put it to the wire
                        writeCommands.Add((w, commandInfo.Sender));
                        break;
                    case Write w:
                        // Write command is empty, so just sending Ask if required
                        if (w.WantsAck) commandInfo.Sender.Tell(w.Ack);
                        break;
                    default:
                        //TODO: there's no TransmitFile API - .NET Core doesn't support it at all
                        throw new InvalidOperationException("Non reachable code");
                }
            }

            if (writeCommands.Count > 0)
            {
                return CreatePendingBufferWrite(writeCommands);
            }
            
            // No more writes out there
            return Option<PendingWrite>.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PendingWrite CreatePendingBufferWrite(List<(Write Command, IActorRef Sender)> writes)
        {
            var acks = writes.Select(w => (w.Sender, (object)w.Command.Ack)).ToImmutableList();
            var dataList = writes.Select(w => w.Command.Data);
            return new PendingBufferWrite(this, SendArgs, Self, acks, dataList, Tcp.BufferPool);
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
            public Option<PendingWrite> RemainingWrite { get; }
            public Action Work { get; }

            public UpdatePendingWriteAndThen(Option<PendingWrite> remainingWrite, Action work)
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
            public IImmutableList<(IActorRef Commander, object Ack)> PendingAcks { get; }

            protected PendingWrite(IImmutableList<(IActorRef Commander, object Ack)> pendingAcks)
            {
                PendingAcks = pendingAcks;
            }

            public abstract void DoWrite(ConnectionInfo info);
        }

        private sealed class PendingBufferWrite : PendingWrite
        {
            private readonly TcpConnection _connection;
            private readonly IActorRef _self;
            private readonly IEnumerable<ByteString> _dataToSend;
            private readonly IBufferPool _bufferPool;
            private readonly SocketAsyncEventArgs _sendArgs;

            public PendingBufferWrite(
                TcpConnection connection,
                SocketAsyncEventArgs sendArgs,
                IActorRef self,
                IImmutableList<(IActorRef Commander, object Ack)> acks,
                IEnumerable<ByteString> dataToSend,
                IBufferPool bufferPool) : base(acks)
            {
                _connection = connection;
                _sendArgs = sendArgs;
                _self = self;
                _dataToSend = dataToSend;
                _bufferPool = bufferPool;
            }

            public override void DoWrite(ConnectionInfo info)
            {
                try
                {
                    _sendArgs.SetBuffer(_dataToSend);
                    if (!_connection.Socket.SendAsync(_sendArgs))
                        _self.Tell(SocketSent.Instance);
                }
                catch (SocketException e)
                {
                    _connection.HandleError(info.Handler, e);
                }
            }
        }

        public class PendingSimpleWritesQueue
        {
            private readonly ILoggingAdapter _log;
            private readonly Option<int> _maxQueueSizeInBytes;
            private readonly Queue<(SimpleWriteCommand Command, IActorRef Commander, int Size)> _queue;
            private int _totalSizeInBytes = 0;

            public PendingSimpleWritesQueue(ILoggingAdapter log, Option<int> maxQueueSizeInBytes)
            {
                _log = log;
                _maxQueueSizeInBytes = maxQueueSizeInBytes;
                _queue = new Queue<(SimpleWriteCommand Command, IActorRef Commander, int Size)>();
            }

            /// <summary>
            /// Gets total number of items in queue
            /// </summary>
            public int ItemsCount => _queue.Count;

            /// <summary>
            /// Adds all <see cref="SimpleWriteCommand"/> subcommands stored in provided command.
            /// Performs buffer size checks
            /// </summary>
            /// <exception cref="InternalBufferOverflowException">
            /// Thrown when data to buffer is larger then allowed <see cref="_maxQueueSizeInBytes"/>
            /// </exception>
            public bool EnqueueSimpleWrites(WriteCommand command, IActorRef sender)
            {
                return EnqueueSimpleWrites(command, sender, out _);
            }
            
            /// <summary>
            /// Adds all <see cref="SimpleWriteCommand"/> subcommands stored in provided command.
            /// Performs buffer size checks
            /// </summary>
            /// <exception cref="InternalBufferOverflowException">
            /// Thrown when data to buffer is larger then allowed <see cref="_maxQueueSizeInBytes"/>
            /// </exception>
            public bool EnqueueSimpleWrites(WriteCommand command, IActorRef sender, out int bufferedSize)
            {
                bufferedSize = 0;
                
                foreach (var writeInfo in ExtractFromCommand(command))
                {
                    var sizeAfterAppending = _totalSizeInBytes + writeInfo.DataSize;
                    if (_maxQueueSizeInBytes.HasValue && _maxQueueSizeInBytes.Value < sizeAfterAppending)
                    {
                        _log.Warning("Could not receive write command of size {0} bytes, " +
                                     "because buffer limit is {1} bytes and " +
                                     "it is already {2} bytes", writeInfo.DataSize, _maxQueueSizeInBytes, _totalSizeInBytes);
                        return false;
                    }

                    _totalSizeInBytes = sizeAfterAppending;
                    _queue.Enqueue((writeInfo.Command, sender, writeInfo.DataSize));
                    bufferedSize += writeInfo.DataSize;
                }
                
                return true;
            }
            
            /// <summary>
            /// Adds all <see cref="SimpleWriteCommand"/> subcommands stored in provided command.
            /// Performs buffer size checks for all, except first one, that is not buffered
            /// </summary>
            /// <returns>
            /// Not buffered (and not checked) first <see cref="SimpleWriteCommand"/>
            /// </returns>
            /// <exception cref="InternalBufferOverflowException">
            /// Thrown when data to buffer is larger then allowed <see cref="_maxQueueSizeInBytes"/>
            /// </exception>
            public bool EnqueueSimpleWritesExceptFirst(WriteCommand command, IActorRef sender, out SimpleWriteCommand first)
            {
                first = null;
                foreach (var writeInfo in ExtractFromCommand(command))
                {
                    if (first == null)
                    {
                        first = writeInfo.Command;
                        continue;
                    }
                    
                    var sizeAfterAppending = _totalSizeInBytes + writeInfo.DataSize;
                    if (_maxQueueSizeInBytes.HasValue && _maxQueueSizeInBytes.Value < sizeAfterAppending)
                    {
                        _log.Warning("Could not receive write command of size {0} bytes, " +
                                    "because buffer limit is {1} bytes and " +
                                    "it is already {2} bytes", writeInfo.DataSize, _maxQueueSizeInBytes, _totalSizeInBytes);
                        return false;
                    }

                    _totalSizeInBytes = sizeAfterAppending;
                    _queue.Enqueue((writeInfo.Command, sender, writeInfo.DataSize));
                }

                return true;
            }

            /// <summary>
            /// Gets next command from the queue, if any
            /// </summary>
            public (SimpleWriteCommand, IActorRef Sender) Dequeue()
            {
                if (_queue.Count == 0)
                    throw new InvalidOperationException("Write commands queue is empty");
                
                var (command, sender, size) = _queue.Dequeue();
                _totalSizeInBytes -= size;
                return (command, sender);
            }

            /// <summary>
            /// Dequeue all elements one by one
            /// </summary>
            /// <returns></returns>
            public IEnumerable<(SimpleWriteCommand Command, IActorRef Sender)> DequeueAll()
            {
                while (TryGetNext(out var command))
                    yield return command;
            }
            
            /// <summary>
            /// Gets next command from the queue, if any
            /// </summary>
            public bool TryGetNext(out (SimpleWriteCommand Command, IActorRef Sender) command)
            {
                command = default;
                if (_queue.Count == 0)
                    return false;

                command = Dequeue();
                return true;
            }

            /// <summary>
            /// Checks if commands queue is empty
            /// </summary>
            public bool IsEmpty => _totalSizeInBytes == 0;

            private IEnumerable<(SimpleWriteCommand Command, int DataSize)> ExtractFromCommand(WriteCommand command)
            {
                switch (command)
                {
                    case Write write:
                        yield return (write, write.Data.Count);
                        break;
                    case CompoundWrite compoundWrite:
                        var extractedFromHead = ExtractFromCommand(compoundWrite.Head);
                        var extractedFromTail = ExtractFromCommand(compoundWrite.TailCommand);
                        foreach (var extractedSimple in extractedFromHead.Concat(extractedFromTail))
                        {
                            yield return extractedSimple;
                        }
                        break;
                    default:
                        throw new ArgumentException($"Trying to calculate size of unknown write type: {command.GetType().FullName}");
                }
            }
        }
    }
}
