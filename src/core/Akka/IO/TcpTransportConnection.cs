//-----------------------------------------------------------------------
// <copyright file="TcpTransportConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.IO
{
    /// <summary>
    /// Plaintext TCP implementation of <see cref="ITransportConnection"/>.
    /// Writes directly to a <see cref="NetworkStream"/> using a double-buffer
    /// ping-pong (no output Pipe, no background write pump), and exposes a
    /// <see cref="PipeReader"/> for the inbound side that the consuming actor drives directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Write side — double-buffer ping-pong.</b> The owning <see cref="TcpConnection"/>
    /// actor is the single writer: it pushes bytes in via <see cref="WriteUnflushed(ReadOnlyMemory{byte})"/>
    /// and commits them with <see cref="FlushAsync"/>. There are two
    /// <see cref="ArrayBufferWriter{T}"/> slots. New bytes accumulate into the <em>active</em>
    /// slot; a flush hands the active slot to <see cref="Stream.WriteAsync(ReadOnlyMemory{byte},CancellationToken)"/>
    /// and swaps to the other (already-drained) slot so the actor can keep batching the next
    /// burst while the kernel transmits the previous one. The in-flight write is awaited only
    /// just before a new one would start — that overlap is what replaces the old pump→Pipe→stream
    /// cross-thread hand-off.
    /// </para>
    /// <para>
    /// <b>Backpressure.</b> Memory is bounded to (at most) two buffered batches. If a flush
    /// arrives while a write is already in flight, <see cref="FlushAsync"/> returns a not-yet-completed
    /// <see cref="ValueTask{FlushResult}"/> that completes only once the in-flight write drains and the
    /// newly-buffered batch has itself been handed to the stream — so a fast producer cannot pile up
    /// unbounded bytes in user space.
    /// </para>
    /// </remarks>
    public sealed class TcpTransportConnection : ITransportConnection
    {
        // Segment size for the inbound StreamPipeReader. Large enough that one ReadAsync
        // typically pulls a full kernel receive buffer in a single call.
        private const int ReadBufferSize = 64 * 1024;

        // Initial capacity of each outbound double-buffer slot. Grows on demand if a batch
        // exceeds it; sized so the common small-message batch never reallocates.
        private const int WriteBufferInitialCapacity = 8 * 1024;

        // Bounds for the drain-on-close. Closing a socket that still has unread inbound
        // data in the kernel receive buffer makes the OS emit RST instead of FIN, so the
        // peer would observe a "connection reset" error instead of a clean close. Before we
        // close we therefore drain pending inbound bytes (until the peer's FIN, or until we
        // hit these caps) so the OS can complete a graceful FIN/ACK exchange. The caps keep
        // a pathological peer that keeps shoving data at us from blocking close forever.
        private const int DrainMaxBytes = 1024 * 1024;
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromMilliseconds(250);

        private readonly Socket _socket;
        private readonly Stream _stream;
        private readonly ReadGate _inputReader;
        private readonly CancellationTokenSource _cts = new();

        // ── Outbound double-buffer ping-pong ───────────────────────────────────
        // Two slots: bytes accumulate into _buffers[_activeIdx]; on flush that slot is
        // handed to _stream.WriteAsync and we swap to the other slot, so the actor can keep
        // batching the next burst while the kernel transmits this one.
        //
        // CONCURRENCY: the owning TcpConnection actor is the single *producer* (it calls
        // WriteUnflushed/FlushAsync from its own mailbox thread, fire-and-forget). But the
        // completion of an in-flight _stream.WriteAsync runs on an arbitrary thread-pool /
        // I/O thread, and that completion has to start the NEXT batch (otherwise a batch that
        // landed in the standby slot while a write was in flight would never be sent until the
        // actor happened to flush again — under load that wakeup is exactly what gets lost and
        // the whole connection hangs). So both the actor thread and the write-completion
        // continuation mutate the buffers / _activeIdx / _inflightWrite. ALL of those
        // mutations are serialized under _writeGate; the buffer slots are never touched outside
        // the lock. The write itself runs outside the lock — only the bookkeeping is guarded.
        private readonly object _writeGate = new();

        private readonly ArrayBufferWriter<byte>[] _buffers =
        {
            new(WriteBufferInitialCapacity),
            new(WriteBufferInitialCapacity),
        };

        private int _activeIdx;

        // The in-flight write of the OTHER (non-active) slot, or null if no write is in flight.
        // Guarded by _writeGate. While non-null the slot it is transmitting (1 ^ _activeIdx)
        // must not be touched; the active slot keeps accumulating the next batch.
        private Task? _inflightWrite;

        // Latches the first write error so subsequent flushes surface it instead of writing
        // to a dead stream. WriteCompleted is completed (faulted) when this is set.
        private readonly TaskCompletionSource<int> _writeCompletedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Guarded by _writeGate.
        private bool _writeClosed;
        private Exception? _writeError;

        /// <summary>
        /// Creates a transport connection from an already-connected socket.
        /// Inbound reads are driven on demand by the consumer via <see cref="Input"/>;
        /// outbound writes go straight to the stream via the double-buffer.
        /// </summary>
        public TcpTransportConnection(Socket socket, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: false);

            // Inbound: no background read pump. The consumer (TcpConnection actor) drives
            // PipeReader.ReadAsync directly, so a socket read runs on the consumer's own
            // continuation instead of being flushed across a Pipe to a second thread. The
            // inputPipeOptions / outputPipeOptions parameters are retained for source/binary
            // compatibility but are unused now that neither side owns a Pipe.
            //
            // The reader is wrapped in a ReadGate so the close/abort/dispose paths can quiesce
            // any in-flight consumer ReadAsync BEFORE completing the underlying StreamPipeReader.
            // Completing a StreamPipeReader concurrently with an in-flight read resets the
            // segment whose End the resuming read is about to write, throwing
            // ArgumentOutOfRangeException out of BufferSegment.set_End (see ReadGate).
            _inputReader = new ReadGate(
                PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: ReadBufferSize, leaveOpen: true)));

            ReadCompleted = Task.CompletedTask;
            WriteCompleted = _writeCompletedTcs.Task;
        }

        /// <summary>
        /// Creates a transport connection from an existing stream (for TLS or testing).
        /// </summary>
        public TcpTransportConnection(Socket socket, Stream stream, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
        {
            _socket = socket;
            _stream = stream;

            // See the socket-only constructor: the inbound side is a consumer-driven
            // PipeReader (wrapped in a ReadGate for safe close) and the outbound side is a
            // direct double-buffer, so there is no Pipe.
            _inputReader = new ReadGate(
                PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: ReadBufferSize, leaveOpen: true)));

            ReadCompleted = Task.CompletedTask;
            WriteCompleted = _writeCompletedTcs.Task;
        }

        public PipeReader Input => _inputReader;

        /// <inheritdoc/>
        public Task ReadCompleted { get; }

        /// <inheritdoc/>
        public Task WriteCompleted { get; }

        /// <inheritdoc/>
        // No background read pump: read errors surface synchronously from the consumer's
        // PipeReader.ReadAsync (the TcpConnection actor catches them and reports IoTaskFailed),
        // so there is no out-of-band error to latch here.
        public bool HasReadError => false;

        /// <inheritdoc/>
        public Exception? ReadError => null;

        internal void WriteUnflushed(ReadOnlyMemory<byte> data)
        {
            // Guard the buffer + _activeIdx: a write-completion continuation may concurrently
            // swap _activeIdx and clear a slot, so appends must be serialized with it.
            lock (_writeGate)
            {
                if (_writeClosed)
                    return; // writer side already shut down — drop (the actor sees the failure via WriteCompleted)

                _buffers[_activeIdx].Write(data.Span);
            }
        }

        internal void WriteUnflushed(ReadOnlySequence<byte> data)
        {
            lock (_writeGate)
            {
                if (_writeClosed)
                    return;

                var active = _buffers[_activeIdx];
                if (data.IsSingleSegment)
                {
                    active.Write(data.FirstSpan);
                    return;
                }

                foreach (var segment in data)
                    active.Write(segment.Span);
            }
        }

        public ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            WriteUnflushed(data);
            return FlushAsync(ct);
        }

        public ValueTask<FlushResult> WriteAsync(ReadOnlySequence<byte> data, CancellationToken ct = default)
        {
            WriteUnflushed(data);
            return FlushAsync(ct);
        }

        /// <summary>
        /// Commits whatever has accumulated in the active buffer to the stream.
        /// If no write is in flight, it starts one for the active slot and swaps to the drained
        /// slot, so the actor can keep batching the next burst while the kernel transmits this
        /// one. If a write is already in flight, the freshly-buffered bytes simply stay in the
        /// (standby) active slot: the in-flight write's completion continuation picks them up and
        /// starts the next write itself (see <see cref="OnWriteCompleted"/>), so the batch is
        /// always drained without a second flush from the actor. Memory stays bounded to two
        /// batches and the call always returns synchronously — the caller (the actor) fires
        /// flushes and discards the result; liveness is owned by the internal write chain.
        /// </summary>
        public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return new ValueTask<FlushResult>(new FlushResult(isCanceled: true, isCompleted: false));

            // All double-buffer bookkeeping is serialized here against the write-completion
            // continuation (OnWriteCompleted), which runs on an arbitrary thread.
            lock (_writeGate)
            {
                // If the writer side already failed/closed, report it (IsCompleted = true) so the
                // caller stops pushing. We never throw out of the hot flush path.
                if (_writeClosed)
                    return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: true));

                // A write is already in flight: leave the bytes in the standby slot. When the
                // in-flight write completes, OnWriteCompleted will start them. Nothing to do now.
                if (_inflightWrite != null)
                    return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: false));

                // Nothing in flight — start the active batch (if any) right now.
                if (_buffers[_activeIdx].WrittenCount > 0)
                    StartActiveWriteLocked(ct);

                return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: false));
            }
        }

        /// <summary>
        /// Starts the stream write for the active buffer (which must be non-empty), stores it as
        /// the in-flight write, swaps the active index to the now-idle slot, and attaches a
        /// completion continuation that chains the next batch. MUST be called while holding
        /// <see cref="_writeGate"/>, and only when no write is already in flight.
        /// </summary>
        private void StartActiveWriteLocked(CancellationToken ct)
        {
            var slot = _activeIdx;
            var active = _buffers[slot];

            // Hold the write as a Task so the completion continuation can chain off it.
            // .AsTask() boxes the ValueTask once. NetworkStream/SslStream flush per WriteAsync,
            // so no explicit Flush is needed.
            var writeTask = _stream.WriteAsync(active.WrittenMemory, ct).AsTask();
            _inflightWrite = writeTask;

            // Swap: the next batch fills the OTHER slot. The slot we just handed off (now the
            // standby) stays untouched until OnWriteCompleted observes its write finishing.
            _activeIdx ^= 1;

            // Clear the slot we are about to start filling. Its previous write already completed
            // (we never reuse a slot whose write is still in flight), so this is safe.
            _buffers[_activeIdx].Clear();

            // When this write finishes, drive the chain forward (start the standby batch, surface
            // faults). Runs on the completing thread, but everything it touches is under the lock.
            writeTask.ContinueWith(
                static (t, state) => ((TcpTransportConnection)state!).OnWriteCompleted(t),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Continuation invoked when an in-flight write completes. Clears the just-finished
        /// write, surfaces any fault, and — if the actor accumulated another batch in the
        /// standby slot while this write was on the wire — starts that batch, keeping the write
        /// chain alive without requiring a fresh flush from the actor. This is what guarantees a
        /// buffered batch is never stranded (the missed-wakeup hang the double-buffer regressed).
        /// </summary>
        private void OnWriteCompleted(Task completed)
        {
            lock (_writeGate)
            {
                // Stale continuation (e.g. after close/abort replaced or cleared the field): ignore.
                if (!ReferenceEquals(_inflightWrite, completed))
                {
                    _ = completed.Exception; // observe to avoid UnobservedTaskException
                    return;
                }

                _inflightWrite = null;

                if (completed.IsFaulted)
                {
                    FailWriteLocked(completed.Exception?.GetBaseException() ?? new IOException("TCP write failed"));
                    return;
                }

                // Once draining/closed, DrainPendingWritesAsync owns the remaining writes — do
                // not self-chain (it awaits _inflightWrite, which we have just nulled).
                if (completed.IsCanceled || _writeClosed || _draining)
                    return;

                // The actor may have buffered another batch in the (now) active slot while this
                // write was transmitting. Start it so the chain keeps draining.
                if (_buffers[_activeIdx].WrittenCount > 0)
                    StartActiveWriteLocked(_cts.Token);
            }
        }

        /// <summary>
        /// Awaits the final in-flight write plus any bytes still sitting in the buffers, so that
        /// every byte the actor handed us reaches the socket before we send FIN / close.
        /// Replaces the old "await WriteCompleted (pump drains)" step now that there is no pump.
        /// </summary>
        /// <remarks>
        /// The actor has already flushed all pending writes before calling close, so no new bytes
        /// arrive once we are here. We close the producer side (so the self-chaining continuation
        /// stops starting writes and hands control to us), then loop: grab the in-flight write
        /// under the lock, await it outside the lock, and write any buffered tail — until both
        /// slots are empty and nothing is in flight.
        /// </remarks>
        private async Task DrainPendingWritesAsync()
        {
            try
            {
                // Take ownership of the write chain: stop OnWriteCompleted from starting new
                // writes so we can drain deterministically. _writeClosed is set only at the very
                // end (CompleteWrite); this DrainingClosed handshake is the interim guard.
                Task? inflight;
                lock (_writeGate)
                {
                    _draining = true;
                    inflight = _inflightWrite;
                }

                while (true)
                {
                    // Finish whatever is on the wire (outside the lock).
                    if (inflight != null)
                        await inflight.ConfigureAwait(false);

                    byte[]? tail = null;
                    int slot;
                    lock (_writeGate)
                    {
                        _inflightWrite = null;

                        slot = _activeIdx;
                        var active = _buffers[slot];
                        if (active.WrittenCount == 0)
                            break; // nothing left in flight or buffered — done

                        // Copy the tail out so we can write it outside the lock without it being
                        // mutated, then clear the slot.
                        tail = active.WrittenMemory.ToArray();
                        active.Clear();
                    }

                    await _stream.WriteAsync(tail.AsMemory(), _cts.Token).ConfigureAwait(false);
                    inflight = null; // loop to re-check for any further buffered bytes
                }

                CompleteWrite(null);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // slopwatch-ignore: SW003 shutdown raced an abort — treat as a clean write completion
                CompleteWrite(null);
            }
            catch (Exception ex)
            {
                CompleteWrite(ex);
            }
        }

        /// <summary>
        /// Attaches a benign continuation to the in-flight write (if any) so that, when an
        /// <see cref="Abort"/> RSTs the socket and faults the write, it is observed rather than
        /// surfacing later as an unobserved <see cref="TaskScheduler.UnobservedTaskException"/>.
        /// </summary>
        private void ObserveInflightWrite()
        {
            Task? inflight;
            lock (_writeGate)
            {
                inflight = _inflightWrite;
            }

            if (inflight is null || inflight.IsCompleted)
            {
                // Already done — touch its exception (if faulted) so it is marked observed.
                _ = inflight?.Exception;
                return;
            }

            inflight.ContinueWith(
                static t => { _ = t.Exception; }, // slopwatch-ignore: SW003 fault is expected when the abort RSTs the socket
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // Set under _writeGate at the start of a drain-on-close so OnWriteCompleted stops
        // self-chaining and hands the remaining writes to DrainPendingWritesAsync.
        private bool _draining;

        private void FailWriteLocked(Exception ex)
        {
            // Caller already holds _writeGate.
            _writeError ??= ex;
            CompleteWriteLocked(ex);
        }

        private void CompleteWrite(Exception? error)
        {
            lock (_writeGate)
            {
                CompleteWriteLocked(error);
            }
        }

        private void CompleteWriteLocked(Exception? error)
        {
            // Caller holds _writeGate.
            if (_writeClosed)
                return;

            _writeClosed = true;
            _inflightWrite = null;

            if (error != null)
                _writeCompletedTcs.TrySetException(error);
            else
                _writeCompletedTcs.TrySetResult(0);
        }

        public async Task ShutdownAsync()
        {
            // Tcp.ConfirmedClose: this is a HALF close. Flush our pending writes, then send
            // FIN (Shutdown(Send)) but leave the read side open. The consuming actor keeps
            // driving the inbound PipeReader until it observes the peer's FIN (EOF), at which
            // point it finalizes the close. We must NOT drain inbound here — that data still
            // belongs to the consumer.

            // Flush all buffered + in-flight writes so every byte reaches the socket before FIN.
            await DrainPendingWritesAsync().ConfigureAwait(false);

            // Half-close the socket (send FIN).
            // SocketException is expected if the peer already reset the connection.
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException) { } // slopwatch-ignore: SW003 socket may already be closed by peer or abort
            catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket may already be disposed by an abort
        }

        public async Task CloseAsync()
        {
            // Tcp.Close: full, graceful close. Flush pending writes, send our FIN, drain any
            // still-buffered inbound bytes so the OS sends FIN (not RST) on the final close,
            // then tear everything down.

            // Flush all buffered + in-flight writes so every byte reaches the socket before FIN.
            await DrainPendingWritesAsync().ConfigureAwait(false);

            // Send our FIN now (half-close the send side). Doing this before the drain lets
            // the peer observe our orderly shutdown and reciprocate while we read out whatever
            // it already sent us. SocketException/ObjectDisposedException can occur if the peer
            // already reset us or an abort raced in — both are fine, we're closing anyway.
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException) { } // slopwatch-ignore: SW003 socket may already be closed by peer or abort
            catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket may already be disposed by an abort

            // Cancel any straggling write/flush; the inbound reader is consumer-driven, so
            // completing it releases pooled buffers and frees the underlying stream for the
            // bounded socket-level drain below.
            _cts.Cancel();

            // Quiesce the consumer's in-flight ReadAsync (cancel it and wait for it to settle)
            // BEFORE completing the underlying StreamPipeReader. Completing it while a read is
            // mid-flight resets the segment the resuming read writes into, throwing
            // ArgumentOutOfRangeException from BufferSegment.set_End — surfaced to the peer as a
            // spurious ErrorClosed. Serializing the two closes that race window.
            await _inputReader.QuiesceAndCompleteAsync().ConfigureAwait(false);

            // Wait for read completion — no background pump, so this is already complete.
            try { await ReadCompleted.ConfigureAwait(false); }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected cancellation or I/O error during shutdown

            // Drain any inbound bytes the consumer never read (and wait for the peer's FIN,
            // bounded). Without this, closing a socket with unread receive-buffer data makes
            // the OS emit RST instead of FIN, which the peer would surface as ErrorClosed
            // ("connection reset by peer") instead of a clean PeerClosed/Closed.
            DrainInboundForClose();

            // Close the stream and socket
            await _stream.DisposeAsync().ConfigureAwait(false);
            _socket.Close();
        }

        /// <summary>
        /// Reads and discards inbound bytes until the peer's FIN (recv returns 0) or until a
        /// byte/time cap is hit. Used only on the full-close path so the final socket close
        /// produces a FIN rather than an RST. Operates directly on the socket because the
        /// consumer-driven <see cref="PipeReader"/> has already been completed by this point.
        /// </summary>
        private void DrainInboundForClose()
        {
            // If the socket is already gone (e.g. peer reset, or a racing abort) there is
            // nothing to drain — closing it will just be a no-op / already-RST'd connection.
            byte[]? buffer = null;
            try
            {
                var deadline = DateTime.UtcNow + DrainTimeout;
                var drained = 0;

                while (drained < DrainMaxBytes && DateTime.UtcNow < deadline)
                {
                    // Only block waiting for data if there might be more coming. Poll with a
                    // short, bounded timeout so a quiet-but-not-yet-FIN peer can't hang close.
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    // Poll returns true when the socket is readable; a readable socket that
                    // yields 0 bytes from Receive means the peer has sent its FIN (EOF).
                    var pollMicros = (int)Math.Min(remaining.TotalMilliseconds * 1000, int.MaxValue);
                    if (!_socket.Poll(pollMicros, SelectMode.SelectRead))
                        break; // no data and no FIN within the budget — stop draining

                    buffer ??= ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                    int read;
                    try
                    {
                        read = _socket.Receive(buffer, SocketFlags.None);
                    }
                    catch (SocketException) { break; } // slopwatch-ignore: SW003 peer reset/closed mid-drain — stop, close will be a no-op

                    if (read == 0)
                        break; // peer FIN observed — receive side fully drained

                    drained += read;
                }
            }
            catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket disposed by a racing abort — nothing to drain
            catch (SocketException) { } // slopwatch-ignore: SW003 connection already reset — nothing to drain
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Abort()
        {
            // Cancel any pending write/flush immediately.
            _cts.Cancel();

            // Observe any in-flight write before we mark the write side closed — RSTing the
            // socket below will fault it, and nobody else awaits it on the Abort path.
            ObserveInflightWrite();

            // Mark the write side closed so no further bytes are buffered, and complete
            // WriteCompleted (no exception — Abort is an intentional RST, not an I/O failure).
            CompleteWrite(null);

            // Complete the inbound reader to unblock any pending read.
            // Exception if already completed / mid-read — safe to ignore.
            try { _inputReader.Complete(); } catch (Exception) { } // slopwatch-ignore: SW003 reader may already be completed / mid-read

            // RST the socket — SocketException/ObjectDisposedException if already closed.
            try
            {
                _socket.LingerState = new LingerOption(true, 0);
                _socket.Close();
            }
            catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 socket may already be disposed
            catch (SocketException) { } // slopwatch-ignore: SW003 socket may already be closed

            // Dispose the stream — ObjectDisposedException if already disposed.
            try { _stream.Dispose(); } catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 stream may already be disposed
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            // Capture any in-flight write BEFORE we mark the write side closed (CompleteWrite
            // nulls the field) so we can still settle it below and not dispose the stream out
            // from under it.
            Task? inflight;
            lock (_writeGate)
            {
                inflight = _inflightWrite;
                CompleteWriteLocked(null);
            }

            try { await _inputReader.CompleteAsync().ConfigureAwait(false); }
            catch (Exception) { } // slopwatch-ignore: SW003 reader may have an in-flight read during disposal

            // Best-effort: let any in-flight write settle so we don't dispose the stream out
            // from under it. It may throw OperationCanceledException / I/O errors during shutdown.
            if (inflight != null)
            {
                try { await inflight.ConfigureAwait(false); }
                catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected errors during disposal
                catch (Exception) { } // slopwatch-ignore: SW003 in-flight write failed during disposal — closing anyway
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            _socket.Dispose();
            _cts.Dispose();
        }

        /// <summary>
        /// A <see cref="PipeReader"/> decorator that serializes <see cref="Complete"/> /
        /// <see cref="CompleteAsync"/> against an in-flight consumer <see cref="ReadAsync"/> on the
        /// wrapped <c>StreamPipeReader</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The inbound side is consumer-driven (no read pump): the owning
        /// <see cref="TcpConnection"/> actor keeps a <see cref="ReadAsync"/> in flight on this
        /// reader. A <c>StreamPipeReader</c> is <b>not</b> safe to <see cref="PipeReader.Complete"/>
        /// while a read is in flight — <see cref="PipeReader.Complete"/> resets the
        /// <c>BufferSegment</c> the resuming read is about to commit bytes into, so the read's
        /// continuation does <c>segment.End += bytesRead</c> against a zero-length segment and
        /// throws <see cref="ArgumentOutOfRangeException"/> out of <c>BufferSegment.set_End</c>.
        /// That surfaces to the actor as an I/O error and, to the peer, as a spurious
        /// <c>ErrorClosed</c> on an otherwise graceful close.
        /// </para>
        /// <para>
        /// This gate closes that window. <see cref="ReadAsync"/> publishes the in-flight read (and a
        /// per-read CTS linked to the caller's token). The teardown paths first
        /// <em>quiesce</em> that read — cancel it and wait for it to observably finish — and only
        /// then complete the underlying reader, so <see cref="PipeReader.Complete"/> can never run
        /// concurrently with a read. The synchronous <see cref="Complete"/> (used by the abort path,
        /// which must not block) defers the underlying complete to the in-flight read's own
        /// continuation when a read is in flight, achieving the same serialization without awaiting.
        /// </para>
        /// </remarks>
        private sealed class ReadGate : PipeReader
        {
            private readonly PipeReader _inner;
            private readonly object _sync = new();

            // The in-flight consumer ReadAsync, or null when no read is outstanding. Tracked so the
            // teardown paths can cancel it and wait for it to settle before completing _inner.
            private Task? _inflightRead;

            // Cancels the in-flight ReadAsync so it unblocks promptly when we tear down.
            private CancellationTokenSource? _readCts;

            // Set once a teardown asked to complete the reader. Latched so a single completion wins
            // and a deferred (read-in-flight) completion knows to run once the read settles.
            private bool _completeRequested;
            private Exception? _completeError;
            private bool _innerCompleted;

            public ReadGate(PipeReader inner) => _inner = inner;

            public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                CancellationTokenSource linked;
                Task<ReadResult> read;
                lock (_sync)
                {
                    // If a teardown already completed (or requested completion of) the reader, do
                    // not start a new socket read — surface a completed/canceled result instead.
                    if (_completeRequested)
                        return new ReadResult(default, isCanceled: true, isCompleted: true);

                    _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked = _readCts;
                    read = _inner.ReadAsync(linked.Token).AsTask();
                    _inflightRead = read;
                }

                try
                {
                    // CRITICAL: do the copy-out + AdvanceTo on the underlying StreamPipeReader
                    // HERE, while this read is still tracked as in-flight (_inflightRead == read).
                    // The StreamPipeReader's BufferSegments are pooled and get recycled the moment
                    // Complete() runs. If we returned the raw ReadResult and let the consumer copy
                    // it out and AdvanceTo *outside* the gate, a teardown that observed
                    // _inflightRead == null (because this read already returned) could call
                    // _inner.Complete() and recycle those segments while the consumer is mid-CopyTo
                    // — throwing ArgumentOutOfRangeException out of BuffersExtensions.CopyTo /
                    // BufferSegment.set_End and surfacing to the peer as a spurious ErrorClosed.
                    //
                    // By snapshotting the bytes into a fresh, segment-independent array and calling
                    // _inner.AdvanceTo before we clear _inflightRead, the consumer never touches the
                    // recyclable segments and Complete() can never race them. The single copy here
                    // replaces the one the consumer used to do, so per-message allocation is
                    // unchanged.
                    var result = await read.ConfigureAwait(false);
                    return SnapshotAndAdvance(result);
                }
                finally
                {
                    bool runDeferredComplete;
                    Exception? deferredError;
                    lock (_sync)
                    {
                        if (ReferenceEquals(_inflightRead, read))
                            _inflightRead = null;
                        linked.Dispose();
                        if (ReferenceEquals(_readCts, linked))
                            _readCts = null;

                        // A synchronous Complete() that arrived while this read was in flight
                        // deferred the underlying complete to us — run it now that the read has
                        // settled and no read is outstanding.
                        runDeferredComplete = _completeRequested && !_innerCompleted && _inflightRead == null;
                        deferredError = _completeError;
                        if (runDeferredComplete)
                            _innerCompleted = true;
                    }

                    if (runDeferredComplete)
                        CompleteInnerSafely(deferredError);
                }
            }

            /// <summary>
            /// Copies the just-read bytes out of the underlying <c>StreamPipeReader</c>'s pooled
            /// segments into a fresh, segment-independent array and advances the underlying reader
            /// past them — all while the read is still tracked in-flight so a concurrent
            /// <see cref="Complete"/> cannot recycle the segments out from under the copy. The
            /// returned <see cref="ReadResult"/> is backed by the copied array, so the consumer
            /// never touches the recyclable segments. Empty reads (EOF / cancellation) return a
            /// default-buffer result without allocating.
            /// </summary>
            private ReadResult SnapshotAndAdvance(ReadResult result)
            {
                var buffer = result.Buffer;
                if (buffer.Length == 0)
                {
                    // Nothing to copy. Still advance the underlying reader so it does not see the
                    // same (empty) segment again, then surface the completion/cancel flags.
                    _inner.AdvanceTo(buffer.Start, buffer.End);
                    return new ReadResult(ReadOnlySequence<byte>.Empty, result.IsCanceled, result.IsCompleted);
                }

                var array = new byte[checked((int)buffer.Length)];
                buffer.CopyTo(array);

                // Consume everything we copied. We are still inside the gate (this read is still
                // _inflightRead), so this AdvanceTo cannot race a Complete() on _inner.
                _inner.AdvanceTo(buffer.End);

                return new ReadResult(new ReadOnlySequence<byte>(array), result.IsCanceled, result.IsCompleted);
            }

            public override bool TryRead(out ReadResult result)
            {
                lock (_sync)
                {
                    if (_completeRequested)
                    {
                        result = new ReadResult(default, isCanceled: true, isCompleted: true);
                        return true;
                    }

                    if (!_inner.TryRead(out var inner))
                    {
                        result = default;
                        return false;
                    }

                    // Snapshot + advance under _sync (same lock Complete() takes) so the segments
                    // can't be recycled out from under the copy. Mirrors SnapshotAndAdvance so a
                    // TryRead consumer also gets a segment-independent ReadResult, even though no
                    // current caller uses TryRead on this reader.
                    result = SnapshotAndAdvance(inner);
                    return true;
                }
            }

            // No-ops: ReadAsync already copied the bytes out of the underlying reader's pooled
            // segments and advanced _inner past them (see SnapshotAndAdvance), so the ReadResult
            // the consumer holds is segment-independent. The consumer's AdvanceTo therefore refers
            // to the synthetic snapshot buffer, not _inner — forwarding it would double-advance the
            // underlying StreamPipeReader. Keeping these as no-ops preserves the PipeReader
            // contract for the consumer while ensuring _inner is advanced exactly once, inside the
            // gate, before _inflightRead is cleared.
            public override void AdvanceTo(SequencePosition consumed) { }

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) { }

            public override void CancelPendingRead()
            {
                CancellationTokenSource? cts;
                lock (_sync)
                {
                    cts = _readCts;
                }

                if (cts != null)
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 read already settled and disposed its CTS
                }

                _inner.CancelPendingRead();
            }

            /// <summary>
            /// Cancels the in-flight read (if any), waits for it to settle, then completes the
            /// underlying reader. The teardown paths call this so <see cref="PipeReader.Complete"/>
            /// never runs concurrently with a read.
            /// </summary>
            public async Task QuiesceAndCompleteAsync(Exception? exception = null)
            {
                Task? inflight;
                CancellationTokenSource? cts;
                lock (_sync)
                {
                    if (_innerCompleted)
                        return;

                    _completeRequested = true;
                    _completeError ??= exception;
                    inflight = _inflightRead;
                    cts = _readCts;

                    // No read in flight: complete now under the same lock-ordering as the deferred
                    // path so the two never both complete _inner.
                    if (inflight == null)
                        _innerCompleted = true;
                }

                if (inflight == null)
                {
                    CompleteInnerSafely(exception);
                    return;
                }

                // Cancel the in-flight read so it unblocks promptly, then wait for it to settle.
                // The read's own continuation may complete _inner (the deferred path); whichever of
                // us observes _inflightRead == null && !_innerCompleted does the completion.
                if (cts != null)
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 read already settled and disposed its CTS
                }

                try { await inflight.ConfigureAwait(false); }
                catch (Exception) { } // slopwatch-ignore: SW003 the read we just cancelled is expected to cancel/fault

                bool completeNow;
                lock (_sync)
                {
                    completeNow = !_innerCompleted;
                    if (completeNow)
                        _innerCompleted = true;
                }

                if (completeNow)
                    CompleteInnerSafely(exception);
            }

            public override void Complete(Exception? exception = null)
            {
                CancellationTokenSource? cts;
                bool completeNow;
                lock (_sync)
                {
                    if (_innerCompleted)
                        return;

                    _completeRequested = true;
                    _completeError ??= exception;
                    cts = _readCts;

                    // If a read is in flight, DO NOT complete _inner here — that is the exact
                    // corruption. Cancel the read and let its continuation (in ReadAsync's finally)
                    // complete _inner once it settles. If no read is in flight, complete now.
                    completeNow = _inflightRead == null;
                    if (completeNow)
                        _innerCompleted = true;
                }

                if (cts != null)
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { } // slopwatch-ignore: SW003 read already settled and disposed its CTS
                }

                if (completeNow)
                    CompleteInnerSafely(exception);
            }

            public override ValueTask CompleteAsync(Exception? exception = null)
                => new(QuiesceAndCompleteAsync(exception));

            private void CompleteInnerSafely(Exception? exception)
            {
                try { _inner.Complete(exception); }
                catch (Exception) { } // slopwatch-ignore: SW003 underlying reader may already be completed
            }
        }
    }
}
