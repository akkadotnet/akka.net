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
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;

#nullable enable

namespace Akka.IO
{
    using static Akka.IO.Tcp;

    /// <summary>
    /// INTERNAL API: Base class for TcpIncomingConnection and TcpOutgoingConnection.
    ///
    /// TcpConnection is an actor abstraction over a single TCP connection using
    /// <see cref="System.IO.Pipelines.Pipe"/> for read buffering and
    /// <see cref="System.Threading.Channels.Channel{T}"/> for write command queuing.
    ///
    /// Three background tasks coordinate through actor mailbox (self-tell on completion/error):
    ///
    /// - ReadFromStream: reads from the network stream into the Pipe writer
    /// - ReadFromPipe: reads from the Pipe reader, copies to pooled buffers, emits Tcp.Received
    /// - WriteToStream: dequeues write commands, batches outbound writes opportunistically,
    ///   calls stream.WriteAsync, delivers ACKs
    ///
    /// All shutdown and error handling flows through the actor mailbox for thread safety.
    /// </summary>
    internal abstract class TcpConnection : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        #region Internal messages

        /// <summary>
        /// Self-tell: all background I/O tasks have completed.
        /// </summary>
        private sealed class IoTasksCompleted : INoSerializationVerificationNeeded
        {
            public static readonly IoTasksCompleted Instance = new();
            private IoTasksCompleted() { }
        }

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
            public ReadOnlyMemory<byte> Data { get; }
            public bool IsCompleted { get; }
            public bool IsCanceled { get; }

            public PipeReadCompleted(ReadOnlyMemory<byte> data, bool isCompleted, bool isCanceled)
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

        private sealed class ReadStreamCompleted : INoSerializationVerificationNeeded
        {
            public static readonly ReadStreamCompleted Instance = new();
            private ReadStreamCompleted() { }
        }

        /// <summary>
        /// Self-tell: all pending writes have been flushed (write channel completed + drained).
        /// </summary>
        private sealed class WritesFlushed : INoSerializationVerificationNeeded
        {
            public static readonly WritesFlushed Instance = new();
            private WritesFlushed() { }
        }

        /// <summary>
        /// Self-tell: the read-from-stream task encountered an I/O error.
        /// Distinct from IoTaskFailed because this carries the error that caused pipe completion
        /// and must be processed BEFORE PipeReadCompleted to distinguish error-EOF from normal EOF.
        /// </summary>
        private sealed class StreamReadFailed : INoSerializationVerificationNeeded
        {
            public Exception Cause { get; }
            public StreamReadFailed(Exception cause) { Cause = cause; }
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

        private const int MaxWriteCommandsPerBatch = 128;

        private readonly bool _traceLogging;
        private readonly bool _pullMode;
        private readonly int _maxQueuedBytes;
        private readonly int _writeBatchSizeBytes;

        // Pipe for read buffering
        private Pipe? _pipe;

        // Write channel
        private Channel<WriteCommand>? _writeChannel;

        // Background tasks (pipe reading is actor-driven, not a background task)
        private Task? _readFromStreamTask;
        private Task? _writeToStreamTask;

        // CTS for background task cancellation
        private CancellationTokenSource? _cts;

        // Shutdown guard - ensures only one shutdown path executes
        private int _shutdownState = ShutdownNone;

        // Stream for network I/O (set by subclass via StartIoTasks)
        private Stream? _stream;

        // Reading flow control — all state managed in actor thread, no synchronization needed
        private PipeReader? _pipeReader;
        private bool _readingAllowed;
        private bool _readPending; // true when a PipeReader.ReadAsync is in flight

        // Actor references
        private IActorRef? _commander;
        private IActorRef? _handler;
        private CloseInformation? _closeInformation;

        // Queued bytes tracking for write backpressure
        private int _queuedBytes;
        private int _pendingRegistrationBytes;

        private readonly Queue<WriteCommand> _pendingRegistrationWrites = new();

        // State flags
        private bool _peerClosed;
        private bool _outputShutdown;
        private bool _keepOpenOnPeerClosed;
        private bool _closingGracefully;
        private bool _readStreamCompleted;
        private bool _writesFlushed;

        // Tracks error from ReadFromStream so HandlePipeRead can distinguish error-EOF from normal EOF.
        // _readStreamError is set from the actor thread when StreamReadFailed is processed.
        // _readStreamErrorVolatile is set from the background thread when the error occurs,
        // BEFORE the pipe writer is completed. This ensures HandlePipeRead can always detect
        // the error even if the StreamReadFailed message hasn't been processed yet.
        private Exception? _readStreamError;
        private volatile bool _readStreamHasError;

