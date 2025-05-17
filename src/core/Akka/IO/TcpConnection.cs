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
    internal abstract class TcpConnection : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        /// <summary>
        /// Immutable connection state – the *only* mutable field in the actor is this record.
        /// </summary>
        private enum Phase
        {
            Connecting,
            AwaitReg,
            Open,
            HalfOpen,
            Closed
        }

        /// <summary>
        /// Immutable flags – reference to the live Queue + byte counter **and any deferred half‑close**.
        /// Moving every transient flag in here lets us reason over shutdown with a single value.
        /// </summary>
        /// <param name="PendingHalfClose">
        /// Indicates that a half-close (shutdown of the write side) has been requested (via ConfirmedClose or Close),
        /// but there are still pending writes in the queue. When all writes have been delivered, the write side will
        /// be closed (Socket.Shutdown(SocketShutdown.Send)), and this flag will be reset to false.
        /// </param>
        private readonly record struct ConnState(
            Phase Phase,
            bool  IsReceiving,
            bool  IsSending,
            bool  PeerClosed,
            bool  ClosedForWrites,
            bool  ReadingSuspended,
            bool  WritingSuspended,
            bool  KeepOpenOnPeerClosed,
            bool  PendingHalfClose,
            Queue<(WriteCommand Cmd, IActorRef Snd)> Queue,
            int   QueuedBytes)
        {
            public bool HasPending => IsSending || Queue.Count != 0;
            public bool CanSend    => !ClosedForWrites && !WritingSuspended;
            public bool CanReceive    => !PeerClosed       && !ReadingSuspended;

            private bool PeerIsReadyForUsToShutdown => (KeepOpenOnPeerClosed && !HasPending && PeerClosed && CanSend) || 
                                                    (!KeepOpenOnPeerClosed && PeerClosed);

            public bool Closeable(bool closeRequested) =>
                (closeRequested && Phase < Phase.Open) || // IMMEDIATE close if requested during connect or reg
                closeRequested &&
                !IsReceiving   &&
                !HasPending    &&
                (
                    // If we're in HalfOpen, both sides have closed their write sides, and nothing is left to do
                    (Phase == Phase.HalfOpen && ClosedForWrites && PeerClosed)
                    ||
                    // Fallback to previous logic for other phases
                    PeerIsReadyForUsToShutdown
                );

            public static ConnState Initial(Queue<(WriteCommand Cmd, IActorRef Snd)> q) =>
                new(Phase.Connecting, false, false, false, false, false, false, false, false, q, 0);
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

        private readonly Queue<(WriteCommand Cmd, IActorRef Sender)> _pendingWrites;
        private readonly byte[] _receiveBuffer;
        private ReadSocketAsyncEventArgs _receiveArgs;
        private AckSocketAsyncEventArgs _sendArgs;


        private bool _closeRequested;
        private readonly int _maxQueuedBytes;

        private ConnState _state;

        private readonly bool _traceLogging;
        
        // used by Akka.Streams
        private readonly bool _pullMode;

        private IActorRef? _commander;
        private IActorRef? _handler;

        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        protected TcpConnection(TcpSettings settings, Socket socket, bool pullMode)
        {
            Settings = settings;
            _maxQueuedBytes = settings.WriteCommandsQueueMaxSize; // –1 ⇒ unlimited;
            _pendingWrites = new Queue<(WriteCommand Cmd, IActorRef Sender)>(16);
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
            try
            {
                Socket.Dispose();
            }
            catch
            {
                /* ignore */
            }

            _receiveArgs.Dispose();
            _sendArgs.Dispose();
            _bufferPool.Return(_receiveBuffer);

            // fail everything still queued
            while (_pendingWrites.Count > 0)
            {
                var (cmd, snd) = _pendingWrites.Dequeue();
                snd.Tell(cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            }

            if (_closeEvent != null)
            {
                if(Settings.TraceLogging)
                    Log.Debug("[TcpConnection] sending close event [{0}] to {1}", _closeEvent, string.Join(",", _closeNotify));
                
                foreach (var sub in _closeNotify)
                    sub.Tell(_closeEvent);
            }
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
            _state = _state with { Phase = Phase.AwaitReg };
            _commander = commander;
            Become(AwaitRegBehaviour);
        }

        /* ================================================================= */
        /*  Close‑notification tracking                                  */
        /* ---------------------------------------------------------------- */
        private readonly HashSet<IActorRef> _closeNotify = [];
        private Event? _closeEvent;

        protected void MarkClose(IActorRef src, Event evt)
        {
            if (Settings.TraceLogging)
            {
                Log.Debug("[TcpConnection] working on connection closure: {0}", evt);
            }
            
            if (_closeEvent == null)
                _closeEvent = evt;
            _closeNotify.Add(src);
            if (_handler != null) _closeNotify.Add(_handler);
        }

        private void AwaitRegBehaviour()
        {
            Receive<Register>(reg =>
            {
                _handler = reg.Handler;
                if (_traceLogging) Log.Debug("[{0}] registered as connection handler", reg.Handler);
                Context.WatchWith(_handler, HandlerDied.Instance);
                Context.Unwatch(_commander);
                _state = _state with { Phase = Phase.Open, KeepOpenOnPeerClosed = reg.KeepOpenOnPeerClosed };
                Context.SetReceiveTimeout(null);
                Become(OpenBehaviour);
                IssueReceive();
                TrySend();
            });
            Receive<WriteCommand>(Enqueue);
            Receive<CloseCommand>(c =>
            {
                _closeRequested = true;
                EvaluateShutdown();
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
            Receive<ReadSocketAsyncEventArgs>(HandleReceiveCompleted);
            Receive<AckSocketAsyncEventArgs>(HandleSendCompleted);

            Receive<WriteCommand>(Enqueue);

            Receive<Close>(c =>
            {
                if (Settings.TraceLogging)
                    Log.Debug("[TcpConnection] Close requested");
                _closeRequested = true;
                _state = _state with { ReadingSuspended = true, IsReceiving = false };
                MarkClose(Sender, c.Event);
                TrySend();
                if (_state.HasPending)
                {
                    _state = _state with { PendingHalfClose = true };
                }
                else
                {
                    HalfCloseWriteSide();
                }
                EvaluateShutdown();
            });
            Receive<ConfirmedClose>(cc =>
            {
                if (Settings.TraceLogging)
                    Log.Debug("[TcpConnection] ConfirmedClose requested");
                MarkClose(Sender, cc.Event);
                _closeRequested = true;
                if (_state.HasPending)
                {
                    _state = _state with { PendingHalfClose = true };
                }
                else
                {
                    HalfCloseWriteSide();
                }
                EvaluateShutdown();
            });
            Receive<Abort>(s =>
            {
                if (Settings.TraceLogging)
                    Log.Debug("[TcpConnection] AbortSocket requested");
                MarkClose(Sender, s.Event);
                AbortSocket();
            });

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
            
            Receive<HandlerDied>(h =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
            //Receive<SuspendWriting>(_=> { _st = _st with { WritingSuspended=true  };               });
        }

        /* ----------------------------------------------------------------- */
        /*  Socket‑event handlers                                            */
        /* ----------------------------------------------------------------- */

        private long _totalSentBytes;
        private long _totalReceivedBytes;

        private void HandleReceiveCompleted(SocketAsyncEventArgs ea)
        {
            _state = _state with { IsReceiving = false };
            if (ea is { SocketError: SocketError.Success, BytesTransferred: > 0 })
            {
                if (Settings.TraceLogging)
                {
                    _totalReceivedBytes += ea.BytesTransferred;
                    Log.Debug("[TcpConnection] received {0} bytes [{1} total]", ea.BytesTransferred, _totalReceivedBytes);
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
            
            // unless we've been told otherwise, we want to close down the connection
            if (!_state.KeepOpenOnPeerClosed)
                _closeRequested = true;

            // FIN or error
            MarkClose(Self, PeerClosed.Instance);
            _handler!.Tell(PeerClosed.Instance);
            _state = _state with { PeerClosed = true };
            EvaluateShutdown();
        }

        private void HandleSendCompleted(AckSocketAsyncEventArgs ea)
        {
            _state = _state with { IsSending = false };

            if (ea.SocketError != SocketError.Success)
            {
                Log.Warning("[TcpConnection] send failed with error [{0}]", ea.SocketError);
                MarkClose(_handler!, new ErrorClosed(ea.SocketError.ToString()));
                Context.Stop(Self);
            }
            
            if (Settings.TraceLogging)
            {
                _totalSentBytes += ea.BytesTransferred;
                Log.Debug("[TcpConnection] completed write of {0}/{1} bytes (queued={2}/{3}) [{4} total sent]", ea.BytesTransferred, ea.BufferList.Sum(c => c.Count), _state.QueuedBytes, _maxQueuedBytes, _totalSentBytes);
            }

            foreach (var (c, ack) in ea.PendingAcks)
                c.Tell(ack);
            
           
            
            ea.ClearAcks();
            ea.BufferList = null; // release refs
            
            /* check deferred FIN */
            if(_state.PendingHalfClose && _pendingWrites.Count==0)
            {
                HalfCloseWriteSide();
                _state = _state with { PendingHalfClose = false };
            }
            
            TrySend();
            EvaluateShutdown();
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
                Sender.Tell(cmd.FailureMessage.WithCause(new IOException("write‑queue full")));
                return;
            }

            _pendingWrites.Enqueue((cmd, Sender));
            _state = _state with { QueuedBytes = _state.QueuedBytes + b };
            TrySend();
        }

        private void TrySend()
        {
            if (_state.IsSending || _pendingWrites.Count == 0 || !_state.CanSend) return;
            var segs = new List<ArraySegment<byte>>(8);
            var batchBytes = 0;

            while(_pendingWrites.Count>0)
            {
                var (cmd,snd) = _pendingWrites.Peek();
                if(cmd is not Write w)
                {
                    // unsupported command, fail fast
                    _pendingWrites.Dequeue();
                    snd.Tell(cmd.FailureMessage);
                    continue;
                }

                // do not break MTU / send‑buffer – simple heuristic
                if(batchBytes !=0 && batchBytes + w.Data.Count > Settings.MaxFrameSizeBytes)
                    break;

                // dequeue & account
                _pendingWrites.Dequeue();
                _state = _state with { QueuedBytes = _state.QueuedBytes - w.Data.Count };
                batchBytes += w.Data.Count;
                segs.AddRange(w.Data.Buffers);

                if(w.WantsAck) _sendArgs.PendingAcks.Add((snd, w.Ack));
            }

            if(segs.Count == 0) return; // only empty writes encountered
            
            _sendArgs.BufferList = segs;
            _state = _state with { IsSending = true };
            if(!Socket.SendAsync(_sendArgs)) Self.Tell(_sendArgs, Self);
        }

        private void HalfCloseWriteSide()
        {
            if (_state.ClosedForWrites) return;
            try
            {
                if(Settings.TraceLogging)
                    Log.Debug("[TcpConnection] half‑closing write side");
                
                Socket.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                /* ignore */
            }

            _state = _state with { ClosedForWrites = true, Phase = Phase.HalfOpen, PendingHalfClose = false};
        }

        /* ====================================================================*/
        /*  Shutdown decision                                                  */
        /* ====================================================================*/
        private void EvaluateShutdown()
        {
            if (_closeRequested)
            {
                var canClose = _state.Closeable(_closeRequested);
                if (!canClose)
                {
                    if (Settings.TraceLogging)
                        Log.Debug("[TcpConnection] can't close yet - state is [{0}]", _state);
                }
            }

            if (!_state.Closeable(_closeRequested)) return;
            if (Settings.TraceLogging)
                Log.Debug("[TcpConnection] shutting down connection [{0}]", _state);
            
            Self.Tell(PoisonPill.Instance);
        }
        
        private void AbortSocket()
        {
            try
            {
                Socket.LingerState = new LingerOption(true, 0);  // causes the following close() to send TCP RST
            }
            catch (Exception e)
            {
                if (_traceLogging) Log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }

            Context.Stop(Self);
        }

    }
}