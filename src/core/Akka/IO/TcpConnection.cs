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
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;

#nullable enable

namespace Akka.IO
{
    using static Akka.IO.Tcp;
    using ByteBuffer = ArraySegment<byte>;

    // A **green‑field** rewrite of the connection actor, distilled to
    //   • 4 stable phases (Connecting ▸ AwaitRegistration ▸ Open ▸ HalfOpen)
    //   • 8 booleans that fully describe the transient aspects of the socket.
    //   • single immutable record `ConnState` passed by value.
    //   • all close logic in one method (TryStop).
    //
    //  ┌───────────────────────── ASCII *phase* diagram ─────────────────────────┐
    //  │                                                                         │
    //  │                (socket.ConnectAsync)                                    │
    //  │     +-----------+   Connected   +---------------+                       │
    //  │     |Connecting |──────────────►|AwaitReg       |──Register────────────+│
    //  │     +-----------+               +-------┬-------+                       │
    //  │                                             │                           │
    //  │                       writes/reads          ▼                           │
    //  │                                        +-----------+  Close  +------+   │
    //  │                                        |   Open    |────────►|Closed|   │
    //  │                                        +----┬------+         +------+   │
    //  │                                             │ ConfirmedClose            │
    //  │                                             ▼                           │
    //  │                                        +-----------+  FIN↑  +------+   │
    //  │                                        | HalfOpen  |────────►|Closed|   │
    //  │                                        +-----------+         +------+   │
    //  │                                                                         │
    //  └─────────────────────────────────────────────────────────────────────────┘


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
    /// - <see cref="_receiveArgs"/> used only for receiving data. It has assigned buffer, rent from 
    ///   <see cref="TcpExt"/> once and recycled back upon actor termination. Once data has been received, it's 
    ///   copied to a separate <see cref="ByteString"/> object (so it's NOT a zero-copy operation).
    /// - <see cref="_sendArgs"/> used only for sending data. Unlike receive args, it doesn't have any buffer 
    ///   assigned. Instead it uses treats incoming data as a buffer (it's safe due to immutable nature of
    ///   <see cref="ByteString"/> object). Therefore writes don't allocate any byte buffers.
    /// 
    /// Similar approach can be found on other networking libraries (i.e. System.IO.Pipelines and EventStore).
    /// Both buffers and <see cref="SocketAsyncEventArgs"/> are pooled to reduce GC pressure.
    /// </summary>
    internal abstract class TcpConnection : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        /// <summary>
        /// Immutable flags – reference to the live Queue + byte counter **and any deferred half‑close**.
        /// Moving every transient flag in here lets us reason over shutdown with a single value.
        /// </summary>
        private readonly record struct ConnState(
            bool IsReceiving,
            bool IsSending,
            bool PeerClosed,
            bool OutputShutdown,
            bool ReadingSuspended,
            bool WritingSuspended,
            bool KeepOpenOnPeerClosed,
            Queue<(Write Cmd, IActorRef Snd)> Queue,
            int QueuedBytes)
        {
            public bool HasPending => IsSending || Queue.Count != 0;
            public bool CanSend => !OutputShutdown && !WritingSuspended;
            public bool CanReceive => !PeerClosed && !ReadingSuspended;

            public static ConnState Initial(Queue<(Write Cmd, IActorRef Snd)> q) =>
                new(false, false, false, false, true, true, false, q, 0);
        }

        #region Ack‑aware SAEA

        private sealed class AckSocketAsyncEventArgs : SocketAsyncEventArgs, INoSerializationVerificationNeeded,
            IDeadLetterSuppression
        {
            public readonly List<(IActorRef Commander, object Ack)> PendingAcks = new(8);
            public void ClearAcks() => PendingAcks.Clear();
        }

        private sealed class ReadSocketAsyncEventArgs : SocketAsyncEventArgs, INoSerializationVerificationNeeded,
            IDeadLetterSuppression;

