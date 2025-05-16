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

        private readonly record struct ConnState(
            Phase Phase,
            bool IsReceiving,
            bool IsSending,
            bool PeerClosed,
            bool ClosedForWrites,
            bool ReadingSuspended,
            bool WritingSuspended,
            bool KeepOpenOnPeerClosed,
            Queue<(WriteCommand Cmd, IActorRef Sender)> Queue,
            int QueuedBytes)
        {
            public bool HasPending => IsSending || Queue.Count > 0;
            public bool CanSend => !ClosedForWrites && !WritingSuspended;
            public bool CanReceive => !PeerClosed && !ReadingSuspended;

            // may stop only when *we* requested it OR peer closed and we are NOT asked to stay open
            public bool Closeable(bool closeRequested) =>
                closeRequested &&
                !IsReceiving &&
                !HasPending &&
                (!PeerClosed || !KeepOpenOnPeerClosed);

            public static ConnState Initial(Queue<(WriteCommand Cmd, IActorRef Sender)> queue) =>
                new(Phase.Connecting, false, false, false, false, false, false, false, queue, 0);
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

        private IActorRef? _commander;
        private IActorRef? _handler;

        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        protected TcpConnection(TcpSettings settings, Socket socket)
        {
            Settings = settings;
            _maxQueuedBytes = settings.WriteCommandsQueueMaxSize; // –1 ⇒ unlimited;
            _pendingWrites = new Queue<(WriteCommand Cmd, IActorRef Sender)>(16);

            _traceLogging = Settings.TraceLogging;
            _state = ConnState.Initial(_pendingWrites);
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _receiveBuffer = _bufferPool.Rent(settings.MaxFrameSizeBytes);
            _receiveArgs = new ReadSocketAsyncEventArgs();
            _sendArgs = new AckSocketAsyncEventArgs();
            InitSocketEventArgs();
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
            _closeEvent = evt;
            _closeNotify.Add(src);
            if (_handler != null) _closeNotify.Add(_handler);
        }


        private void AwaitRegBehaviour()
        {
            Receive<Register>(reg =>
            {
                _handler = reg.Handler;
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
                TryStop();
            });
            Receive<CommanderDied>(_ => Context.Stop(Self));
        }

        private void OpenBehaviour()
        {
            Receive<ReadSocketAsyncEventArgs>(HandleReceiveCompleted);
            Receive<AckSocketAsyncEventArgs>(HandleSendCompleted);

            Receive<WriteCommand>(Enqueue);

            Receive<Close>(_ =>
            {
                _closeRequested = true;
                TryStop();
            });
            Receive<ConfirmedClose>(_ =>
            {
                HalfCloseWriteSide();
                _closeRequested = true;
                TryStop();
            });
            Receive<Abort>(_ => { Context.Stop(Self); });

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
                Log.Debug("Handler died, stopping connection actor");
                Context.Stop(Self);
            });
            //Receive<SuspendWriting>(_=> { _st = _st with { WritingSuspended=true  };               });
        }

        /* ----------------------------------------------------------------- */
        /*  Socket‑event handlers                                            */
        /* ----------------------------------------------------------------- */

        private void HandleReceiveCompleted(SocketAsyncEventArgs ea)
        {
            _state = _state with { IsReceiving = false };
            if (ea is { SocketError: SocketError.Success, BytesTransferred: > 0 })
            {
                _handler!.Tell(new Received(ByteString.CopyFrom(_receiveBuffer, 0, ea.BytesTransferred)));
                IssueReceive();
                return;
            }

            // FIN or error
            MarkClose(Self, PeerClosed.Instance);
            _handler!.Tell(PeerClosed.Instance);
            _state = _state with { PeerClosed = true };
            TryStop();
        }

        private void HandleSendCompleted(AckSocketAsyncEventArgs ea)
        {
            _state = _state with { IsSending = false };

            foreach (var (c, ack) in ea.PendingAcks)
                c.Tell(ack);
            ea.ClearAcks();
            ea.BufferList = null; // release refs

            TrySend();
            TryStop();
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
                Socket.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                /* ignore */
            }

            _state = _state with { ClosedForWrites = true, Phase = Phase.HalfOpen };
        }

        private void TryStop()
        {
            if (_state.Closeable(_closeRequested))
            {
                // graceful stop
                Self.Tell(PoisonPill.Instance);
            }
        }
    }
}