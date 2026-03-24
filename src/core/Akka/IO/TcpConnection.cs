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
    /// - WriteToStream: dequeues write commands, calls stream.WriteAsync, delivers ACKs
    ///
    /// All shutdown and error handling flows through the actor mailbox for thread safety.
    /// </summary>
    internal abstract class TcpConnection : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        #region Internal messages

        /// <summary>
        /// Self-tell: all background I/O tasks have completed.
        /// </summary>
        private sealed class IoTasksCompleted
        {
            public static readonly IoTasksCompleted Instance = new();
            private IoTasksCompleted() { }
        }

        /// <summary>
        /// Self-tell: a background task failed with an exception.
        /// </summary>
        private sealed class IoTaskFailed
        {
            public Exception Cause { get; }
            public IoTaskFailed(Exception cause) { Cause = cause; }
        }

        /// <summary>
        /// Self-tell: the read-from-stream task observed EOF (0 bytes).
        /// </summary>
        private sealed class StreamEof
        {
            public static readonly StreamEof Instance = new();
            private StreamEof() { }
        }

        /// <summary>
        /// Self-tell: PipeReader.ReadAsync completed with data.
        /// </summary>
        private sealed class PipeReadCompleted
        {
            public ReadResult Result { get; }
            public PipeReadCompleted(ReadResult result) { Result = result; }
        }

        /// <summary>
        /// Self-tell: all pending writes have been flushed (write channel completed + drained).
        /// </summary>
        private sealed class WritesFlushed
        {
            public static readonly WritesFlushed Instance = new();
            private WritesFlushed() { }
        }

        private sealed class CommanderDied : IDeadLetterSuppression
        {
            public static readonly CommanderDied Instance = new();
            private CommanderDied() { }
        }

        private sealed class HandlerDied : IDeadLetterSuppression
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

        // State flags
        private bool _peerClosed;
        private bool _outputShutdown;
        private bool _keepOpenOnPeerClosed;
        private bool _closingGracefully;

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

            try { _stream?.Dispose(); } catch { /* ignore */ }
            try { _pipe?.Writer.Complete(); } catch { /* ignore */ }
            try { _pipe?.Reader.Complete(); } catch { /* ignore */ }
            try { _writeChannel?.Writer.TryComplete(); } catch { /* ignore */ }

            if (Socket.Connected) AbortSocket();
            else CloseSocket();

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
            Task.WhenAll(_readFromStreamTask, _writeToStreamTask)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        var innerEx = t.Exception.InnerExceptions.FirstOrDefault() ?? t.Exception;
                        self.Tell(new IoTaskFailed(innerEx));
                    }
                    else
                    {
                        self.Tell(IoTasksCompleted.Instance);
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
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

                Become(OpenBehaviour);
            });
            Receive<Tcp.WriteCommand>(w =>
            {
                Log.Warning("Received Write command before Register command. " +
                            "It will be buffered until Register will be received (buffered write size is {0} bytes)", w.Bytes);
                // We can't enqueue writes yet - no write channel
                Sender.Tell(w.FailureMessage.WithCause(new InvalidOperationException("Connection not yet registered")));
            });
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
            Receive<PipeReadCompleted>(HandlePipeRead);
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
            Receive<PipeReadCompleted>(HandlePipeRead);
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
            Receive<StreamEof>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] EOF received during closing - connection fully closed");
                DoCloseConnection(closeSender, closeEvent is ConfirmedClosed ? closeEvent : ConfirmedClosed.Instance);
            });
            Receive<PipeReadCompleted>(HandlePipeRead);
            Receive<WritesFlushed>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] Writes flushed during close");
                // Now cancel the read side
                TryCancelCts();
            });
            Receive<IoTasksCompleted>(_ =>
            {
                if (_traceLogging)
                    Log.Debug("[TcpConnection] All I/O tasks completed during close");
                DoCloseConnection(closeSender, closeEvent);
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

            var valueTask = reader.ReadAsync(ct);
            if (valueTask.IsCompletedSuccessfully)
            {
                // Fast path: data already available in the pipe
                self.Tell(new PipeReadCompleted(valueTask.Result));
            }
            else
            {
                valueTask.AsTask().ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                        self.Tell(new PipeReadCompleted(task.Result));
                    else if (task.IsFaulted)
                        self.Tell(new IoTaskFailed(task.Exception!.InnerException ?? task.Exception));
                    // Cancelled → actor is stopping, no message needed
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        /// <summary>
        /// Actor handles a completed pipe read: copy data, deliver to handler,
        /// advance the reader, and optionally request the next read.
        /// </summary>
        private void HandlePipeRead(PipeReadCompleted msg)
        {
            _readPending = false;
            var result = msg.Result;
            var buffer = result.Buffer;

            if (buffer.Length > 0)
            {
                var data = new byte[buffer.Length];
                buffer.CopyTo(data);

                _handler!.Tell(new Received(data));

                if (_traceLogging)
                    Log.Debug("[TcpConnection] Delivered {0} bytes to handler", data.Length);
            }

            _pipeReader!.AdvanceTo(buffer.End);

            if (result.IsCompleted)
            {
                // Pipe writer completed (stream EOF or error).
                // All buffered data has been delivered above — now signal EOF.
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
            var minimumBufferSize = Settings.MaxFrameSizeBytes;
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
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                // I/O error - notify the actor
                Self.Tell(new IoTaskFailed(ex));
            }
            catch (Exception ex)
            {
                Self.Tell(new IoTaskFailed(ex));
            }
            finally
            {
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Background task: dequeues write commands and writes them to the stream.
        /// </summary>
        private async Task WriteToStreamAsync(Stream stream, ChannelReader<WriteCommand> reader, CancellationToken ct)
        {
            var self = Self;
            try
            {
                await foreach (var cmd in reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    var write = cmd.Cmd;
                    var sender = cmd.Sender;

                    try
                    {
                        var data = write.Data;
                        if (data.Length > 0)
                        {
                            await stream.WriteAsync(data, ct).ConfigureAwait(false);
                        }

                        // Decrement queued bytes
                        Interlocked.Add(ref _queuedBytes, -(int)write.Bytes);

                        if (_traceLogging)
                        {
                            Interlocked.Add(ref _totalSentBytes, data.Length);
                            Log.Debug("[TcpConnection] WriteToStream: wrote {0} bytes [{1} total sent]",
                                data.Length, Interlocked.Read(ref _totalSentBytes));
                        }

                        // ACK: self-tell before caller-tell for ordering
                        if (write.WantsAck)
                        {
                            sender.Tell(write.Ack);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Cancelled during write - send failure for this write
                        sender.Tell(write.FailureMessage.WithCause(DroppingWriteBecauseClosingException));
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or SocketException)
                    {
                        // Write failed - notify sender and actor
                        sender.Tell(write.FailureMessage.WithCause(ex));
                        self.Tell(new IoTaskFailed(ex));
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal cancellation
            }
            catch (ChannelClosedException)
            {
                // Channel was completed - normal shutdown
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
        /// </summary>
        private void HandleConfirmedClose(IActorRef closeSender)
        {
            _closingGracefully = true;

            // Complete the write channel
            _writeChannel?.Writer.TryComplete();

            // Send FIN
            if (_peerClosed || !ShutdownOutput())
            {
                // Peer already closed or shutdown failed - close immediately
                DoCloseConnection(closeSender, ConfirmedClosed.Instance);
            }
            else
            {
                // Wait for peer FIN (StreamEof) - the ClosingBehaviour will handle it
                Become(() => ClosingBehaviour(closeSender, ConfirmedClosed.Instance));
            }
        }

        /// <summary>
        /// Handle EOF from the stream read task.
        /// </summary>
        private void HandleStreamEof()
        {
            if (_traceLogging)
                Log.Debug("[TcpConnection] HandleStreamEof: peer closed");

            if (_outputShutdown)
            {
                // Both sides closed - connection is fully closed
                DoCloseConnection(_handler ?? _commander!, ConfirmedClosed.Instance);
            }
            else
            {
                _peerClosed = true;
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
                try
                {
                    _cts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
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
            try { Socket.Close(); } catch { /* ignore */ }
            try { Socket.Dispose(); } catch { /* ignore */ }
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
