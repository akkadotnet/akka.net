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
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;

#nullable enable

namespace Akka.IO
{
    using static Akka.IO.Tcp;

    //  ┌──────────────────────── ASCII *phase* diagram ─────────────────────────┐
    //  │                                                                         │
    //  │     +-----------+   Connected   +---------------+                       │
    //  │     |Connecting |──────────────►|AwaitReg       |──Register──────────┐  │
    //  │     +-----------+               +---------------+                    │  │
    //  │                                                                      ▼  │
    //  │                          +------------------+   PeerClosed   +-----+    │
    //  │                          |       Open       |───keepOpen────►| EOF |    │
    //  │                          +-------┬----------+                +--┬--+    │
    //  │                                  │ Close / ConfirmedClose       │       │
    //  │                                  ▼                              ▼       │
    //  │                          +------------------+   StreamEof   +-------+   │
    //  │                          |     Closing      |───────────────►|Closed |  │
    //  │                          +------------------+                +-------+  │
    //  │                                                                         │
    //  └─────────────────────────────────────────────────────────────────────────┘
    //
    // Phases map onto Become(...) calls: AwaitRegBehaviour, OpenBehaviour,
    // PeerSentEofBehaviour, ClosingBehaviour. State that survives a Become
    // lives in the connection-state flag region below; everything else is
    // local to the behaviour.

    /// <summary>
    /// INTERNAL API: Base class for TcpIncomingConnection and TcpOutgoingConnection.
    ///
    /// TcpConnection is an actor abstraction over a single TCP connection.
    /// It delegates all I/O machinery (pipes, pump loops, buffer management) to an
    /// <see cref="ITransportConnection"/> implementation.
    ///
    /// Two actor-driven coordination paths:
    /// - ReadFromPipe: reads from <see cref="ITransportConnection.Input"/>, copies to pooled buffers,
    ///   emits <see cref="Tcp.Received"/>
    /// - Write: writes directly to the transport via <see cref="ITransportConnection.WriteAsync(ReadOnlySequence{byte}, CancellationToken)"/>
    ///
    /// All shutdown and error handling flows through the actor mailbox for thread safety.
    /// </summary>
    internal abstract class TcpConnection : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        #region Internal messages

        /// <summary>
        /// Self-tell: a background task failed with an exception.
        /// </summary>
        private sealed class IoTaskFailed : INoSerializationVerificationNeeded
        {
            public Exception Cause { get; }
            public IoTaskFailed(Exception cause) { Cause = cause; }
        }

        /// <summary>
        /// Self-tell: the read-from-stream task observed EOF (0 bytes).
        /// </summary>
        private sealed class StreamEof : INoSerializationVerificationNeeded
        {
            public static readonly StreamEof Instance = new();
            private StreamEof() { }
        }

        /// <summary>
        /// Self-tell: PipeReader.ReadAsync completed with data.
        /// </summary>
        private sealed class PipeReadCompleted : INoSerializationVerificationNeeded
        {
            public ReadOnlySequence<byte> Data { get; }
            public bool IsCompleted { get; }
            public bool IsCanceled { get; }

            public PipeReadCompleted(ReadOnlySequence<byte> data, bool isCompleted, bool isCanceled)
            {
                Data = data;
                IsCompleted = isCompleted;
                IsCanceled = isCanceled;
            }
        }

        private sealed class PipeReadCanceled : INoSerializationVerificationNeeded
        {
            public static readonly PipeReadCanceled Instance = new();
            private PipeReadCanceled() { }
        }

        /// <summary>
        /// Self-tell: the transport's read pump has completed (check IsFaulted for errors).
        /// </summary>
        private sealed class ReadPumpCompleted : INoSerializationVerificationNeeded
        {
            public static readonly ReadPumpCompleted Instance = new();
            private ReadPumpCompleted() { }
        }

        /// <summary>
        /// Self-tell: the transport's read pump failed with an I/O error.
        /// </summary>
        private sealed class ReadPumpFailed : INoSerializationVerificationNeeded
        {
            public Exception Cause { get; }
            public ReadPumpFailed(Exception cause) { Cause = cause; }
        }