        private long _totalSentBytes;
        private long _totalReceivedBytes;

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
            _writeBatchSizeBytes = settings.MaxFrameSizeBytes;
            _pullMode = pullMode;
            _traceLogging = Settings.TraceLogging;
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        /* ================================================================= */
        /*  Base-class public API                                            */
        /* ================================================================= */

        protected override void PostStop()
        {
            // Best-effort cleanup - cancel everything and close
            TryCancelCts();

            var closedEvent = _closeInformation?.ClosedEvent;
            if (closedEvent is Aborted or ErrorClosed)
                AbortSocket();
            else
                CloseSocket();

            DisposeStream(_stream);
            CompletePipeWriter(_pipe?.Writer);
            CompletePipeReader(_pipe?.Reader);
            _writeChannel?.Writer.TryComplete();

            while (_pendingRegistrationWrites.Count > 0)
            {
                var write = _pendingRegistrationWrites.Dequeue();
                write.Sender.Tell(write.Cmd.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
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

        private static bool DisposeStream(Stream? stream)
        {
            if (stream is null)
                return false;

            try
            {
                stream.Dispose();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool CompletePipeWriter(PipeWriter? writer)
        {
            if (writer is null)
                return false;

            try
            {
                writer.Complete();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool CompletePipeReader(PipeReader? reader)
        {
            if (reader is null)
                return false;

            try
            {
                reader.Complete();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
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
        /// Starts the background I/O tasks using the provided stream.
        /// Called after registration is complete.
        /// </summary>
        protected void StartIoTasks(Stream stream)
        {
            _stream = stream;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Configure pipe with backpressure thresholds
            var pipeOptions = new PipeOptions(
                pauseWriterThreshold: Settings.ReceiveBufferSize * 2,
                resumeWriterThreshold: Settings.ReceiveBufferSize,
                useSynchronizationContext: false);

            _pipe = new Pipe(pipeOptions);
            _pipeReader = _pipe.Reader;

            // Bounded write channel
            var channelOptions = new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            };
            _writeChannel = Channel.CreateBounded<WriteCommand>(channelOptions);

            // Start background tasks (pipe reading is actor-driven via PipeTo)
            _readFromStreamTask = ReadFromStreamAsync(_stream, _pipe.Writer, ct);
            _writeToStreamTask = WriteToStreamAsync(_stream, _writeChannel.Reader, ct);

            // Track background tasks - self-tell on completion
            var self = Self;
            _ = NotifyWhenIoTasksCompleteAsync();

            async Task NotifyWhenIoTasksCompleteAsync()
            {
                try
                {
                    await Task.WhenAll(_readFromStreamTask, _writeToStreamTask).ConfigureAwait(false);
                    self.Tell(IoTasksCompleted.Instance);
                }
                catch (Exception ex)
                {
                    var aggregate = ex as AggregateException;
                    self.Tell(new IoTaskFailed(aggregate?.InnerExceptions.FirstOrDefault() ?? ex));
                }
            }
        }

        /// <summary>
        /// Provides the Stream for I/O - subclasses must supply this.
        /// For incoming connections, it's the NetworkStream wrapping the accepted socket.
        /// For outgoing connections, it's the stream from IStreamProvider or a NetworkStream.
        /// </summary>
        protected abstract Stream GetStream();

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

                // Start the I/O tasks now that we have a handler
                var stream = GetStream();
                StartIoTasks(stream);

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
            Receive<StreamReadFailed>(msg => HandleStreamReadFailed(msg));
            Receive<ReadStreamCompleted>(_ => HandleReadStreamCompleted());
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            SuspendResumeHandlers();
            Receive<StreamEof>(_ => HandleStreamEof());
            Receive<IoTaskFailed>(msg => HandleIoError(msg.Cause));
            Receive<IoTasksCompleted>(_ =>
            {
                if (_traceLogging) Log.Debug("[TcpConnection] All I/O tasks completed");
            });
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
            Receive<StreamReadFailed>(msg => HandleStreamReadFailed(msg));
            Receive<ReadStreamCompleted>(_ => HandleReadStreamCompleted());
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            Receive<StreamEof>(_ =>
            {
                // Already in PeerSentEof state — this is a duplicate notification, ignore
                if (_traceLogging) Log.Debug("[TcpConnection] StreamEof in PeerSentEofBehaviour (no-op)");
            });
            SuspendResumeHandlers();
            Receive<IoTaskFailed>(msg => HandleIoError(msg.Cause));
            Receive<IoTasksCompleted>(_ =>
            {
                if (_traceLogging) Log.Debug("[TcpConnection] All I/O tasks completed (peer EOF)");
            });
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died, stopping connection actor", _handler);
                Context.Stop(Self);
            });
        }

        private void ClosingBehaviour(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            // We're shutting down - reject new writes, wait for tasks to complete
            Receive<Tcp.WriteCommand>(w =>
            {
                Sender.Tell(w.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            });
            Receive<Abort>(c => HandleClose(Sender, c.Event));
            Receive<StreamReadFailed>(msg => HandleStreamReadFailed(msg));
            Receive<ReadStreamCompleted>(_ =>
            {
                HandleReadStreamCompleted();
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<StreamEof>(_ =>
            {
                _peerClosed = true;

                if (closeEvent is ConfirmedClosed)
                {
                    if (_traceLogging)
                        Log.Debug("[TcpConnection] Peer FIN received during ConfirmedClose - connection fully closed");
                    DoCloseConnection(closeSender, ConfirmedClosed.Instance);
                    return;
                }

                if (_traceLogging)
                    Log.Debug("[TcpConnection] EOF received during close - waiting for writes/tasks to finish");

                TryFinishClose(closeSender, closeEvent);
            });
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<PipeReadCanceled>(_ => HandlePipeReadCanceled());
            Receive<WritesFlushed>(_ =>
            {
                _writesFlushed = true;

                if (_traceLogging)
                    Log.Debug("[TcpConnection] Writes flushed during close");

                if (closeEvent is ConfirmedClosed)
                {
                    // For ConfirmedClose (half-close), all writes are flushed.
                    // Now send FIN, then keep reading until peer sends their FIN (StreamEof).
                    // Do NOT cancel the CTS — reading must continue.
                    if (!_outputShutdown)
                        ShutdownOutput();

                    if (_traceLogging)
                        Log.Debug("[TcpConnection] ConfirmedClose: FIN sent, waiting for peer FIN");
                }
                else
                {
                    // For regular Close, shut down the output and cancel reads
                    if (!_outputShutdown)
                        ShutdownOutput();
                    TryCancelCts();

                    // If I/O tasks already completed, close now
                    TryFinishClose(closeSender, closeEvent);
                }
            });
            Receive<IoTasksCompleted>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] All I/O tasks completed during close");
                TryFinishClose(closeSender, closeEvent);
            });
            Receive<IoTaskFailed>(msg =>
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] I/O task failed during close: {0}", msg.Cause.Message);
                DoCloseConnection(closeSender, closeEvent);
            });
            SuspendResumeHandlers();
            Receive<HandlerDied>(_ =>
            {
                Log.Debug("Handler [{0}] died during close, stopping connection actor", _handler);
                Context.Stop(Self);
            });

            // If I/O tasks already completed before we entered ClosingBehaviour, try to close now
            TryFinishClose(closeSender, closeEvent);
        }

        /// <summary>
        /// Checks whether all conditions are met to finalize the connection close.
        /// For ConfirmedClose, the StreamEof handler manages closing directly (waiting for peer FIN).
        /// For regular Close, we close once I/O tasks are done.
        /// </summary>
        private void TryFinishClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            if (closeEvent is ConfirmedClosed)
            {
                // For ConfirmedClose, we need to wait for peer FIN (StreamEof).
                // The StreamEof handler calls DoCloseConnection directly.
                return;
            }

            // For regular Close: once outbound writes are flushed and the read stream
            // has completed or been cancelled, the actor can finish closing.
            if (_writesFlushed && _readStreamCompleted)
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
                // Resume writing is handled by the channel - no special action needed
                if (_traceLogging) Log.Debug("[TcpConnection] ResumeWriting received");
            });
        }

