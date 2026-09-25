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
        /// Self-tell: the transport's WRITE pump (<see cref="ITransportConnection.WriteCompleted"/>)
        /// failed with an I/O error (e.g. a broken pipe/connection reset discovered while flushing
        /// a previously-buffered write to the socket, well after <see cref="EnqueueWrite"/> already
        /// returned).
        ///
        /// <para>
        /// <b>Why this exists.</b> Unlike the read side, nothing previously observed
        /// <see cref="ITransportConnection.WriteCompleted"/> proactively -- a write-pump failure on
        /// a connection with no FURTHER write attempts (e.g. an otherwise-idle one-way outbound
        /// connection) would never surface at all: <see cref="EnqueueWrite"/> only discovers a
        /// stale failure reactively, via a synchronous re-throw from the pipe on the NEXT write
        /// attempt, which may never come. This mirrors <see cref="ReadPumpFailed"/>'s monitoring
        /// (see <see cref="StartTransport"/>) so a write-side failure is detected promptly even
        /// when nothing is actively writing.
        /// </para>
        /// </summary>
        private sealed class WritePumpFailed : INoSerializationVerificationNeeded
        {
            public Exception Cause { get; }
            public WritePumpFailed(Exception cause) { Cause = cause; }
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

        /// <summary>
        /// Self-tell: the output pipe's pending flush completed, so it is back under its pause threshold.
        /// </summary>
        private sealed class FlushCompleted : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            public static readonly FlushCompleted Instance = new();
            private FlushCompleted() { }
        }

        /// <summary>
        /// Self-tell: the output pipe's pending flush failed or found the output closed.
        /// </summary>
        private sealed class FlushFailed : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            public Exception Cause { get; }
            public FlushFailed(Exception cause) { Cause = cause; }
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

        // Writes waiting for Register, or for the output pipe's pending flush. FIFO.
        private readonly Queue<WriteCommand> _pendingWrites = new();
        private int _pendingWriteBytes;

        // At most one output-pipe flush is awaited at a time; _flushWaiter's ack waits on it.
        private bool _flushPending;
        private WriteCommand? _flushWaiter;
        private IActorRef? _resumeWritingSender;

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

        // Non-null once a later Tcp.Close upgraded an in-flight ConfirmedClose to a full
        // close: its sender, notified alongside closeSender once the connection finishes.
        private IActorRef? _fullCloseCommander;
        #endregion

        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        private static readonly IOException OutputClosedException =
            new("The connection's output was closed before the write was flushed");

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
                // Graceful close only when the close actually completed: ConfirmedClosed, or Closed
                // after an upgrade. Anything else aborts below; CloseAsync could wait on a stuck write.
                if (_closeInformation?.ClosedEvent is ConfirmedClosed ||
                    (_fullCloseCommander is not null && _closeInformation?.ClosedEvent is Closed))
                {
                    // Only ShutdownAsync ran, so the socket is still open: close it without Abort's
                    // linger-0 RST. Fire-and-forget keeps PostStop non-blocking; the fault is observed below.
                    var transport = _transport;
                    transport.CloseAsync().ContinueWith(t =>
                    {
                        if (!t.IsFaulted)
                            return;

                        // read the exception so it is observed
                        var ex = t.Exception;
                        if (_traceLogging)
                            Log.Debug(ex, "Best-effort graceful CloseAsync after ConfirmedClosed observed a fault");

                        // CloseAsync failed partway; abort so the socket isn't leaked
                        try { transport.Abort(); }
                        catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 transport may already be disposed
                    }, TaskScheduler.Default);
                }
                else
                {
                    // Abort cancels the CTS, sets linger=0, closes the socket.
                    // This unblocks any pending stream.ReadAsync/WriteAsync in the pump tasks.
                    // The pump tasks will exit with OperationCanceledException or IOException.
                    try { _transport.Abort(); }
                    catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 transport may already be disposed
                }
            }
            else
            {
                // Transport was never created (e.g. PoisonPill before Register).
                // Close the socket directly since no transport owns it.
                try { Socket.Close(); }
                catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket may already be disposed
            }

            // Its bytes are already in the pipe (segments freed after the copy), but the flush never completed.
            if (_flushWaiter is { } waiter)
                waiter.Sender.Tell(waiter.Cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));

            while (_pendingWrites.Count > 0)
            {
                var write = _pendingWrites.Dequeue();
                // Disposal path: PostStop/drain. The connection is tearing down and this queued
                // write will never reach the pipe — dispose any owner it carries
                // before notifying the sender of failure, same as every other rejection path.
                write.Cmd.Data.DisposeOwnedSegments();
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
            _ = MonitorWritePumpAsync();

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

            // Proactively observe the write pump too (see WritePumpFailed's remarks) -- a
            // GRACEFUL write-side completion (this system deliberately calling ShutdownAsync/
            // CloseAsync/DisposeAsync) is already tracked by those callers' own explicit awaits
            // (e.g. HandleConfirmedClose's ContinueWith), so only a FAULT here is actionable;
            // a normal (non-faulted) completion is a no-op from this monitor's perspective.
            async Task MonitorWritePumpAsync()
            {
                try
                {
                    await transport.WriteCompleted.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    self.Tell(new WritePumpFailed(ex));
                }
            }
        }

        /// <summary>
        /// Creates the transport connection. Subclasses must supply this.
        /// For incoming connections, wraps the accepted socket.
        /// For outgoing connections, wraps the connected socket (possibly with TLS).
        /// </summary>
        protected abstract ITransportConnection CreateTransport();

        /// <summary>
        /// Resolves the resume-writer threshold (in bytes) for this connection's input pipe, i.e.
        /// the <see cref="System.IO.Pipelines.Pipe"/> that buffers bytes read from the socket before
        /// the actor drains them. The pause-writer threshold applied by callers is twice this value.
        /// </summary>
        /// <remarks>
        /// If <paramref name="options"/> contains an <see cref="Inet.SO.PipeBufferSize"/>, its
        /// <see cref="Inet.SO.PipeBufferSize.Size"/> wins (the LAST one, if more than one is present --
        /// mirroring how later options generally override earlier ones for the same knob). Otherwise
        /// this falls back to <paramref name="settings"/>'s <see cref="TcpSettings.ReceiveBufferSize"/>,
        /// preserving the pre-existing default for every Akka.IO TCP connection that doesn't opt in.
        /// </remarks>
        internal static int ResolvePipeBufferSize(TcpSettings settings, IEnumerable<Inet.SocketOption> options)
        {
            var size = settings.ReceiveBufferSize;
            foreach (var option in options)
            {
                if (option is Inet.SO.PipeBufferSize pipeBufferSize)
                    size = pipeBufferSize.Size;
            }

            return size;
        }

        /// <summary>
        /// Output pipe options when <paramref name="options"/> sets <see cref="Inet.SO.PipeBufferSize"/>
        /// (same watermarks as the input pipe); otherwise null, which keeps <see cref="PipeOptions.Default"/>
        /// (pause at 64 KB, resume at 32 KB).
        /// </summary>
        internal static PipeOptions? ResolveOutputPipeOptions(IEnumerable<Inet.SocketOption> options)
        {
            var size = options.OfType<Inet.SO.PipeBufferSize>().LastOrDefault()?.Size;
            return size is { } s
                ? new PipeOptions(pauseWriterThreshold: s * 2L, resumeWriterThreshold: s, useSynchronizationContext: false)
                : null;
        }

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

                DrainPendingWrites();

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
            Receive<WritePumpFailed>(msg => HandleIoError(msg.Cause));
            Receive<FlushCompleted>(_ => OnFlushCompleted());
            Receive<FlushFailed>(msg => HandleIoError(msg.Cause));
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
            Receive<WritePumpFailed>(msg => HandleIoError(msg.Cause));
            Receive<FlushCompleted>(_ => OnFlushCompleted());
            Receive<FlushFailed>(msg => HandleIoError(msg.Cause));
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
                // Disposal path: rejected while closing. The write never reaches the pipe, so
                // dispose any owner(s) it carries before signaling failure.
                DisposeOwnedSegments(w);
                Sender.Tell(w.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            });
            Receive<Abort>(c => HandleClose(Sender, c.Event));
            Receive<Close>(_ =>
            {
                // Only meaningful once, while still waiting on the ConfirmedClose's peer FIN.
                if (closeEvent is not ConfirmedClosed || _fullCloseCommander is not null)
                    return;

                if (_traceLogging)
                    Log.Debug("Got Close while ConfirmedClose was draining - upgrading to a full close instead of waiting for the peer's FIN.");

                _fullCloseCommander = Sender;
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<ReadPumpFailed>(msg =>
            {
                HandleReadPumpFailed(msg);

                if (closeEvent is ConfirmedClosed)
                {
                    // The read side failed outright (e.g. connection reset) instead of
                    // reaching a clean peer FIN, so _peerClosed will never become true -
                    // TryFinishClose's ConfirmedClosed branch would otherwise wait forever.
                    // Concretely: reading suspended (pull mode, or an explicit
                    // SuspendReading) means no PipeReadCompleted/StreamEof is ever in
                    // flight to surface this any other way, so this handler is the only
                    // place that ever learns about it. Report the real outcome instead of
                    // hanging.
                    DoCloseConnection(closeSender, new ErrorClosed(msg.Cause.Message));
                    return;
                }

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

                if (_traceLogging)
                {
                    Log.Debug(closeEvent is ConfirmedClosed
                        ? "Peer FIN received during ConfirmedClose - checking whether our own output has drained"
                        : "EOF received during close - waiting for transport to finish");
                }

                // Don't close here directly - TryFinishClose is the single place that decides
                // whether every condition for this closeEvent has been met. For ConfirmedClose
                // that means BOTH our own output has drained (_outputShutdown) AND the peer's
                // FIN has arrived (_peerClosed, just set above) - the peer's FIN landing first
                // must not race ahead of our own still-draining writes.
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            Receive<TransportOperationCompleted>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("Transport operation completed during close");

                // For ConfirmedClose (half-close), transport has flushed writes and sent FIN.
                // For a regular Close, transport is fully closed. Either way, set the flag and
                // let TryFinishClose decide whether every condition for this closeEvent has
                // been met - for ConfirmedClose that also means the peer's FIN (_peerClosed)
                // may already have arrived (e.g. via keepOpenOnPeerClosed, before this
                // ConfirmedClose was even requested), in which case this is the message that
                // finishes the close.
                _outputShutdown = true;

                if (_traceLogging && closeEvent is ConfirmedClosed)
                    Log.Debug("ConfirmedClose: FIN sent, waiting for peer FIN (if not already received)");

                TryFinishClose(closeSender, closeEvent);
            });
            Receive<TransportOperationFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("Transport operation failed during close: {0}", msg.Cause.Message);
                // The drain (flush pending writes / send FIN) failed, so whatever the caller
                // requested (Closed / ConfirmedClosed) did NOT actually happen - reporting that
                // event here would tell the caller writes were delivered when they may not have
                // been. Report the real outcome instead.
                DoCloseConnection(closeSender, new ErrorClosed(msg.Cause.Message));
            });
            Receive<IoTaskFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("I/O task failed during close: {0}", msg.Cause.Message);
                DoCloseConnection(closeSender, new ErrorClosed(msg.Cause.Message));
            });
            Receive<WritePumpFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("Write pump failed during close: {0}", msg.Cause.Message);
                DoCloseConnection(closeSender, new ErrorClosed(msg.Cause.Message));
            });
            Receive<FlushCompleted>(_ =>
            {
                OnFlushCompleted();
                TryStartTransportClose(closeEvent);
            });
            Receive<FlushFailed>(msg => DoCloseConnection(closeSender, new ErrorClosed(msg.Cause.Message)));
            SuspendResumeHandlers();
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died during close, stopping connection actor", _handler);
                Context.Stop(Self);
            });

            TryStartTransportClose(closeEvent);

            // If read pump already completed before we entered ClosingBehaviour, try to close now
            TryFinishClose(closeSender, closeEvent);
        }

        /// <summary>
        /// Checks whether all conditions are met to finalize the connection close.
        /// For ConfirmedClose, we close once BOTH our own output has drained
        /// (<see cref="_outputShutdown"/> - pending writes flushed, FIN sent) AND the peer's FIN
        /// has arrived (<see cref="_peerClosed"/>). Either condition can be the one still
        /// outstanding when this is called - the peer's FIN can land before our drain finishes
        /// (<c>StreamEof</c> calls this), or the drain can finish before/without ever seeing
        /// another <c>StreamEof</c> because the peer's FIN already arrived earlier, e.g. via
        /// <c>keepOpenOnPeerClosed</c> (<c>TransportOperationCompleted</c> calls this). Checking
        /// both flags from both call sites means whichever condition completes last is the one
        /// that finishes the close, and neither a premature "success" nor a permanent hang can
        /// occur.
        /// Unless a later Tcp.Close upgraded this ConfirmedClose (<see cref="_fullCloseCommander"/>
        /// non-null): then the peer's FIN no longer matters, and we finish once
        /// <see cref="_outputShutdown"/> alone is true, reporting <see cref="Closed"/> instead of
        /// <see cref="ConfirmedClosed"/> - same caveat as a plain Close: unread inbound data can
        /// still turn this into a reset.
        /// For regular Close, we close once the read pump has completed and transport is done.
        /// </summary>
        private void TryFinishClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            if (closeEvent is ConfirmedClosed)
            {
                if (_fullCloseCommander is not null)
                {
                    if (_outputShutdown)
                        DoCloseConnection(closeSender, Closed.Instance);
                    return;
                }

                if (_outputShutdown && _peerClosed)
                    DoCloseConnection(closeSender, ConfirmedClosed.Instance);
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
                if (_flushPending || _pendingWrites.Count > 0)
                    _resumeWritingSender = Sender;
                else
                    Sender.Tell(WritingResumed.Instance);
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

        /// <summary>
        /// Disposes every owner-carrying segment reachable from <paramref name="cmd"/>'s data —
        /// <see cref="Write.Data"/> directly for a single write, or every constituent
        /// <see cref="Write"/>'s <see cref="Write.Data"/> for a <see cref="CompoundWrite"/>. Used on
        /// rejection paths where the whole command (which may bundle several writes) is being
        /// dropped without ever reaching the transport.
        /// </summary>
        private static void DisposeOwnedSegments(Tcp.WriteCommand cmd)
        {
            switch (cmd)
            {
                case Write w:
                    w.Data.DisposeOwnedSegments();
                    break;
                case CompoundWrite compound:
                    foreach (var part in compound)
                    {
                        if (part is Write w2)
                            w2.Data.DisposeOwnedSegments();
                    }
                    break;
            }
        }

        private void HandleWrite(Tcp.WriteCommand cmd)
        {
            if (_closingGracefully)
            {
                // Disposal path: rejected because a graceful close is already underway. The write
                // never reaches the pipe, so dispose any owner(s) it carries before signaling failure.
                DisposeOwnedSegments(cmd);
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

            if (_maxQueuedBytes >= 0 && _pendingWriteBytes + byteCount > _maxQueuedBytes)
            {
                // Disposal path: queue-full rejection (pre-registration). The write never reaches
                // the pipe, so dispose any owner(s) it carries before signaling failure.
                write.Data.DisposeOwnedSegments();
                sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseQueueIsFullException));
                return;
            }

            if (byteCount == 0)
            {
                // Disposal path: empty write, nothing to buffer. In practice an owner-carrying
                // segment always has non-zero length, but dispose defensively here too so a
                // degenerate zero-length owned write can never leak.
                write.Data.DisposeOwnedSegments();
                if (write.WantsAck) sender.Tell(write.Ack);
                return;
            }

            Log.Warning("Received Write command before Register command. It will be buffered until Register will be received (buffered write size is {0} bytes)",
                write.Bytes);

            QueueWrite(write, sender);
        }

        /// <summary>
        /// Queues a write until Register arrives or the pending output-pipe flush completes.
        /// </summary>
        private void QueueWrite(Write write, IActorRef sender)
        {
            // A queued write outlives this message-handler turn, and TcpConnection never holds a
            // borrowed buffer past the turn (#8323), so copy it. An OWNED buffer was handed over, so
            // queue it as-is; WriteToPipe or PostStop disposes it. A write that mixes borrowed and
            // owned segments is not copied (see OwnedSequenceSegment.cs).
            var queuedWrite = write.Data.HasOwnedSegments()
                ? write
                : Write.Create(new ReadOnlySequence<byte>(write.Data.ToArray()), write.Ack);

            _pendingWrites.Enqueue(new WriteCommand(queuedWrite, sender));
            _pendingWriteBytes += (int)write.Bytes;
        }

        private void DrainPendingWrites()
        {
            // Stop at a pending flush, or once a write failure has started shutdown (PostStop fails the rest).
            while (!_flushPending && _pendingWrites.Count > 0 && _shutdownState == ShutdownNone)
            {
                var write = _pendingWrites.Dequeue();
                _pendingWriteBytes -= (int)write.Cmd.Bytes;
                WriteToPipe(write.Cmd, write.Sender);
            }
        }

        private void EnqueueWrite(Write write, IActorRef sender)
        {
            // write-commands-queue-max-size caps a single write. Backpressure comes from WriteToPipe
            // holding back the ack while the output pipe is over its pause threshold.
            if (_maxQueuedBytes >= 0 && write.Bytes > _maxQueuedBytes)
            {
                // Disposal path: queue-full rejection (open/registered path). The write never
                // reaches the pipe, so dispose any owner(s) it carries before signaling failure.
                write.Data.DisposeOwnedSegments();
                sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseQueueIsFullException));
                return;
            }

            // Keep FIFO order: while a flush is pending, every later write (empty ones too) waits.
            if (_flushPending || _pendingWrites.Count > 0)
            {
                QueueWrite(write, sender);
                return;
            }

            WriteToPipe(write, sender);
        }

        private void WriteToPipe(Write write, IActorRef sender)
        {
            if (write.Bytes == 0)
            {
                // Disposal path: empty write, never reaches WriteAsync. Defensive - see the
                // matching byteCount == 0 branch in BufferSingleWriteBeforeRegister.
                write.Data.DisposeOwnedSegments();
                if (write.WantsAck) sender.Tell(write.Ack);
                return;
            }

            // WriteAsync (see TcpTransportConnection) copies write.Data into the output pipe before
            // it returns. The ValueTask it returns tracks the flush, which stays pending while the
            // pipe is over its pause threshold. The ack waits for it, so a WriteAck means the bytes
            // were accepted into the bounded output buffer.
            //
            // A SYNCHRONOUS throw here (not merely a faulted ValueTask) means the
            // transport's write pump (TcpTransportConnection.RunWritePumpAsync) has ALREADY hit a
            // fatal socket error (e.g. a broken pipe/connection reset after the peer vanished)
            // and completed the pipe's reader WITH that exception -- PipeWriter.Write/FlushAsync
            // re-throws it synchronously on the very next call. Left uncaught, this exception
            // used to escape into the actor's normal message-processing turn as an UNHANDLED
            // exception: the default supervisor strategy would try to Restart this actor, and
            // PostRestart (above) deliberately forbids that ("Restarting not supported for
            // connection actors"), turning an ordinary peer-disconnect into a PostRestartException
            // that escalates to this actor's supervisor instead of a graceful connection close.
            // Route it through the SAME graceful teardown every other I/O failure on this actor
            // uses (HandleIoError: notify the handler/commander with ErrorClosed, stop self) --
            // see design.md group 9's reconnect correctness suite ("kill the peer mid-traffic"),
            // which is what first exercised this path.
            ValueTask<FlushResult> flush;
            try
            {
                flush = _transport!.WriteAsync(write.Data, _cts!.Token);
            }
            catch (Exception ex)
            {
                // Disposal path: open/registered path, WriteAsync threw synchronously. Per the
                // contract documented above, a synchronous throw here means the pipe's writer was
                // already completed (with an exception) BEFORE this call — i.e. no bytes of this
                // write reached the pipe. Safe (and required) to dispose the same as the success
                // path: either a given segment's bytes were copied before the throw (copy done,
                // safe to free the source) or they never were (never reached the pipe, so nothing
                // downstream can be reading them).
                write.Data.DisposeOwnedSegments();
                sender.Tell(write.FailureMessage.WithCause(ex));
                HandleIoError(ex);
                return;
            }

            // Disposal path: open/registered path. The bytes are in the pipe now, so free the
            // caller's segments even if the ack waits on the flush.
            write.Data.DisposeOwnedSegments();

            if (flush.IsCompletedSuccessfully)
            {
                var result = flush.Result;
                if (result.IsCompleted || result.IsCanceled)
                {
                    // The write pump has exited, so these bytes will never reach the socket.
                    sender.Tell(write.FailureMessage.WithCause(OutputClosedException));
                    HandleIoError(OutputClosedException);
                    return;
                }

                if (write.WantsAck) sender.Tell(write.Ack);
                return;
            }

            // Only one flush may be awaited at a time: awaiting a second makes the first throw.
            _flushPending = true;
            _flushWaiter = new WriteCommand(write, sender);
            _ = AwaitFlushAsync(flush, Self);

            static async Task AwaitFlushAsync(ValueTask<FlushResult> flush, IActorRef self)
            {
                try
                {
                    var result = await flush.ConfigureAwait(false);
                    if (result.IsCompleted || result.IsCanceled)
                        self.Tell(new FlushFailed(OutputClosedException));
                    else
                        self.Tell(FlushCompleted.Instance);
                }
                catch (Exception ex)
                {
                    self.Tell(new FlushFailed(ex));
                }
            }
        }

        private void OnFlushCompleted()
        {
            _flushPending = false;
            if (_flushWaiter is { } waiter && waiter.Cmd.WantsAck)
                waiter.Sender.Tell(waiter.Cmd.Ack);
            _flushWaiter = null;

            DrainPendingWrites();

            if (_resumeWritingSender != null && !_flushPending && _pendingWrites.Count == 0)
            {
                _resumeWritingSender.Tell(WritingResumed.Instance);
                _resumeWritingSender = null;
            }
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
        /// Tcp.Close: flush pending writes, then close everything (see <see cref="TryStartTransportClose"/>).
        /// </summary>
        private void HandleGracefulClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            _closingGracefully = true;
            Become(() => ClosingBehaviour(closeSender, closeEvent));
        }

        /// <summary>
        /// Starts the transport's CloseAsync (ShutdownAsync for ConfirmedClose) once every queued
        /// write is in the output pipe, since both complete the pipe. ClosingBehaviour calls this
        /// on entry and after each FlushCompleted.
        /// </summary>
        private void TryStartTransportClose(ConnectionClosed closeEvent)
        {
            if (_transport == null || _flushPending || _pendingWrites.Count > 0 || _shutdownState != ShutdownNone)
                return;

            var operation = closeEvent is ConfirmedClosed ? _transport.ShutdownAsync() : _transport.CloseAsync();
            operation.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    return (object)new TransportOperationFailed(t.Exception!.InnerException ?? t.Exception);
                return TransportOperationCompleted.Instance;
            }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
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

            // _outputShutdown can never be true here: it is only ever set inside
            // ClosingBehaviour's TransportOperationCompleted handler, and reaching
            // ClosingBehaviour requires HandleClose to already have run (via HandleGracefulClose
            // or HandleConfirmedClose) - at which point StreamEof is handled by
            // ClosingBehaviour's own handler (which calls TryFinishClose), not this method.
            // This method only ever runs from OpenBehaviour/PeerSentEofBehaviour, i.e. before
            // any Close/ConfirmedClose has been requested.
            HandleClose(_handler ?? _commander!, PeerClosed.Instance);
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

            // Also notify the Close that upgraded an in-flight ConfirmedClose, if any.
            var notificationsTo = ImmutableHashSet<IActorRef>.Empty.Add(closeSender);
            if (_fullCloseCommander is not null)
                notificationsTo = notificationsTo.Add(_fullCloseCommander);
            StopWith(new CloseInformation(notificationsTo, closedEvent));
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
