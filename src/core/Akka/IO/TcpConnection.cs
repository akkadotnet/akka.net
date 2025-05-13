//-----------------------------------------------------------------------
// <copyright file="TcpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

#nullable enable

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
        #region Ack‑aware SAEA

        private sealed class AckSocketAsyncEventArgs : SocketAsyncEventArgs
        {
            public readonly List<(IActorRef Commander, object Ack)> PendingAcks = new(8);
            public void ClearAcks() => PendingAcks.Clear();
        }

        #endregion

        #region completion msgs

        private sealed class SocketReceiveCompleted(int bytes, SocketError error) : INoSerializationVerificationNeeded
        {
            public int Bytes { get; } = bytes;
            public SocketError Error { get; } = error;
        }

        private sealed class SocketSendCompleted : INoSerializationVerificationNeeded
        {
            public static readonly SocketSendCompleted Instance = new();
        }

        #endregion

        protected readonly TcpExt Tcp;
        protected readonly Socket Socket;
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        private readonly Queue<(WriteCommand Cmd, IActorRef Sender)> _pendingWrites;

        private readonly byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveArgs;
        private AckSocketAsyncEventArgs _sendArgs;

        private IActorRef _handler = ActorRefs.Nobody;
        private IActorRef _watchedActor = Context.System.DeadLetters;
        private readonly int _maxWriteCapacity;

        private volatile bool _sending;
        private volatile bool _closingRequested;
        private volatile bool _peerClosed;

        private readonly bool _traceLogging;

        private bool _isOutputShutdown;

        private CloseInformation _closedMessage; // for ConnectionClosed message in postStop

        private readonly IOException _droppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private readonly IOException _droppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        protected TcpConnection(TcpExt tcp, Socket socket, Option<int> writeCommandsBufferMaxSize)
        {
            _maxWriteCapacity = writeCommandsBufferMaxSize.GetOrElse(tcp.Settings.WriteCommandsQueueMaxSize);
            _pendingWrites = new Queue<(WriteCommand Cmd, IActorRef Sender)>(_maxWriteCapacity);
            _traceLogging = tcp.Settings.TraceLogging;

            Tcp = tcp;
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            const int DefaultBufferSize = 64 * 1024; // 64 KiB – matches legacy DirectBufferSize
            _receiveBuffer = _bufferPool.Rent(DefaultBufferSize);
            InitSocketEventArgs();
        }

        private void InitSocketEventArgs()
        {
            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.UserToken = Self;
            _receiveArgs.Completed += OnCompleted;

            _sendArgs = new AckSocketAsyncEventArgs();
            _sendArgs.UserToken = Self;
            _sendArgs.Completed += OnCompleted;
        }

        private static void OnCompleted(object? sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not IActorRef self) return;
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    self.Tell(new SocketReceiveCompleted(e.BytesTransferred, e.SocketError));
                    break;
                case SocketAsyncOperation.Send:
                    self.Tell(SocketSendCompleted.Instance);
                    break;
                case SocketAsyncOperation.Connect: // TODO: need to anchor this to the `TcpOutGoingConnection` implementation
                    self.Tell(SocketConnected.Instance);
                    break;
                default:
                    self.Tell(new ErrorClosed($"Unexpected socket op {e.LastOperation}"));
                    break;
            }
        }

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

        private void IssueReceive()
        {
            if (!Socket.ReceiveAsync(_receiveArgs))
                Self.Tell(new SocketReceiveCompleted(_receiveArgs.BytesTransferred, _receiveArgs.SocketError));
        }
        
        private void HandleRead(SocketReceiveCompleted rc)
        {
            if (rc.Error != SocketError.Success)
            {
                Log.Error("Closing connection due to IO error {0}", rc.Error);
                _handler.Tell(new ErrorClosed(rc.Error.ToString()));
                Context.Stop(Self);
                return;
            }

            if (rc.Bytes == 0)
            {
                _peerClosed = true;
                TryCloseIfDone();
                return;
            }

            var bs = ByteString.CopyFrom(_receiveBuffer, 0, rc.Bytes);
            _handler.Tell(new Received(bs));
            IssueReceive();
        }

        private void IssueSend(IList<ArraySegment<byte>> buffers)
        {
            _sendArgs.BufferList = buffers;
            if (!Socket.SendAsync(_sendArgs))
                Self.Tell(SocketSendCompleted.Instance);
        }

        private void TrySendNext()
        {
            if (_sending || _pendingWrites.Count == 0) return;

            var maxBytes = _receiveBuffer.Length;
            var accumulated = 0;
            var batch = new List<ByteString>();
            _sendArgs.ClearAcks();

            while (_pendingWrites.Count > 0 && accumulated < maxBytes)
            {
                var (cmd, snd) = _pendingWrites.Peek();
                switch (cmd)
                {
                    case Write w when !w.Data.IsEmpty:
                        int wouldBe = accumulated + w.Data.Count;
                        if (wouldBe > maxBytes && batch.Count > 0) goto done;
                        _pendingWrites.Dequeue();
                        batch.Add(w.Data);
                        accumulated = wouldBe;
                        if (!Equals(w.Ack, NoAck.Instance))
                            _sendArgs.PendingAcks.Add((snd, w.Ack));
                        break;
                    case Write w:
                        _pendingWrites.Dequeue();
                        if (w.WantsAck) snd.Tell(w.Ack);
                        break;
                    default:
                        _pendingWrites.Dequeue();
                        snd.Tell(new CommandFailed(cmd));
                        break;
                }
            }

            done:
            if (batch.Count == 0)
            {
                TrySendNext();
                return;
            }

            _sending = true;
            var payload = FlattenByteStrings(batch);
            IssueSend(payload);
        }

        private void HandleSendCompleted()
        {
            _sending = false;
            foreach (var (c, ack) in _sendArgs.PendingAcks)
                c.Tell(ack);
            _sendArgs.ClearAcks();
            _sendArgs.BufferList.Clear();
            TrySendNext();
            TryCloseIfDone();
        }

        private void TryCloseIfDone()
        {
            if (!_closingRequested) return;
            if (_sending || _pendingWrites.Count > 0) return;
            if (!_peerClosed) return;
            _handler.Tell(ConfirmedClosed.Instance);
            Context.Stop(Self);
        }

        private static IList<ArraySegment<byte>> FlattenByteStrings(List<ByteString> parts)
        {
            if (parts.Count == 1)
                return parts[0].Buffers;

            return parts.SelectMany(c => c.Buffers).ToArray();
        }

        // STATES

        private bool TryBuffer(WriteCommand cmd, IActorRef sender)
        {
            if (_pendingWrites.Count < _maxWriteCapacity)
            {
                _pendingWrites.Enqueue((cmd, sender));
                return true;
            }
            else
            {
                // buffer is full
                return false;
            }
        }

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
                            SignDeathPact(register.Handler); // will unsign death pact with commander automatically

                        if (_traceLogging) Log.Debug("[{0}] registered as connection handler", register.Handler);

                        var registerInfo = new ConnectionInfo(register.Handler, register.KeepOpenOnPeerClosed,
                            register.UseResumeWriting);

                        Context.SetReceiveTimeout(null);
                        Context.Become(Connected(registerInfo));
                        // If there is something buffered before we got Register message - put it all to the socket
                        TrySendNext();
                        // start reading
                        IssueReceive();
                        return true;
                    case CloseCommand cmd:
                        var info = new ConnectionInfo(commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
                        HandleClose(info, Sender, cmd.Event);
                        return true;
                    case ReceiveTimeout:
                        // after sending `Register` user should watch this actor to make sure
                        // it didn't die because of the timeout
                        Log.Debug("Configured registration timeout of [{0}] expired, stopping",
                            Tcp.Settings.RegisterTimeout);
                        Context.Stop(Self);
                        return true;
                    case WriteCommand write:
                        // Have to buffer writes until registration
                        var buffered = TryBuffer(write, Sender);
                        if (!buffered)
                        {
                            var writerInfo = new ConnectionInfo(Sender, false, false);
                            DropWrite(writerInfo, write);
                        }
                        else
                        {
                            Log.Warning("Received Write command before Register command. " +
                                        "It will be buffered until Register will be received (buffered write size is {0} bytes)",
                                write.Bytes);
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
            return message =>
            {
                switch (message)
                {
                    case SocketReceiveCompleted r:
                        HandleRead(r);
                        return true;
                    case WriteCommand write:
                        var buffered = TryBuffer(write, Sender);
                        if (!buffered)
                        {
                            var writerInfo = new ConnectionInfo(Sender, false, false);
                            DropWrite(writerInfo, write);
                        }
                        else
                        {
                            Log.Warning("Received Write command before Register command. " +
                                        "It will be buffered until Register will be received (buffered write size is {0} bytes)",
                                write.Bytes);
                        }

                        return true;
                    case SocketSendCompleted:
                        HandleSendCompleted();
                        return true;
                    case CloseCommand cmd:
                        HandleClose(info, Sender, cmd.Event);
                        return true;
                    case SuspendReading:
                    case ResumeReading:
                        // no-ops
                        return true;
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
        private Receive ClosingWithPendingWrite(ConnectionInfo info, IActorRef closeCommander,
            ConnectionClosed closedEvent)
        {
            return message =>
            {
                switch (message)
                {
                    case SuspendReading _:
                        SuspendReading();
                        return true;
                    case ResumeReading _:
                        ResumeReading();
                        return true;
                    case SocketReceived _:
                        DoRead(info, closeCommander);
                        return true;
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
                    case WriteFileFailed fail:
                        HandleError(info.Handler, fail.Cause);
                        return true;
                    case Abort _:
                        HandleClose(info, Sender, Aborted.Instance);
                        return true;
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
                    case SocketReceived _:
                        DoRead(info, closeCommander);
                        return true;
                    case Abort _:
                        HandleClose(info, Sender, Aborted.Instance);
                        return true;
                    case SuspendReading _:
                    case ResumeReading _:
                        // no-ops
                        return true;
                    default: return false;
                }
            };
        }

        private void DropWrite(ConnectionInfo info, WriteCommand write)
        {
            if (_traceLogging) Log.Debug("Dropping write because queue is full");
            Sender.Tell(write.FailureMessage.WithCause(_droppingWriteBecauseQueueIsFullException));
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
            else if (IsWritePending) // finish writing first
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
            StopWith(
                new CloseInformation(new HashSet<IActorRef>(new[] { handler }), new ErrorClosed(exception.Message)));
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

        protected static void ReleaseSocketEventArgs(SocketAsyncEventArgs e)
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
            catch (InvalidOperationException)
            {
            }

            e.Dispose();
        }
        
        private void Abort()
        {
            try
            {
                Socket.LingerState = new LingerOption(true, 0); // causes the following close() to send TCP RST
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
            UnsignDeathPact();
            Context.Stop(Self);
        }

        protected override void PostStop()
        {
            try { Socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
            Socket.Dispose();
            _receiveArgs.Dispose();
            _sendArgs.Dispose();
            _bufferPool.Return(_receiveBuffer);
            base.PostStop();
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
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
    }
}