        /* ================================================================= */
        /*  Stream error tracking                                           */
        /* ================================================================= */

        /// <summary>
        /// Called when ReadFromStream encounters an I/O error. Records the error
        /// so that subsequent HandlePipeRead with IsCompleted can propagate it
        /// as an ErrorClosed instead of treating it as normal EOF.
        /// </summary>
        private void HandleStreamReadFailed(StreamReadFailed msg)
        {
            _readStreamError = msg.Cause;
            if (_traceLogging)
                Log.Debug("[TcpConnection] Stream read failed: {0}", msg.Cause.Message);
        }

        private void HandleReadStreamCompleted()
        {
            _readStreamCompleted = true;

            if (_traceLogging)
                Log.Debug("[TcpConnection] Read stream completed");
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
        /// No-op if a read is already in flight or the pipe isn't initialized.
        /// </summary>
        private void RequestPipeRead()
        {
            if (_readPending || _pipeReader == null || _cts == null) return;
            _readPending = true;

            if (_traceLogging) Log.Debug("[TcpConnection] RequestPipeRead: kicking off pipe read");

            var self = Self;
            var reader = _pipeReader;
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
                    Log.Debug("[TcpConnection] Delivered {0} bytes to handler", data.Length);
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
                        Log.Debug("[TcpConnection] Pipe completed with data — requesting drain read");
                    RequestPipeRead();
                    return;
                }

                // Check for stream read error. _readStreamError is set by the actor thread
                // when StreamReadFailed is processed. _readStreamHasError is a volatile flag
                // set by the background thread BEFORE the pipe writer is completed, ensuring
                // it's visible here even if the StreamReadFailed message hasn't been processed yet.
                if (_readStreamError != null || _readStreamHasError)
                {
                    // The stream read failed with an I/O error (connection reset, etc.).
                    // Propagate as an I/O error, not as normal EOF.
                    // Use _readStreamError if available, otherwise create a generic error.
                    var error = _readStreamError ?? new IOException("Connection reset by peer");
                    if (_traceLogging)
                        Log.Debug("[TcpConnection] Pipe completed with error — signaling I/O error: {0}",
                            error.Message);
                    HandleIoError(error);
                    return;
                }

                // Normal EOF — peer closed their write side cleanly.
                if (_traceLogging)
                    Log.Debug("[TcpConnection] Pipe completed — signaling EOF");
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
                Log.Debug("[TcpConnection] Pipe read cancelled");
        }