        private class CommanderDied : IDeadLetterSuppression
        {
            public static readonly CommanderDied Instance = new();

            private CommanderDied()
            {
            }
        }

        private class HandlerDied : IDeadLetterSuppression
        {
            public static readonly HandlerDied Instance = new();

            private HandlerDied()
            {
            }
        }

        #endregion

        protected readonly TcpSettings Settings;
        protected readonly Socket Socket;
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        private readonly Queue<(Write Cmd, IActorRef Sender)> _pendingWrites;
        private readonly byte[] _receiveBuffer;
        private readonly ReadSocketAsyncEventArgs _receiveArgs;
        private readonly AckSocketAsyncEventArgs _sendArgs;
        
        private readonly int _maxQueuedBytes;

        private ConnState _state;

        private readonly bool _traceLogging;

        // used by Akka.Streams
        private readonly bool _pullMode;

        private IActorRef? _commander;
        private IActorRef? _handler;
        private CloseInformation? _closeInformation;

        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        private int? _partialWriteOffset = null;

        protected TcpConnection(TcpSettings settings, Socket socket, bool pullMode)
        {
            Settings = settings;
            _maxQueuedBytes = settings.WriteCommandsQueueMaxSize; // –1 ⇒ unlimited;
            _pendingWrites = new Queue<(Write Cmd, IActorRef Sender)>(16);
            _pullMode = pullMode;

            _traceLogging = Settings.TraceLogging;
            _state = ConnState.Initial(_pendingWrites);
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _receiveBuffer = _bufferPool.Rent(settings.MaxFrameSizeBytes);
            _receiveArgs = new ReadSocketAsyncEventArgs();
            _sendArgs = new AckSocketAsyncEventArgs();
            InitSocketEventArgs();

            if (_pullMode)
            {
                // have to wait for the first pull request to start reading
                _state = _state with { ReadingSuspended = true };
            }
        }

        private void InitSocketEventArgs()
        {
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.UserToken = Self;
            _receiveArgs.Completed += OnCompleted;


            _sendArgs.UserToken = Self;
            _sendArgs.Completed += OnCompleted;
        }