        /// <summary>
        /// Self-tell: transport shutdown/close operation completed successfully.
        /// </summary>
        private sealed class TransportOperationCompleted : INoSerializationVerificationNeeded
        {
            public static readonly TransportOperationCompleted Instance = new();
            private TransportOperationCompleted() { }
        }

        /// <summary>
        /// Self-tell: transport shutdown/close operation failed.
        /// </summary>
        private sealed class TransportOperationFailed : INoSerializationVerificationNeeded
        {
            public Exception Cause { get; }
            public TransportOperationFailed(Exception cause) { Cause = cause; }
        }

        private sealed class CommanderDied : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            public static readonly CommanderDied Instance = new();
            private CommanderDied() { }
        }

        private sealed class HandlerDied : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            public static readonly HandlerDied Instance = new();
            private HandlerDied() { }
        }

        #endregion

        #region Write command wrapper

        private readonly record struct WriteCommand(Write Cmd, IActorRef Sender);

        #endregion

        #region Shutdown state

        private const int ShutdownNone = 0;
        private const int ShutdownInitiated = 1;

        #endregion

        protected readonly TcpSettings Settings;
        protected readonly Socket Socket;
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        private readonly bool _traceLogging;
        private readonly bool _pullMode;
        private readonly int _maxQueuedBytes;

        // Transport connection — owns pipes, pump loops, stream
        private ITransportConnection? _transport;

        // CTS for pipe read cancellation
        private CancellationTokenSource? _cts;

        // Shutdown guard - ensures only one shutdown path executes
        private int _shutdownState = ShutdownNone;

        // Reading flow control — all state managed in actor thread, no synchronization needed
        private bool _readingAllowed;
        private bool _readPending; // true when a PipeReader.ReadAsync is in flight

        // Actor references
        private IActorRef? _commander;
        private IActorRef? _handler;
        private CloseInformation? _closeInformation;

        private int _pendingRegistrationBytes;

        private readonly Queue<WriteCommand> _pendingRegistrationWrites = new();

        #region Connection state flags
        // Transient flags that survive a Become(...) and together describe where this
        // connection sits in the Open → PeerSentEof / Closing → Closed flow.
        // Set/cleared only on the actor thread; no synchronization required.

        // Peer sent FIN — incoming side is half-closed. Set by HandleStreamEof or
        // by the Closing-phase StreamEof handler; once true, no further reads will arrive.
        private bool _peerClosed;

        // We've sent FIN (or fully closed). Set when Tcp.Close / Tcp.ConfirmedClose
        // completes the transport shutdown; latches off for the rest of the close.
        private bool _outputShutdown;

        // From Tcp.Register: when true, peer-FIN does not stop the connection — we
        // just transition to PeerSentEofBehaviour and keep the write side open.
        private bool _keepOpenOnPeerClosed;

        // We've initiated a graceful close (HandleClose has run). Used to fast-fail
        // any further Tcp.Write commands with DroppingWriteBecauseClosingException.
        private bool _closingGracefully;

        // The pipe-reader pump task observed completion (EOF or error). Read path
        // gates on this together with _outputShutdown to know when TryFinishClose
        // can stop the actor.
        private bool _readPumpCompleted;

        // Read pump failed with an I/O error rather than EOF. Set together with
        // _readPumpError; the next HandlePipeRead with IsCompleted=true will surface
        // this as Tcp.ErrorClosed instead of treating it as a clean EOF.
        private bool _readPumpHasError;
        private Exception? _readPumpError;
        #endregion

        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        protected TcpConnection(TcpSettings settings, Socket socket, bool pullMode)
        {
            Settings = settings;
            _maxQueuedBytes = settings.WriteCommandsQueueMaxSize;
            _pullMode = pullMode;
            _traceLogging = Settings.TraceLogging;
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        /* ================================================================= */
        /*  Base-class public API                                            */
        /* ================================================================= */

        protected override void PostStop()
        {
            // Best-effort cleanup - cancel everything and close.
            // Do NOT synchronously wait for DisposeAsync — the pump tasks may be
            // blocked on stream I/O that can only be unblocked by closing the socket,
            // which would deadlock if we Wait() first.
            TryCancelCts();

            if (_transport != null)
            {
                // Abort cancels the CTS, sets linger=0, closes the socket.
                // This unblocks any pending stream.ReadAsync/WriteAsync in the pump tasks.
                // The pump tasks will exit with OperationCanceledException or IOException.
                try { _transport.Abort(); }
                catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 transport may already be disposed
            }
            else
            {
                // Transport was never created (e.g. PoisonPill before Register).
                // Close the socket directly since no transport owns it.
                try { Socket.Close(); }
                catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket may already be disposed
            }

            while (_pendingRegistrationWrites.Count > 0)
            {
                var write = _pendingRegistrationWrites.Dequeue();
                write.Sender.Tell(write.Cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            }

            if (_closeInformation != null)
            {
                if (Settings.TraceLogging)
                    Log.Debug("sending close event [{0}] to {1}", _closeInformation.ClosedEvent,
                        string.Join(",", _closeInformation.NotificationsTo));

                foreach (var sub in _closeInformation.NotificationsTo)
                    sub.Tell(_closeInformation.ClosedEvent);
            }
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }

        /// <summary>
        /// Used in subclasses to start the common machinery above once a channel is connected.
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
            commander.Tell(new Connected(Socket.RemoteEndPoint!, Socket.LocalEndPoint!));

            Context.SetReceiveTimeout(Settings.RegisterTimeout);
            Become(AwaitRegBehaviour);
        }

        /// <summary>
        /// Starts the transport connection and monitors its read pump.
        /// Called after registration is complete.
        /// </summary>
        protected void StartTransport(ITransportConnection transport)
        {
            _transport = transport;
            _cts = new CancellationTokenSource();

            // Monitor the read pump for completion/errors
            var self = Self;
            _ = MonitorReadPumpAsync();

            async Task MonitorReadPumpAsync()
            {
                try
                {
                    await transport.ReadCompleted.ConfigureAwait(false);
                    self.Tell(ReadPumpCompleted.Instance);
                }
                catch (Exception ex)
                {
                    self.Tell(new ReadPumpFailed(ex));
                }
            }
        }

        /// <summary>
        /// Creates the transport connection. Subclasses must supply this.
        /// For incoming connections, wraps the accepted socket.
        /// For outgoing connections, wraps the connected socket (possibly with TLS).
        /// </summary>
        protected abstract ITransportConnection CreateTransport();

        /* ================================================================= */
        /*  Close-notification tracking                                      */
        /* ================================================================= */

        protected void StopWith(CloseInformation closeInformation)
        {
            if (_handler != null)
            {
                closeInformation = closeInformation with { NotificationsTo = closeInformation.NotificationsTo.Add(_handler!) };
            }

            _closeInformation = closeInformation;
            Context.Stop(Self);
        }

        /* ================================================================= */
        /*  Actor Behaviours                                                 */
        /* ================================================================= */

        private void AwaitRegBehaviour()
        {
            Receive<Register>(reg =>
            {
                _handler = reg.Handler;
                if (_traceLogging) Log.Debug("[{0}] registered as connection handler", reg.Handler);
                Context.WatchWith(_handler, HandlerDied.Instance);
                Context.Unwatch(_commander);
                _keepOpenOnPeerClosed = reg.KeepOpenOnPeerClosed;
                _closeInformation = CloseInformation.Single(_handler, Aborted.Instance);
                Context.SetReceiveTimeout(null);

                // Create and start the transport now that we have a handler
                var transport = CreateTransport();
                StartTransport(transport);

                // Allow reading unless pull mode
                if (!_pullMode)
                {
                    AllowReading();
                }

                FlushPendingRegistrationWrites();

                Become(OpenBehaviour);
            });
            Receive<Tcp.WriteCommand>(w => BufferWriteBeforeRegister(w, Sender));
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            Receive<SuspendReading>(_ => { /* no-op before registration */ });
            Receive<ResumeReading>(_ => { /* no-op before registration */ });
            Receive<CommanderDied>(_ => Context.Stop(Self));
            Receive<ReceiveTimeout>(_ =>
            {
                Log.Debug("Configured registration timeout of [{0}] expired, stopping", Settings.RegisterTimeout);
                Context.Stop(Self);
            });
        }

        private void OpenBehaviour()
        {
            Receive<Tcp.WriteCommand>(HandleWrite);
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            Receive<ReadPumpFailed>(msg => HandleReadPumpFailed(msg));
            Receive<ReadPumpCompleted>(_ => HandleReadPumpCompleted());
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            SuspendResumeHandlers();
            Receive<StreamEof>(_ => HandleStreamEof());
            Receive<IoTaskFailed>(msg => HandleIoError(msg.Cause));
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
        }

        private void PeerSentEofBehaviour()
        {
            // Peer closed their write side, but we can still write
            Receive<Tcp.WriteCommand>(HandleWrite);
            Receive<CloseCommand>(c => HandleClose(Sender, c.Event));
            Receive<ReadPumpFailed>(msg => HandleReadPumpFailed(msg));
            Receive<ReadPumpCompleted>(_ => HandleReadPumpCompleted());
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            Receive<StreamEof>(_ =>
            {
                // Already in PeerSentEof state — this is a duplicate notification, ignore
                if (_traceLogging) Log.Debug("StreamEof in PeerSentEofBehaviour (no-op)");
            });
            SuspendResumeHandlers();
            Receive<IoTaskFailed>(msg => HandleIoError(msg.Cause));
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
        }

        private void ClosingBehaviour(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            // We're shutting down - reject new writes, wait for transport operations
            Receive<Tcp.WriteCommand>(w =>
            {
                Sender.Tell(w.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            });
            Receive<Abort>(c => HandleClose(Sender, c.Event));
            Receive<ReadPumpFailed>(msg =>
            {
                HandleReadPumpFailed(msg);
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<ReadPumpCompleted>(_ =>
            {
                HandleReadPumpCompleted();
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<StreamEof>(_ =>
            {
                _peerClosed = true;

                if (closeEvent is ConfirmedClosed)
                {
                    if (_traceLogging)
                        Log.Debug("Peer FIN received during ConfirmedClose - connection fully closed");
                    DoCloseConnection(closeSender, ConfirmedClosed.Instance);
                    return;
                }

                if (_traceLogging)
                    Log.Debug("EOF received during close - waiting for transport to finish");

                TryFinishClose(closeSender, closeEvent);
            });
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            Receive<TransportOperationCompleted>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("Transport operation completed during close");

                if (closeEvent is ConfirmedClosed)
                {
                    // For ConfirmedClose (half-close), transport has flushed writes and sent FIN.
                    // Now keep reading until peer sends their FIN (StreamEof).
                    _outputShutdown = true;

                    if (_traceLogging)
                        Log.Debug("ConfirmedClose: FIN sent, waiting for peer FIN");
                }
                else
                {
                    // For regular Close, transport is fully closed
                    _outputShutdown = true;
                    TryFinishClose(closeSender, closeEvent);
                }
            });
            Receive<TransportOperationFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("Transport operation failed during close: {0}", msg.Cause.Message);
                DoCloseConnection(closeSender, closeEvent);
            });
            Receive<IoTaskFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("I/O task failed during close: {0}", msg.Cause.Message);
                DoCloseConnection(closeSender, closeEvent);
            });
            SuspendResumeHandlers();
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died during close, stopping connection actor", _handler);
                Context.Stop(Self);
            });

            // If read pump already completed before we entered ClosingBehaviour, try to close now
            TryFinishClose(closeSender, closeEvent);
        }

        /// <summary>
        /// Checks whether all conditions are met to finalize the connection close.
        /// For ConfirmedClose, the StreamEof handler manages closing directly (waiting for peer FIN).
        /// For regular Close, we close once the read pump has completed and transport is done.
        /// </summary>
        private void TryFinishClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            if (closeEvent is ConfirmedClosed)
            {
                // For ConfirmedClose, we need to wait for peer FIN (StreamEof).
                // The StreamEof handler calls DoCloseConnection directly.
                return;
            }

            // For regular Close: once read pump has completed and output is shutdown
            if (_outputShutdown && _readPumpCompleted)
                DoCloseConnection(closeSender, closeEvent);
        }

        private void SuspendResumeHandlers()
        {
            Receive<ResumeReading>(_ =>
            {
                AllowReading();
            });
            Receive<SuspendReading>(_ =>
            {
                SuspendReadingInternal();
            });
            Receive<ResumeWriting>(_ =>
            {
                // No special action needed — transport handles write buffering
                if (_traceLogging) Log.Debug("ResumeWriting received");
            });
        }

        /* ================================================================= */
        /*  Read pump monitoring                                             */
        /* ================================================================= */

        /// <summary>
        /// Called when the transport's read pump encounters an I/O error.
        /// Records the error so that subsequent HandlePipeRead with IsCompleted
        /// can propagate it as an ErrorClosed instead of treating it as normal EOF.
        /// </summary>
        private void HandleReadPumpFailed(ReadPumpFailed msg)
        {
            _readPumpCompleted = true;
            _readPumpHasError = true;
            _readPumpError = msg.Cause;
            if (_traceLogging)
                Log.Debug("Read pump failed: {0}", msg.Cause.Message);
        }

        private void HandleReadPumpCompleted()
        {
            _readPumpCompleted = true;

            if (_traceLogging)
                Log.Debug("Read pump completed");
        }

        /* ================================================================= */
        /*  Read flow control — actor-driven pipe reads, no synchronization  */
        /* ================================================================= */

        private void AllowReading()
        {
            _readingAllowed = true;
            RequestPipeRead();
        }

        private void SuspendReadingInternal()
        {
            _readingAllowed = false;
            // Current in-flight read (if any) will still complete and deliver,
            // but no further reads will be requested until ResumeReading.
        }

        /// <summary>
        /// Kicks off a PipeReader.ReadAsync and pipes the result back to Self.
        /// No-op if a read is already in flight or the transport isn't initialized.
        /// </summary>
        private void RequestPipeRead()
        {
            if (_readPending || _transport == null || _cts == null) return;
            _readPending = true;

            if (_traceLogging) Log.Debug("RequestPipeRead: kicking off pipe read");

            var self = Self;
            var reader = _transport.Input;
            var ct = _cts.Token;

            _ = AwaitPipeReadAsync();

            async Task AwaitPipeReadAsync()
            {
                try
                {
                    var result = await ReadPipeChunkAsync(reader, ct).ConfigureAwait(false);
                    self.Tell(result);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    self.Tell(PipeReadCanceled.Instance);
                }
                catch (Exception ex)
                {
                    self.Tell(new IoTaskFailed(ex));
                }
            }
        }

        /// <summary>
        /// Actor handles a completed pipe read: copy data, deliver to handler,
        /// advance the reader, and optionally request the next read.
        /// </summary>
        private void HandlePipeRead(PipeReadCompleted msg)
        {
            _readPending = false;
            var data = msg.Data;

            if (data.Length > 0)
            {
                _handler!.Tell(new Received(data));

                if (_traceLogging)
                    Log.Debug("Delivered {0} bytes to handler", data.Length);
            }

            if (msg.IsCompleted || msg.IsCanceled)
            {
                // When the completed/canceled read also carried data, do one more
                // non-demand-driven drain read before signaling EOF.  The pipe
                // writer's CompleteAsync flushes any Advance'd-but-not-Flush'd
                // bytes, but PipeReader.ReadAsync may return the previous flush's
                // segment with IsCompleted while a final segment from the flush
                // inside CompleteAsync is not yet visible.  The extra read is
                // guaranteed to be very cheap (synchronous, empty buffer) in the
                // common case and ensures no bytes are silently dropped.
                if (data.Length > 0)
                {
                    if (_traceLogging)
                        Log.Debug("Pipe completed with data — requesting drain read");
                    RequestPipeRead();
                    return;
                }

                // Check for read pump error. Two paths can set this:
                // 1. _readPumpHasError — set by the actor thread when ReadPumpFailed is processed
                // 2. _transport.HasReadError — set by the read pump thread BEFORE completing
                //    the pipe writer, ensuring it's visible here even if the ReadPumpFailed
                //    message hasn't been processed yet
                if (_readPumpHasError || _transport!.HasReadError)
                {
                    // The read pump failed with an I/O error (connection reset, etc.).
                    // Propagate as an I/O error, not as normal EOF.
                    var error = _readPumpError ?? _transport!.ReadError ?? new IOException("Connection reset by peer");
                    if (_traceLogging)
                        Log.Debug("Pipe completed with error — signaling I/O error: {0}",
                            error.Message);
                    HandleIoError(error);
                    return;
                }

                // Normal EOF — peer closed their write side cleanly.
                if (_traceLogging)
                    Log.Debug("Pipe completed — signaling EOF");
                Self.Tell(StreamEof.Instance);
                return;
            }

            // In pull mode: wait for next ResumeReading before reading again.
            // In non-pull mode: keep reading as long as not suspended.
            if (!_pullMode && _readingAllowed)
            {
                RequestPipeRead();
            }
        }

        private void HandlePipeReadCanceled()
        {
            _readPending = false;

            if (_traceLogging)
                Log.Debug("Pipe read cancelled");
        }

        private static async ValueTask<PipeReadCompleted> ReadPipeChunkAsync(PipeReader reader, CancellationToken ct)
        {
            while (true)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                // We must copy out of the pipe's pooled segments before AdvanceTo because
                // Tell is non-blocking — the handler may not have consumed the data by the
                // time the segments are returned to the pool. Zero-copy reads require an
                // explicit ack protocol with the handler that's out of scope here.
                // The result is wrapped in ReadOnlySequence<byte> (single-segment in practice)
                // so downstream Streams stages can chain sequences without further copies.
                ReadOnlySequence<byte> data;
                if (buffer.Length > 0)
                {
                    var array = new byte[checked((int)buffer.Length)];
                    buffer.CopyTo(array);
                    data = new ReadOnlySequence<byte>(array);
                }
                else
                {
                    data = ReadOnlySequence<byte>.Empty;
                }

                reader.AdvanceTo(buffer.End);

                if (data.Length > 0 || result.IsCompleted || result.IsCanceled)
                    return new PipeReadCompleted(data, result.IsCompleted, result.IsCanceled);
            }
        }

        /* ================================================================= */
        /*  Write handling                                                   */
        /* ================================================================= */

        private void HandleWrite(Tcp.WriteCommand cmd)
        {
            if (_closingGracefully)
            {
                Sender.Tell(cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
                return;
            }

            switch (cmd)
            {
                case Write w:
                    EnqueueWrite(w, Sender);
                    break;
                case CompoundWrite compounds:
                    foreach (var c in compounds)
                    {
                        if (c is Write w2)
                        {
                            EnqueueWrite(w2, Sender);
                        }
                        else
                        {
                            Sender.Tell(c.FailureMessage.WithCause(
                                new InvalidOperationException($"Cannot enqueue {c} - only valid classes are Write and CompoundWrite")));
                        }
                    }
                    break;
                default:
                    Sender.Tell(cmd.FailureMessage.WithCause(
                        new InvalidOperationException($"Cannot enqueue {cmd} - only valid classes are Write and CompoundWrite")));
                    break;
            }
        }

        private void BufferWriteBeforeRegister(Tcp.WriteCommand cmd, IActorRef sender)
        {
            switch (cmd)
            {
                case Write w:
                    BufferSingleWriteBeforeRegister(w, sender);
                    break;
                case CompoundWrite compoundWrite:
                    foreach (var part in compoundWrite)
                    {
                        if (part is Write write)
                            BufferSingleWriteBeforeRegister(write, sender);
                        else
                            sender.Tell(part.FailureMessage.WithCause(new InvalidOperationException(
                                $"Cannot buffer {part} before registration - only valid classes are Write and CompoundWrite")));
                    }

                    break;
                default:
                    sender.Tell(cmd.FailureMessage.WithCause(new InvalidOperationException(
                        $"Cannot buffer {cmd} before registration - only valid classes are Write and CompoundWrite")));
                    break;
            }
        }

        private void BufferSingleWriteBeforeRegister(Write write, IActorRef sender)
        {
            var byteCount = (int)write.Bytes;

            if (_maxQueuedBytes >= 0 && _pendingRegistrationBytes + byteCount > _maxQueuedBytes)
            {
                sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseQueueIsFullException));
                return;
            }

            if (byteCount == 0)
            {
                if (write.WantsAck) sender.Tell(write.Ack);
                return;
            }

            Log.Warning("Received Write command before Register command. It will be buffered until Register will be received (buffered write size is {0} bytes)",
                write.Bytes);

            // INVARIANT: TcpConnection never retains caller-owned memory past the message-handler
            // turn; pre-registration writes are copied at enqueue (cold path) — see the WriteAck
            // contract in EnqueueWrite. On the Open-phase path (HandleWrite -> EnqueueWrite), the
            // caller's ReadOnlySequence<byte> is copied into the transport's pipe synchronously,
            // in the same turn the Write message is handled, before WriteAck is sent. A
            // pre-registration write has no such bound: it can sit in _pendingRegistrationWrites
            // for an arbitrary amount of time (until Register arrives, up to
            // Settings.RegisterTimeout) before FlushPendingRegistrationWrites ever touches it. If
            // we queued the caller's sequence as-is, a caller using a pooled/reusable buffer (the
            // whole point of the ReadOnlySequence<byte> Write surface) could safely reuse or
            // mutate it well before that flush, silently corrupting the bytes eventually written
            // to the socket. Copying here — a one-time, cold-path allocation — makes the buffered
            // write's lifetime fully independent of the caller's buffer.
            var copiedData = new ReadOnlySequence<byte>(write.Data.ToArray());
            var bufferedWrite = Write.Create(copiedData, write.Ack);

            _pendingRegistrationWrites.Enqueue(new WriteCommand(bufferedWrite, sender));
            _pendingRegistrationBytes += byteCount;
        }

        private void FlushPendingRegistrationWrites()
        {
            while (_pendingRegistrationWrites.Count > 0)
            {
                var write = _pendingRegistrationWrites.Dequeue();
                _pendingRegistrationBytes -= (int)write.Cmd.Bytes;
                EnqueueWrite(write.Cmd, write.Sender);
            }
        }

        private void EnqueueWrite(Write write, IActorRef sender)
        {
            var byteCount = (int)write.Bytes;

            // Check message size limit — reject writes that exceed the configured maximum.
            // With pipe-based transport, the pipe's pauseWriterThreshold handles flow control.
            // This check prevents a single oversized write from overwhelming the pipe buffer.
            if (_maxQueuedBytes >= 0 && byteCount > _maxQueuedBytes)
            {
                sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseQueueIsFullException));
                return;
            }

            // Handle empty writes immediately
            if (byteCount == 0)
            {
                if (write.WantsAck) sender.Tell(write.Ack);
                return;
            }

            // Write directly to transport — pipe handles buffering and batching.
            // The pipe absorbs writes into its internal buffer (memcpy, not syscall).
            // The write pump flushes the buffer to the socket asynchronously.
            //
            // WriteAck contract: WriteAsync (see TcpTransportConnection) synchronously copies
            // every segment of write.Data into the pipe's internal buffer before returning —
            // the ValueTask<FlushResult> it hands back only tracks the async flush to the
            // socket, not the memcpy. So by the time we send WriteAck below, the caller's
            // memory has already been copied out of and may be safely reused or mutated. This
            // call happens synchronously within the same actor-message-handler turn that
            // received the Write, which is the invariant BufferSingleWriteBeforeRegister's
            // pre-registration copy exists to preserve for writes that can't reach this method
            // in the same turn.
            _transport!.WriteAsync(write.Data, _cts!.Token);

            if (write.WantsAck) sender.Tell(write.Ack);
        }

        /* ================================================================= */
        /*  Shutdown handling                                                */
        /* ================================================================= */

        private void HandleClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            switch (closeEvent)
            {
                case Aborted:
                    if (_traceLogging)
                        Log.Debug("Got Abort command. RESETing connection.");
                    HandleAbort(closeSender);
                    break;

                case ErrorClosed:
                    DoCloseConnection(closeSender, closeEvent);
                    break;

                case PeerClosed when _keepOpenOnPeerClosed:
                    _handler?.Tell(PeerClosed.Instance);
                    _peerClosed = true;
                    Become(PeerSentEofBehaviour);
                    break;

                case ConfirmedClosed:
                    if (_traceLogging)
                        Log.Debug("Got ConfirmedClose command, sending FIN.");
                    HandleConfirmedClose(closeSender);
                    break;

                default:
                    if (_traceLogging)
                        Log.Debug("Got Close command, closing connection.");
                    HandleGracefulClose(closeSender, closeEvent!);
                    break;
            }
        }

        /// <summary>
        /// Tcp.Close: flush pending writes, then close everything.
        /// </summary>
        private void HandleGracefulClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            _closingGracefully = true;

            // Ask the transport to close (flushes writes, closes connection)
            if (_transport != null)
            {
                _transport.CloseAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        return (object)new TransportOperationFailed(t.Exception!.InnerException ?? t.Exception);
                    return TransportOperationCompleted.Instance;
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
            }

            // Transition to closing behaviour
            Become(() => ClosingBehaviour(closeSender, closeEvent));
        }

        /// <summary>
        /// Tcp.Abort: cancel everything immediately.
        /// </summary>
        private void HandleAbort(IActorRef closeSender)
        {
            _closingGracefully = true;

            // Cancel CTS immediately
            TryCancelCts();

            // Abort the transport (sends RST)
            try { _transport?.Abort(); }
            catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 transport may already be disposed

            StopWith(new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), Aborted.Instance));
        }

        /// <summary>
        /// Tcp.ConfirmedClose: half-close (send FIN), wait for peer FIN.
        /// The sequence is: flush writes -> shutdown output (FIN) -> wait for peer FIN (StreamEof).
        /// </summary>
        private void HandleConfirmedClose(IActorRef closeSender)
        {
            _closingGracefully = true;

            // Ask the transport to shutdown (flush writes, send FIN, keep reading)
            if (_transport != null)
            {
                _transport.ShutdownAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        return (object)new TransportOperationFailed(t.Exception!.InnerException ?? t.Exception);
                    return TransportOperationCompleted.Instance;
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
            }

            // Enter ClosingBehaviour to wait for transport shutdown, then peer FIN
            Become(() => ClosingBehaviour(closeSender, ConfirmedClosed.Instance));
        }

        /// <summary>
        /// Handle EOF from the pipe read (transport's input pipe completed normally).
        /// </summary>
        private void HandleStreamEof()
        {
            if (_peerClosed)
            {
                // Duplicate EOF — already handled, ignore
                if (_traceLogging)
                    Log.Debug("HandleStreamEof: duplicate EOF, ignoring");
                return;
            }

            _peerClosed = true;

            if (_traceLogging)
                Log.Debug("HandleStreamEof: peer closed");

            if (_outputShutdown)
            {
                // Both sides closed - connection is fully closed
                DoCloseConnection(_handler ?? _commander!, ConfirmedClosed.Instance);
            }
            else
            {
                HandleClose(_handler ?? _commander!, PeerClosed.Instance);
            }
        }

        /// <summary>
        /// Handle I/O errors from background tasks.
        /// </summary>
        private void HandleIoError(Exception cause)
        {
            Log.Debug(cause, "Closing connection due to I/O error");
            var errorClosed = new ErrorClosed(cause.Message);

            // Cancel everything
            TryCancelCts();

            if (_closeInformation != null)
            {
                _closeInformation = _closeInformation with { ClosedEvent = errorClosed };
            }
            else
            {
                _closeInformation = CloseInformation.Single(_handler ?? _commander!, errorClosed);
            }

            Context.Stop(Self);
        }

        private void TryCancelCts()
        {
            if (Interlocked.CompareExchange(ref _shutdownState, ShutdownInitiated, ShutdownNone) == ShutdownNone)
            {
                var cts = _cts;
                if (cts is null)
                    return;

                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void DoCloseConnection(IActorRef closeSender, ConnectionClosed closedEvent)
        {
            TryCancelCts();

            switch (closedEvent)
            {
                case Aborted:
                    try { _transport?.Abort(); }
                    catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 transport may already be disposed
                    break;
                default:
                    // Transport handles socket shutdown via CloseAsync/ShutdownAsync
                    break;
            }

            StopWith(new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), closedEvent));
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