        private static async ValueTask<PipeReadCompleted> ReadPipeChunkAsync(PipeReader reader, CancellationToken ct)
        {
            while (true)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;
                byte[] data;

                if (buffer.Length > 0)
                {
                    data = new byte[checked((int)buffer.Length)];
                    buffer.CopyTo(data);
                }
                else
                {
                    data = Array.Empty<byte>();
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

            if (_maxQueuedBytes >= 0 && _queuedBytes + _pendingRegistrationBytes + byteCount > _maxQueuedBytes)
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

            _pendingRegistrationWrites.Enqueue(new WriteCommand(write, sender));
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

            // Check queue size limit
            if (_maxQueuedBytes >= 0 && _queuedBytes + byteCount > _maxQueuedBytes)
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

            if (_writeChannel != null && _writeChannel.Writer.TryWrite(new WriteCommand(write, sender)))
            {
                _queuedBytes += byteCount;
            }
            else
            {
                sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
            }
        }

        /* ================================================================= */
        /*  Background I/O tasks                                             */
        /* ================================================================= */

        /// <summary>
        /// Background task: reads from the network stream into the Pipe writer.
        /// </summary>
        private async Task ReadFromStreamAsync(Stream stream, PipeWriter writer, CancellationToken ct)
        {
            var self = Self;
            var minimumBufferSize = Settings.MaxFrameSizeBytes;
            Exception? streamError = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var memory = writer.GetMemory(minimumBufferSize);
                    var bytesRead = await stream.ReadAsync(memory, ct).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        // EOF - peer closed their send side.
                        // Don't self-tell StreamEof here — let the PipeReader detect
                        // completion via IsCompleted so all buffered data is delivered first.
                        if (_traceLogging)
                            Log.Debug("[TcpConnection] ReadFromStream: EOF received (0 bytes read)");
                        break;
                    }

                    writer.Advance(bytesRead);

                    if (_traceLogging)
                    {
                        Interlocked.Add(ref _totalReceivedBytes, bytesRead);
                        Log.Debug("[TcpConnection] ReadFromStream: read {0} bytes [{1} total]",
                            bytesRead, Interlocked.Read(ref _totalReceivedBytes));
                    }

                    var flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flushResult.IsCompleted || flushResult.IsCanceled)
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal cancellation - expected during shutdown
                if (_traceLogging)
                    Log.Debug("[TcpConnection] ReadFromStream: cancelled");
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                // I/O error - notify the actor before pipe completes so error state is set.
                // Set _readStreamHasError BEFORE Self.Tell and BEFORE CompleteAsync (in finally)
                // so that HandlePipeRead can detect the error even if PipeReadCompleted
                // is processed before StreamReadFailed.
                if (_traceLogging)
                    Log.Debug("[TcpConnection] ReadFromStream: I/O error {0}: {1}", ex.GetType().Name, ex.Message);
                streamError = ex;
                _readStreamHasError = true;
                self.Tell(new StreamReadFailed(ex));
            }
            catch (Exception ex)
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] ReadFromStream: unexpected error {0}: {1}", ex.GetType().Name, ex.Message);
                streamError = ex;
                _readStreamHasError = true;
                self.Tell(new StreamReadFailed(ex));
            }
            finally
            {
                // Complete the pipe writer without passing the exception.
                // PipeWriter.CompleteAsync(exception) causes PipeReader.ReadAsync() to throw
                // synchronously, bypassing the actor's message loop. The error is already
                // communicated via StreamReadFailed self-tell and _readStreamError field.
                await writer.CompleteAsync().ConfigureAwait(false);
                self.Tell(ReadStreamCompleted.Instance);
            }
        }

        /// <summary>
        /// Background task: dequeues write commands and writes them to the stream.
        /// </summary>
        private async Task WriteToStreamAsync(Stream stream, ChannelReader<WriteCommand> reader, CancellationToken ct)
        {
            var self = Self;
            var batch = new List<WriteCommand>(8);
            var hasPending = false;
            WriteCommand pending = default;

            try
            {
                while (true)
                {
                    batch.Clear();
                    var batchBytes = 0;

                    if (hasPending)
                    {
                        batch.Add(pending);
                        batchBytes = (int)pending.Cmd.Bytes;
                        hasPending = false;
                    }
                    else
                    {
                        if (!await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                            break;

                        if (!reader.TryRead(out pending))
                            continue;

                        batch.Add(pending);
                        batchBytes = (int)pending.Cmd.Bytes;
                    }

                    while (batch.Count < MaxWriteCommandsPerBatch
                           && batchBytes < _writeBatchSizeBytes
                           && reader.TryRead(out var next))
                    {
                        var nextBytes = (int)next.Cmd.Bytes;
                        if (batchBytes > 0 && batchBytes + nextBytes > _writeBatchSizeBytes)
                        {
                            pending = next;
                            hasPending = true;
                            break;
                        }

                        batch.Add(next);
                        batchBytes += nextBytes;
                    }

                    try
                    {
                        await WriteBatchToStreamAsync(stream, batch, batchBytes, ct).ConfigureAwait(false);

                        Interlocked.Add(ref _queuedBytes, -batchBytes);

                        if (_traceLogging)
                        {
                            Interlocked.Add(ref _totalSentBytes, batchBytes);
                            Log.Debug("[TcpConnection] WriteToStream: wrote {0} bytes in {1} command(s) [{2} total sent]",
                                batchBytes, batch.Count, Interlocked.Read(ref _totalSentBytes));
                        }

                        AckBatch(batch);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        FailBatch(batch, DroppingWriteBecauseClosingException);
                        FailPendingWrites(reader, hasPending ? pending : null, DroppingWriteBecauseClosingException);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or SocketException)
                    {
                        FailBatch(batch, ex);
                        FailPendingWrites(reader, hasPending ? pending : null, ex);
                        self.Tell(new IoTaskFailed(ex));
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal cancellation
                return;
            }
            catch (ChannelClosedException)
            {
                // Channel was completed - normal shutdown
                return;
            }
            catch (Exception ex)
            {
                self.Tell(new IoTaskFailed(ex));
            }
            finally
            {
                // Signal that writes are flushed
                self.Tell(WritesFlushed.Instance);
            }
        }

        private async Task WriteBatchToStreamAsync(Stream stream, IReadOnlyList<WriteCommand> batch, int batchBytes,
            CancellationToken ct)
        {
            if (batch.Count == 1)
            {
                var data = batch[0].Cmd.Data;
                if (data.Length > 0)
                {
                    if (data.IsSingleSegment)
                    {
                        await stream.WriteAsync(data.First, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        foreach (var segment in data)
                        {
                            await stream.WriteAsync(segment, ct).ConfigureAwait(false);
                        }
                    }
                }

                return;
            }

            var rented = ArrayPool<byte>.Shared.Rent(batchBytes);
            try
            {
                var offset = 0;
                foreach (var item in batch)
                {
                    foreach (var segment in item.Cmd.Data)
                    {
                        segment.Span.CopyTo(rented.AsSpan(offset, segment.Length));
                        offset += segment.Length;
                    }
                }

                await stream.WriteAsync(rented.AsMemory(0, batchBytes), ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static void AckBatch(IReadOnlyList<WriteCommand> batch)
        {
            foreach (var item in batch)
            {
                if (item.Cmd.WantsAck)
                    item.Sender.Tell(item.Cmd.Ack);
            }
        }

        private static void FailBatch(IReadOnlyList<WriteCommand> batch, Exception cause)
        {
            foreach (var item in batch)
            {
                item.Sender.Tell(item.Cmd.FailureMessage.WithCause(cause));
            }
        }

        private static void FailPendingWrites(ChannelReader<WriteCommand> reader, WriteCommand? pending, Exception cause)
        {
            if (pending.HasValue)
                pending.Value.Sender.Tell(pending.Value.Cmd.FailureMessage.WithCause(cause));

            while (reader.TryRead(out var write))
            {
                write.Sender.Tell(write.Cmd.FailureMessage.WithCause(cause));
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
        /// Tcp.Close: flush pending writes, then close everything.
        /// </summary>
        private void HandleGracefulClose(IActorRef closeSender, ConnectionClosed closeEvent)
        {
            _closingGracefully = true;

            // Complete the write channel - no more writes accepted
            _writeChannel?.Writer.TryComplete();

            // Transition to closing behaviour - will wait for writes to flush,
            // then cancel reads, then close
            Become(() => ClosingBehaviour(closeSender, closeEvent));
        }

        /// <summary>
        /// Tcp.Abort: cancel everything immediately.
        /// </summary>
        private void HandleAbort(IActorRef closeSender)
        {
            _closingGracefully = true;

            // Cancel CTS immediately - no flush
            TryCancelCts();

            // Complete the write channel
            _writeChannel?.Writer.TryComplete();

            // Abort the socket (send RST)
            AbortSocket();

            StopWith(new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), Aborted.Instance));
        }

        /// <summary>
        /// Tcp.ConfirmedClose: half-close (send FIN), wait for peer FIN.
        /// The sequence is: flush writes -> shutdown output (FIN) -> wait for peer FIN (StreamEof).
        /// </summary>
        private void HandleConfirmedClose(IActorRef closeSender)
        {
            _closingGracefully = true;

            // Complete the write channel (no more writes accepted)
            _writeChannel?.Writer.TryComplete();

            // Always enter ClosingBehaviour to wait for WritesFlushed,
            // even if peer already closed. This ensures proper sequencing:
            // flush writes -> shutdown output (FIN) -> wait for peer FIN (StreamEof).
            Become(() => ClosingBehaviour(closeSender, ConfirmedClosed.Instance));
        }

        /// <summary>
        /// Handle EOF from the stream read task.
        /// </summary>
        private void HandleStreamEof()
        {
            if (_peerClosed)
            {
                // Duplicate EOF — already handled, ignore
                if (_traceLogging)
                    Log.Debug("[TcpConnection] HandleStreamEof: duplicate EOF, ignoring");
                return;
            }

            _peerClosed = true;

            if (_traceLogging)
                Log.Debug("[TcpConnection] HandleStreamEof: peer closed");

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
            _writeChannel?.Writer.TryComplete();

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

        private bool ShutdownOutput()
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Send);
                _outputShutdown = true;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private void DoCloseConnection(IActorRef closeSender, ConnectionClosed closedEvent)
        {
            TryCancelCts();

            switch (closedEvent)
            {
                case Aborted:
                    AbortSocket();
                    break;
                default:
                    try
                    {
                        Socket.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException e)
                    {
                        Log.Error(e, "Graceful socket shutdown failed");
                    }
                    CloseSocket();
                    break;
            }

            StopWith(new CloseInformation(ImmutableHashSet<IActorRef>.Empty.Add(closeSender), closedEvent));
        }

        private void CloseSocket()
        {
            Socket.Dispose();
            _outputShutdown = true;
        }

        private void AbortSocket()
        {
            try
            {
                Socket.LingerState = new LingerOption(true, 0);
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