        private static void OnCompleted(object? sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not IActorRef self) return;
            self.Tell(e);
        }

        /* ================================================================= */
        /*  Base‑class public API                                            */
        /* ================================================================= */

        protected override void PostStop()
        {
            if (Socket.Connected) AbortSocket();
            else CloseSocket();

            _receiveArgs.Dispose();
            _sendArgs.Dispose();
            _bufferPool.Return(_receiveBuffer);

            // fail everything still queued
            while (_pendingWrites.Count > 0)
            {
                var (cmd, snd) = _pendingWrites.Dequeue();
                snd.Tell(cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            }

            if (_closeInformation != null)
            {
                if (Settings.TraceLogging)
                    Log.Debug("[TcpConnection] sending close event [{0}] to {1}", _closeInformation.ClosedEvent,
                        string.Join(",", _closeInformation.NotificationsTo));

                foreach (var sub in _closeInformation.NotificationsTo)
                    sub.Tell(_closeInformation.ClosedEvent);
            }
        }
        
        protected override void PostRestart(Exception reason)
        {
            // have to assert that we are not restarting
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }

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

            _commander = commander;
            Context.WatchWith(_commander, CommanderDied.Instance);
            commander.Tell(new Connected(Socket.RemoteEndPoint, Socket.LocalEndPoint));

            Context.SetReceiveTimeout(Settings.RegisterTimeout);
            _commander = commander;
            Become(AwaitRegBehaviour);
        }

        /* ================================================================= */
        /*  Close‑notification tracking                                  */
        /* ---------------------------------------------------------------- */
        protected void StopWith(CloseInformation closeInformation)
        {
            if(_handler != null)
            {
                closeInformation = closeInformation with { NotificationsTo = closeInformation.NotificationsTo.Add(_handler!) };
            }
            
            _closeInformation = closeInformation;
            Context.Stop(Self);
        }
        
        private void AwaitRegBehaviour()
        {
            Receive<Register>(reg =>
            {
                _handler = reg.Handler;
                if (_traceLogging) Log.Debug("[{0}] registered as connection handler", reg.Handler);
                Context.WatchWith(_handler, HandlerDied.Instance);
                Context.Unwatch(_commander);
                _state = _state with { KeepOpenOnPeerClosed = reg.KeepOpenOnPeerClosed, ReadingSuspended = _pullMode, WritingSuspended = false };
                // set a default close event - if someone hard-kills us we log an aborted
                _closeInformation = CloseInformation.Single(_handler, Aborted.Instance);
                Context.SetReceiveTimeout(null);
                Become(OpenBehaviour);
                IssueReceive();
                TrySend();
            });
            Receive<WriteCommand>(w =>
            {
                var queueSizeBefore = _pendingWrites.Count;
                Enqueue(w);
                if(_pendingWrites.Count > queueSizeBefore)
                {
                    // need to log a warning here about writing before registration
                    Log.Warning("Received Write command before Register command. " +
                                "It will be buffered until Register will be received (buffered write size is {0} bytes)", w.Bytes);
                }
            });
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            Receive<SuspendReading>(_ => { _state = _state with { ReadingSuspended = true }; });
            Receive<ResumeReading>(_ =>
            {
                _state = _state with { ReadingSuspended = false };
            });
            Receive<CommanderDied>(_ => Context.Stop(Self));
            Receive<ReceiveTimeout>(_ =>
            {
                // after sending `Register` user should watch this actor to make sure
                // it didn't die because of the timeout
                Log.Debug("Configured registration timeout of [{0}] expired, stopping", Settings.RegisterTimeout);
                Context.Stop(Self);
            });
        }

        private void OpenBehaviour()
        {
            Receive<ReadSocketAsyncEventArgs>(s => HandleReceiveCompleted(s, null));
            Receive<AckSocketAsyncEventArgs>(HandleSendCompleted);
            Receive<WriteCommand>(Enqueue);
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            SuspendResumeHandlers();
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
            //Receive<SuspendWriting>(_=> { _st = _st with { WritingSuspended=true  };               });
        }

        private void SuspendResumeHandlers()
        {
            Receive<ResumeReading>(_ =>
            {
                _state = _state with { ReadingSuspended = false };
                IssueReceive();
            });
            Receive<SuspendReading>(_ => { _state = _state with { ReadingSuspended = true }; });
            Receive<ResumeWriting>(_ =>
            {
                _state = _state with { WritingSuspended = false };
                TrySend();
            });
        }

        private void PeerSentEOF()
        {
            Receive<AckSocketAsyncEventArgs>(HandleSendCompleted);
            Receive<WriteCommand>(Enqueue);
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            SuspendResumeHandlers();
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
        }

        private void ClosingWithPendingWrite(IActorRef closeSender, ConnectionClosed e)
        {
            Receive<ReadSocketAsyncEventArgs>(s => HandleReceiveCompleted(s, closeSender));
            Receive<AckSocketAsyncEventArgs>(s =>
            {
                HandleSendCompleted(s);
                if (!_state.HasPending)
                {
                    // we are finished sending
                    HandleClose(closeSender, e);
                }
            });
            Receive<WriteCommand>(Enqueue);
            Receive<Abort>(c => HandleClose(Sender, c.Event));
            SuspendResumeHandlers();
        }

        /// <summary>
        /// Connection is closed on our side, and we're waiting from confirmation from the other side.
        /// </summary>
        private void Closing(IActorRef closeSender)
        {
            Receive<ReadSocketAsyncEventArgs>(s => HandleReceiveCompleted(s, closeSender));
            Receive<AckSocketAsyncEventArgs>(HandleSendCompleted);
            Receive<WriteCommand>(w =>
            {
                // fail all writes
                Sender.Tell(w.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            });
            Receive<Abort>(c => HandleClose(Sender, c.Event));
            SuspendResumeHandlers();
            Receive<HandlerDied>(h =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
        }

        /* ----------------------------------------------------------------- */
        /*  Socket‑event handlers                                            */
        /* ----------------------------------------------------------------- */

        private long _totalSentBytes;
        private long _totalReceivedBytes;

        private void HandleReceiveCompleted(SocketAsyncEventArgs ea, IActorRef? closeCommander)
        {
            _state = _state with { IsReceiving = false };
            if (ea is { SocketError: SocketError.Success, BytesTransferred: > 0 })
            {
                if (Settings.TraceLogging)
                {
                    _totalReceivedBytes += ea.BytesTransferred;
                    Log.Debug("[TcpConnection] received {0} bytes [{1} total]", ea.BytesTransferred,
                        _totalReceivedBytes);
                }

                _handler!.Tell(new Received(ByteString.CopyFrom(_receiveBuffer, 0, ea.BytesTransferred)));

                if (_pullMode)
                {
                    // in pull mode we need to wait for the next pull request
                    _state = _state with { ReadingSuspended = true };
                }
                else
                {
                    IssueReceive();
                }

                return;
            }
            
            // check for an error code
            if (ea.SocketError != SocketError.Success)
            {
                if(_traceLogging)
                    Log.Debug("[TcpConnection] read failed with error [{0}]", ea.SocketError);
                HandleError(new SocketException((int)ea.SocketError));
                return;
            }
            
            // check for EOF
            if (ea.BytesTransferred == 0)
            {
                if (_state.OutputShutdown)
                {
                    if(_traceLogging) 
                        Log.Debug("[TcpConnection] EOF received; our side is already closed. Closing connection.");
                    DoCloseConnection(closeCommander ?? _handler!, ConfirmedClosed.Instance);
                }
                else
                {
                    if (_traceLogging)
                        Log.Debug("[TcpConnection] EOF received");
                    _state = _state with { PeerClosed = true };
                    HandleClose(closeCommander ?? _handler!, PeerClosed.Instance);
                }
            }
        }

        private void HandleSendCompleted(AckSocketAsyncEventArgs ea)
        {
            _state = _state with { IsSending = false };

            if (_traceLogging)
                Log.Debug($"[TcpConnection] HandleSendCompleted: BytesTransferred={ea.BytesTransferred}, PendingAcks={ea.PendingAcks.Count}, PartialWriteOffset={_partialWriteOffset}");

            if (ea.SocketError != SocketError.Success)
            {
                if(_traceLogging)
                    Log.Debug("[TcpConnection] write failed with error [{0}]", ea.SocketError);
                HandleError(new SocketException((int)ea.SocketError));
                return;
            }

            if (Settings.TraceLogging)
            {
                _totalSentBytes += ea.BytesTransferred;
                Log.Debug("[TcpConnection] completed write of {0}/{1} bytes (queued={2}/{3}) [{4} total sent]",
                    ea.BytesTransferred, ea.BufferList?.Sum(c => c.Count) ?? 0, _state.QueuedBytes, _maxQueuedBytes,
                    _totalSentBytes);
            }

            foreach (var (c, ack) in ea.PendingAcks)
                c.Tell(ack);

            ea.ClearAcks();
            ea.BufferList = null; // release refs
            TrySend();
        }

        /* ----------------------------------------------------------------- */
        /*  Read / Write helpers                                             */
        /* ----------------------------------------------------------------- */

        private void IssueReceive()
        {
            if (!_state.CanReceive || _state.IsReceiving) return;
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _state = _state with { IsReceiving = true };
            if (!Socket.ReceiveAsync(_receiveArgs)) Self.Tell(_receiveArgs, Self);
        }

        private void Enqueue(WriteCommand cmd)
        {
            var b = (int)cmd.Bytes;
            if (_maxQueuedBytes >= 0 && _state.QueuedBytes + b > _maxQueuedBytes)
            {
                Sender.Tell(cmd.FailureMessage.WithCause(DroppingWriteBecauseQueueIsFullException));
                return;
            }
            
            EnqueueInner();

            _state = _state with { QueuedBytes = _state.QueuedBytes + b };
            TrySend();
            return;

            void EnqueueInner()
            {
                switch (cmd)
                {
                    case Write realWrite:
                        _pendingWrites.Enqueue((realWrite, Sender));
                        break;
                    case CompoundWrite compounds: //TODO: poorly designed API we should remove
                        foreach (var c in compounds)
                        {
                            if(c is Write w)
                            {
                                _pendingWrites.Enqueue((w, Sender));
                            }
                            else
                            {
                                Sender.Tell(c.FailureMessage.WithCause(new InvalidOperationException($"Cannot enqueue {c} - only valid classes are Write and CompoundWrite")));
                            }
                        }
                        break;
                    default:
                        Sender.Tell(cmd.FailureMessage.WithCause(new InvalidOperationException($"Cannot enqueue {cmd} - only valid classes are Write and CompoundWrite")));
                        break;
                }
            }
        }

        private void TrySend()
        {
            if (_traceLogging)
                Log.Debug($"[TcpConnection] TrySend called. IsSending={_state.IsSending}, PendingWrites={_pendingWrites.Count}, CanSend={_state.CanSend}, PartialWriteOffset={_partialWriteOffset}");
            if (!_state.CanSend) return;
            if (_state.IsSending || _pendingWrites.Count == 0) return;

            var segs = new List<ArraySegment<byte>>(8);
            var batchBytes = 0;

            while (_pendingWrites.Count > 0 && batchBytes < Settings.MaxFrameSizeBytes)
            {
                var (w, snd) = _pendingWrites.Peek();

                var data = w.Data;
                var offset = _partialWriteOffset ?? 0;
                var remaining = data.Count - offset;

                // Handle empty writes immediately
                if (remaining == 0)
                {
                    _pendingWrites.Dequeue();
                    _state = _state with { QueuedBytes = _state.QueuedBytes - w.Data.Count };
                    _partialWriteOffset = null;
                    if (w.WantsAck) snd.Tell(w.Ack); // message was already sent - ACK right away
                    if (_traceLogging)
                        Log.Debug($"[TcpConnection] TrySend: encountered empty write, dequeued. Remaining queue: {_pendingWrites.Count}");
                    continue;
                }

                var toSend = Math.Min(remaining, Settings.MaxFrameSizeBytes - batchBytes);

                if (_traceLogging)
                    Log.Debug($"[TcpConnection] TrySend batching: offset={offset}, remaining={remaining}, toSend={toSend}, batchBytes={batchBytes}");

                // non-copying operation - just creates a new ArraySegment without copying any bytes
                var chunk = data.Slice(offset, toSend);
                segs.AddRange(chunk.Buffers);
                batchBytes += toSend;

                if (toSend == remaining)
                {
                    // Full write completed
                    _pendingWrites.Dequeue();
                    _state = _state with { QueuedBytes = _state.QueuedBytes - w.Data.Count };
                    _partialWriteOffset = null;
                    if (w.WantsAck) _sendArgs.PendingAcks.Add((snd, w.Ack));
                    if (_traceLogging)
                        Log.Debug($"[TcpConnection] TrySend: completed full write, dequeued. Remaining queue: {_pendingWrites.Count}");
                }
                else
                {
                    // Partial write, update offset and break
                    _partialWriteOffset = offset + toSend;
                    if (_traceLogging)
                        Log.Debug($"[TcpConnection] TrySend: partial write, will resume at offset {_partialWriteOffset}");
                    break;
                }
            }

            if (segs.Count == 0)
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] TrySend: no segments to send (only empty writes encountered)");
                return;
            }

            _sendArgs.BufferList = segs;
            _state = _state with { IsSending = true };
            if (_traceLogging)
                Log.Debug($"[TcpConnection] TrySend: sending {segs.Count} segments, total bytes={batchBytes}");
            if (!Socket.SendAsync(_sendArgs)) Self.Tell(_sendArgs, Self);
        }
        
        /* ====================================================================*/
        /*  Shutdown decision                                                  */
        /* ====================================================================*/
        private void HandleClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            switch (closeEvent)
            {
                case Aborted:
                    if(_traceLogging)
                        Log.Debug("Got Abort command. RESETing connection.");
                    DoCloseConnection(closeSender, closeEvent);
                    break;
                // this shouldn't happen really - ErrorClosed is mostly just a message we send to handler.
                // but in case we get it, we should close the connection immediately. 
                case ErrorClosed: 
                    DoCloseConnection(closeSender, closeEvent);
                    break;
                case PeerClosed when _state.KeepOpenOnPeerClosed:
                    _handler.Tell(PeerClosed.Instance);
                    _state = _state with { PeerClosed = true };
                    Become(PeerSentEOF);
                    break;
                case not null when _state.HasPending:
                    Context.Unwatch(_handler); // stop watching the handler
                    if(_traceLogging)
                        Log.Debug("Got Close command but write is still pending.");
                    Become(() => ClosingWithPendingWrite(closeSender, closeEvent));
                    break;
                case ConfirmedClosed: //shutdown output and wait for confirmation
                    if(_traceLogging)
                        Log.Debug("Got ConfirmedClose command, sending FIN.");
                    /*
                     * If peer closed first, the socket is now fully closed.
                     * Also, if ShutdownOutput threw an exception we expect this to be an indication
                     * that the peer closed first or concurrently with this code running.
                     */
                    if(_state.PeerClosed || !ShutdownOutput())
                    {
                        DoCloseConnection(closeSender, closeEvent);
                    }
                    else
                    {
                        if(_traceLogging)
                            Log.Debug("Got ConfirmedClose command, but write is still pending.");
                        Become(() => Closing(closeSender));
                    }
                    break;
                default: // no pending writes, not required to stay open when peer is closed
                    if(_traceLogging)
                        Log.Debug("Got Close command, closing connection.");
                    try
                    {
                        Socket.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException e)
                    {
                        Log.Error(e, "Graceful socket shutdown failed");
                    }
                    DoCloseConnection(closeSender, closeEvent!);
                    break;
            }
        }

        private void HandleError(SocketException e)
        {
            Log.Debug(e, "Closing connection due to I/O error: {0}", e.SocketErrorCode);
            var errorClosed = new ErrorClosed(e.Message);
            if(_closeInformation != null)
            {
                _closeInformation = _closeInformation with { ClosedEvent = errorClosed };
            }
            else
            {
                _closeInformation = CloseInformation.Single(_handler ?? _commander!, errorClosed);
            }
            Context.Stop(Self);
        }

        private bool ShutdownOutput()
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Send);
                _state = _state with { OutputShutdown = true };
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
        
        private void DoCloseConnection(IActorRef closeSender, ConnectionClosed closedEvent)
        {
            switch (closedEvent)
            {
                case Aborted:
                    AbortSocket();
                    break;
                default:
                    CloseSocket();
                    break;
            }

            StopWith(new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), closedEvent));
        }

        private void CloseSocket()
        {
            try
            {
                Socket.Close();
            }
            catch
            {
                /* ignore */
            }

            try
            {
                Socket.Dispose();
            }
            catch
            {
                /* ignore */
            }

            _state = _state with { OutputShutdown = true, ReadingSuspended = true };
        }

        private void AbortSocket()
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

        protected sealed record CloseInformation(ImmutableHashSet<IActorRef> NotificationsTo, Tcp.Event ClosedEvent)
        {
            public static CloseInformation Single(IActorRef closeSender, Tcp.Event closedEvent)
            {
                return new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), closedEvent);
            }
        }
    }
}