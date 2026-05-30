//-----------------------------------------------------------------------
// <copyright file="PipeConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Event;
using Google.Protobuf;

namespace Akka.Remote.Transport.Pipelines
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Represents a single TCP association managed by <see cref="TcpPipeTransport"/>.
    ///
    /// <para>
    /// Owns two async loops running concurrently via <see cref="Start"/>:
    /// <list type="bullet">
    ///   <item><see cref="ReadLoopAsync"/> — reads length-prefixed frames from a
    ///     <see cref="PipeReader"/>-wrapped stream and delivers them to the registered
    ///     <see cref="IHandleEventListener"/>.</item>
    ///   <item><see cref="WriteLoopAsync"/> — drains a bounded
    ///     <see cref="Channel{T}"/> and writes coalesced length-prefixed frames to the stream.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Frame format (little-endian, matching Akka.Remote's DotNetty default):</b>
    /// <code>
    /// ┌──────────────────────────┬───────────────────────────┐
    /// │  length : int32 (4 B LE) │  payload : byte[length]   │
    /// └──────────────────────────┴───────────────────────────┘
    /// </code>
    /// </para>
    ///
    /// <!-- CopilotNotes: Little-endian framing matches akka.remote.dot-netty.tcp byte-order = "little-endian".
    ///      This makes rolling upgrades from DotNetty to PipeTransport wire-compatible when both use
    ///      the protobuf AkkaProtocol codec. Phase 2 can negotiate a frame-tag format for further gains. -->
    /// </summary>
    internal sealed class PipeConnection : IDisposable
    {
        // ── Frame constants ─────────────────────────────────────────────────────
        private const int FrameHeaderSize = 4; // 4-byte LE int32 length prefix

        // ── Core infrastructure ────────────────────────────────────────────────
        private readonly Socket _socket;
        private readonly Stream _stream; // NetworkStream or SslStream
        private readonly PipeReader _reader;
        private readonly Channel<ByteString> _writeChannel;
        private readonly CancellationTokenSource _cts;
        private readonly ILoggingAdapter _log;
        private readonly TcpPipeTransport _transport;

        // Listener is set asynchronously once ReadHandlerSource.Task completes.
        // CopilotNotes: Declared volatile so the write loop's TryEnqueueWrite has a cheap
        // fast-exit path without taking a lock. The read loop awaits the handler source
        // task before processing any frames, so the pre-listener buffer below is a
        // belt-and-suspenders safety net for the inbound path only.
        private volatile IHandleEventListener? _listener;

        // Closed gate: 0 = open, 1 = closed. Used in BeginDisassociate / DisassociateQuiet.
        private int _closed;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>The <see cref="AssociationHandle"/> exposed to the upper transport layers.</summary>
        public PipeAssociationHandle Handle { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Constructs a <see cref="PipeConnection"/> over an already-connected (and optionally TLS-wrapped) stream.
        /// Call <see cref="Start"/> after construction to start the read/write loops.
        /// </summary>
        /// <param name="socket">The underlying socket. Owned by this connection; closed on <see cref="Dispose"/>.</param>
        /// <param name="stream">The <see cref="NetworkStream"/> or <see cref="System.Net.Security.SslStream"/> to use.</param>
        /// <param name="handle">The pre-constructed association handle. Its <see cref="PipeAssociationHandle.Connection"/> is wired here.</param>
        /// <param name="transport">Parent transport (used for connection-set bookkeeping).</param>
        /// <param name="log">Logger.</param>
        /// <param name="writeChannelCapacity">Bounded capacity of the outbound write channel.</param>
        internal PipeConnection(
            Socket socket,
            Stream stream,
            PipeAssociationHandle handle,
            TcpPipeTransport transport,
            ILoggingAdapter log,
            int writeChannelCapacity)
        {
            _socket    = socket;
            _stream    = stream;
            _reader    = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            _writeChannel = Channel.CreateBounded<ByteString>(new BoundedChannelOptions(writeChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                // CopilotNotes: DropWrite means a full channel returns false from TryWrite,
                // which maps to the AssociationHandle.Write contract: "false = dropped, no duplicate".
                FullMode = BoundedChannelFullMode.DropWrite
            });
            _cts       = new CancellationTokenSource();
            _log       = log;
            _transport = transport;
            Handle     = handle;

            // Wire circular reference: handle -> connection
            handle.Connection = this;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the read and write loops as background <see cref="Task"/>s.
        /// Must be called exactly once after construction.
        /// </summary>
        public void Start()
        {
            // Register listener callback: when ProtocolStateActor (or equivalent) completes
            // ReadHandlerSource, store the listener reference so the read loop can deliver frames.
            Handle.ReadHandlerSource.Task.ContinueWith(
                t => { _listener = t.Result; },
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            // Fire-and-forget — exceptions are caught and logged inside each loop.
            _ = ReadLoopAsync(_cts.Token);
            _ = WriteLoopAsync(_cts.Token);
        }

        /// <summary>
        /// Enqueue a payload for writing. Thread-safe. Returns <c>false</c> if the
        /// channel is at capacity (write dropped) or the connection is closing.
        /// </summary>
        public bool TryEnqueueWrite(ByteString payload)
        {
            if (Volatile.Read(ref _closed) == 1)
                return false;

            // ToByteArray() copies — Phase 2 can eliminate this via IBufferWriter writer.
            return _writeChannel.Writer.TryWrite(payload);
        }

        /// <summary>
        /// Begin graceful disassociation: drain any pending writes then close.
        /// Safe to call multiple times concurrently.
        /// </summary>
        public void BeginDisassociate()
        {
            if (Interlocked.CompareExchange(ref _closed, 1, 0) == 0)
            {
                // Signal writer channel that no more items will be enqueued.
                _writeChannel.Writer.TryComplete();
                // Cancel both read + write loops.
                _cts.Cancel();
            }
        }

        /// <summary>
        /// Quiet close used during transport-level shutdown — does NOT notify the listener
        /// (the actor system is shutting down anyway).
        /// </summary>
        public void DisassociateQuiet()
        {
            if (Interlocked.CompareExchange(ref _closed, 1, 0) == 0)
            {
                _writeChannel.Writer.TryComplete();
                _cts.Cancel();
                CloseSocket();
            }
        }

        // ── Read loop ──────────────────────────────────────────────────────────

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                // Wait for the upper layer (ProtocolStateActor) to register itself as the
                // IHandleEventListener before we start processing frames.
                // CopilotNotes: WaitAsync is .NET 6+ and returns a faulted task if ct is cancelled,
                // which is caught below. On net10.0 this is in-box.
                var listener = await Handle.ReadHandlerSource.Task
                    .WaitAsync(ct)
                    .ConfigureAwait(false);
                _listener = listener;

                while (!ct.IsCancellationRequested)
                {
                    var result = await _reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer;

                    while (TryParseFrame(ref buffer, out var frame))
                    {
                        // CopilotNotes: ByteString.CopyFrom allocates per frame (the frame bytes are
                        // already in a pooled PipeReader buffer). Phase 2 can avoid the copy by
                        // teaching IHandleEventListener about ReadOnlySequence<byte> directly.
                        var bytes = frame.IsSingleSegment
                            ? ByteString.CopyFrom(frame.FirstSpan)
                            : ByteString.CopyFrom(frame.ToArray());

                        listener.Notify(new InboundPayload(bytes));
                    }

                    _reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted || result.IsCanceled)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — no-op.
            }
            catch (Exception ex) when (IsConnectionReset(ex))
            {
                _log.Info("Pipe transport: connection reset by remote [{0}]", Handle.RemoteAddress);
            }
            catch (EndOfStreamException)
            {
                _log.Debug("Pipe transport: remote [{0}] closed the connection.", Handle.RemoteAddress);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Pipe transport: read loop error on connection [{0}]", Handle.RemoteAddress);
            }
            finally
            {
                await _reader.CompleteAsync().ConfigureAwait(false);
                NotifyDisassociated();
                CloseSocket();
                _transport.RemoveConnection(this);
            }
        }

        // ── Write loop ─────────────────────────────────────────────────────────

        /// <summary>
        /// Asynchronous write loop that drains <see cref="_writeChannel"/> and sends
        /// coalesced length-prefixed frames to the underlying stream.
        ///
        /// <para>
        /// <b>Double-buffer ping-pong optimisation 🏓</b>: two <see cref="ArrayBufferWriter{T}"/>
        /// slots are pre-allocated and alternated each cycle.  While the OS/kernel is
        /// transmitting the <em>previous</em> batch (in-flight <see cref="Task"/>), the CPU
        /// simultaneously drains new messages from the channel into the <em>other</em> buffer.
        /// The in-flight task is only awaited immediately before we would start the next
        /// <see cref="Stream.WriteAsync(ReadOnlyMemory{byte},CancellationToken)"/> call, giving
        /// maximum overlap between user-space batching and kernel I/O.
        /// </para>
        ///
        /// <para>
        /// <b>Send-buffer watermarking 💧</b>: each batch is bounded to roughly the socket
        /// <see cref="Socket.SendBufferSize"/>. Handing <c>WriteAsync</c> more than the kernel
        /// can absorb in a single syscall forces partial writes — the I/O completion port
        /// has to wait for the receiver / wire to drain SO_SNDBUF before the second half can
        /// be queued, which serialises us behind kernel I/O and destroys the ping-pong overlap.
        /// By cutting the batch at the watermark we keep each individual <c>WriteAsync</c>
        /// "kernel-sized" while still draining the channel as fast as possible in the next slot.
        /// </para>
        ///
        /// <!-- CopilotNotes: ValueTask is NOT safe to store and re-await; .AsTask() boxes once
        ///      but lets us hold the Task across loop iterations.  The swap (activeIdx ^= 1)
        ///      is only performed when a write is actually started so we never clear a buffer
        ///      that is still live inside an in-flight WriteAsync.
        ///
        ///      The watermark is a *soft* cap: a single payload larger than the watermark is
        ///      still written as its own batch (we never split a single Akka frame across two
        ///      WriteAsync calls). This is fine — the kernel will just do a multi-segment send
        ///      itself, and we still avoid coalescing additional frames behind a huge one. -->
        /// </summary>
        private async Task WriteLoopAsync(CancellationToken ct)
        {
            // Two buffers — one fills while the other is in-flight to the stream. ✨
             var buffers = new ArrayBufferWriter<byte>[]
             {
                 new(initialCapacity: 8192),
                 new(initialCapacity: 8192),
             };

            var activeIdx   = 0;
            Task? inflightWrite = null;

            // ── Send-buffer watermark ──
            // Prefer the configured value; fall back to the socket's actual SO_SNDBUF
            // (defaults to 64 KB on Windows, ~16-64 KB on Linux). Floor at 8 KB so a
            // pathological "0" never collapses batching entirely. 🌸
            var settingsSnd = _transport._settings.SendBufferSize;
            int sendWatermark;
            try
            {
                sendWatermark = settingsSnd > 0 ? settingsSnd : _socket.SendBufferSize;
            }
            catch (Exception)
            {
                // Socket might already be closed in a teardown race — fall back to a sane default.
                sendWatermark = 64 * 1024;
            }
            if (sendWatermark < 8 * 1024)
                sendWatermark = 128 * 1024;

            // Local helper: write a single length-prefixed frame into the given buffer.
            // CopilotNotes: Inlined as a static local function — no closure allocation,
            // and the JIT will happily inline it at the call sites. 💝
            static void WriteFrame(ArrayBufferWriter<byte> dest, ByteString payload)
            {
                // GetSpan returns at least 'count' bytes. We write the 4-byte LE length
                // header then the payload bytes back-to-back.
                var sp = dest.GetSpan(FrameHeaderSize + payload.Length);
                BinaryPrimitives.WriteInt32LittleEndian(
                    //dest.GetSpan(FrameHeaderSize + payload.Length),
                    sp,
                    payload.Length);
                //dest.Advance(FrameHeaderSize);

                payload.Span.CopyTo(sp.Slice(FrameHeaderSize));
                dest.Advance(FrameHeaderSize+payload.Length);
            }

            try
            {
                // Carry-over slot: if a payload would push the current batch over the
                // watermark, we stash it here, flush the in-progress batch, then start
                // the next batch with the stashed payload. 🎀
                // CopilotNotes: This is what makes the watermark a "hard cap whenever
                // possible" instead of a "soft cap" — the only way a single batch can
                // exceed the watermark is if its *first* frame is already over-budget,
                // which is unavoidable (we never split a single Akka frame).
                ByteString? carry = null;

                while (true)
                {
                    // Only block on the channel if we don't already have a carried-over
                    // payload to write. Carry-over guarantees forward progress even when
                    // the channel is momentarily empty.
                    if (carry is null)
                    {
                        if (!await _writeChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                            break; // Channel completed — exit the loop.
                    }

                    var active = buffers[activeIdx];
                    // Safe to Clear here: the PREVIOUS write that used this slot was
                    // already awaited (we await `inflightWrite` before swapping back to it).
                    active.Clear();

                    // 1) Drain the carried-over frame first, if any. By construction this
                    //    frame did NOT fit alongside the previous batch's tail, so it goes
                    //    into the freshly-cleared buffer as the new batch's first frame.
                    if (carry is not null)
                    {
                        WriteFrame(active, carry);
                        carry = null;
                    }

                    // 2) Pull additional frames from the channel — but bail (carry) the
                    //    moment one would tip us over the watermark, so we flush a
                    //    cleanly-sized batch instead of overshooting.
                    while (_writeChannel.Reader.TryRead(out var payload))
                    {

                        // CopilotNotes: The `active.WrittenCount > 0` guard ensures that a
                        // single oversized frame still gets sent (as its own batch) on the
                        // next iteration — we only refuse to *append* it to an existing batch.
                        if (active.WrittenCount > 0 &&
                            active.WrittenCount + (FrameHeaderSize + payload.Length) > sendWatermark)
                        {
                            carry = payload;
                            break;
                        }

                        WriteFrame(active, payload);
                    }

                    if (active.WrittenCount == 0)
                        continue; // Spurious wake-up — back to WaitToReadAsync.

                    // Await the PREVIOUS write before launching the next one.
                    // At this point inflightWrite owns the OTHER buffer slot, so once it
                    // completes that slot is idle and can be safely cleared next cycle.
                    // No explicit Flush needed: NetworkStream.WriteAsync flushes immediately.
                    // SslStream also flushes per WriteAsync call.
                    if (inflightWrite != null)
                        await inflightWrite.ConfigureAwait(false);

                    // Kick off the write for the freshly-filled buffer without awaiting it
                    // yet — this is the key: kernel I/O runs concurrently with the next batch fill.
                    inflightWrite = _stream.WriteAsync(active.WrittenMemory, ct).AsTask();

                    // Swap: next iteration fills the slot that was just awaited (now idle).
                    activeIdx ^= 1;
                }

                // Channel drained — await the final in-flight write so we don't close
                // the stream while bytes are still being flushed to the OS. 🌸
                if (inflightWrite != null)
                    await inflightWrite.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — no-op.
            }
            catch (Exception ex) when (IsConnectionReset(ex))
            {
                _log.Info("Pipe transport: write connection reset on [{0}]", Handle.RemoteAddress);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Pipe transport: write loop error on connection [{0}]", Handle.RemoteAddress);
            }
        }

        // ── Frame parsing ──────────────────────────────────────────────────────

        /// <summary>
        /// Tries to parse one complete frame from <paramref name="buffer"/>.
        /// On success, advances <paramref name="buffer"/> past the consumed header + payload.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
        {
            if (buffer.Length < FrameHeaderSize)
            {
                frame = default;
                return false;
            }

            Span<byte> header = stackalloc byte[FrameHeaderSize];
            buffer.Slice(0, FrameHeaderSize).CopyTo(header);
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);

            // Guard against corrupt / malicious frames.
            if (payloadLength < 0 || buffer.Length < FrameHeaderSize + (long)payloadLength)
            {
                frame = default;
                return false;
            }

            frame  = buffer.Slice(FrameHeaderSize, payloadLength);
            buffer = buffer.Slice(FrameHeaderSize + payloadLength);
            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void NotifyDisassociated()
        {
            // Only notify if the listener is already wired — if not, the connection died
            // before the upper layer could register, which is a non-fatal edge case.
            _listener?.Notify(new Disassociated(DisassociateInfo.Unknown));
        }

        private void CloseSocket()
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch (Exception) { /* Socket may already be closed */ }
            try { _socket.Close();  } catch (Exception) { /* Best-effort */ }
            try { _stream.Dispose(); } catch (Exception) { /* Best-effort */ }
        }

        private static bool IsConnectionReset(Exception ex) =>
            ex is SocketException { SocketErrorCode: SocketError.ConnectionReset
                                                   or SocketError.ConnectionAborted
                                                   or SocketError.OperationAborted }
            || (ex is IOException ioEx && ioEx.InnerException is SocketException);

        // ── IDisposable ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose()
        {
            BeginDisassociate();
            _cts.Dispose();
        }
    }
}


