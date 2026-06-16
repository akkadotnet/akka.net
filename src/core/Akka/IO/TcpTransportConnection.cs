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
    /// Owns an output pipe + write pump that bridge to a NetworkStream, and exposes a
    /// <see cref="PipeReader"/> for the inbound side that the consuming actor drives directly.
    /// </summary>
    public sealed class TcpTransportConnection : ITransportConnection
    {
        // Segment size for the inbound StreamPipeReader. Large enough that one ReadAsync
        // typically pulls a full kernel receive buffer in a single call.
        private const int ReadBufferSize = 64 * 1024;

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
        private readonly PipeReader _inputReader;
        private readonly Pipe _outputPipe;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Creates a transport connection from an already-connected socket.
        /// Starts the write pump loop immediately; inbound reads are driven on demand
        /// by the consumer via <see cref="Input"/>.
        /// </summary>
        public TcpTransportConnection(Socket socket, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: false);

            // Inbound: no background read pump. The consumer (TcpConnection actor) drives
            // PipeReader.ReadAsync directly, so a socket read runs on the consumer's own
            // continuation instead of being flushed across a Pipe to a second thread — this
            // removes the pump→Pipe→consumer cross-thread hand-off (one fewer thread-pool
            // dispatch per chunk). inputPipeOptions (readerScheduler etc.) is intentionally
            // unused now that there is no Pipe on the read side.
            _inputReader = PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: ReadBufferSize, leaveOpen: true));
            _outputPipe = new Pipe(outputPipeOptions ?? PipeOptions.Default);

            ReadCompleted = Task.CompletedTask;
            WriteCompleted = RunWritePumpAsync(_cts.Token);
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
            // PipeReader, not a background pump + Pipe.
            _inputReader = PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: ReadBufferSize, leaveOpen: true));
            _outputPipe = new Pipe(outputPipeOptions ?? PipeOptions.Default);

            ReadCompleted = Task.CompletedTask;
            WriteCompleted = RunWritePumpAsync(_cts.Token);
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

        public ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            var writer = _outputPipe.Writer;
            writer.Write(data.Span);
            return writer.FlushAsync(ct);
        }

        public ValueTask<FlushResult> WriteAsync(ReadOnlySequence<byte> data, CancellationToken ct = default)
        {
            var writer = _outputPipe.Writer;
            foreach (var segment in data)
            {
                writer.Write(segment.Span);
            }

            return writer.FlushAsync(ct);
        }

        public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
        {
            return _outputPipe.Writer.FlushAsync(ct);
        }

        public async Task ShutdownAsync()
        {
            // Tcp.ConfirmedClose: this is a HALF close. Flush our pending writes, then send
            // FIN (Shutdown(Send)) but leave the read side open. The consuming actor keeps
            // driving the inbound PipeReader until it observes the peer's FIN (EOF), at which
            // point it finalizes the close. We must NOT drain here — that inbound data still
            // belongs to the consumer.

            // Complete the output pipe — write pump will drain and exit
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for write pump to finish flushing
            await WriteCompleted.ConfigureAwait(false);

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

            // Complete the output pipe — write pump will drain and exit
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for write pump to finish flushing
            await WriteCompleted.ConfigureAwait(false);

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

            // Cancel to unblock the write pump; the inbound reader is consumer-driven,
            // so completing it releases any pooled buffers and frees the underlying stream
            // for the bounded socket-level drain below.
            _cts.Cancel();
            try { _inputReader.Complete(); } catch (Exception) { } // slopwatch-ignore: SW003 reader may have an in-flight read during teardown

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
            // Cancel pumps immediately
            _cts.Cancel();

            // Complete pipes/readers to unblock any pending reads/writes.
            // InvalidOperationException if already completed — safe to ignore.
            try { _outputPipe.Writer.Complete(); } catch (InvalidOperationException) { } // slopwatch-ignore: SW003 pipe may already be completed
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

            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            try { await _inputReader.CompleteAsync().ConfigureAwait(false); }
            catch (Exception) { } // slopwatch-ignore: SW003 reader may have an in-flight read during disposal

            // Wait for the write pump — it may throw OperationCanceledException or I/O errors during shutdown.
            try
            {
                await WriteCompleted.ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected errors during disposal

            await _stream.DisposeAsync().ConfigureAwait(false);
            _socket.Dispose();
            _cts.Dispose();
        }

        /* ================================================================= */
        /*  Write pump: Output Pipe → Stream                                 */
        /* ================================================================= */

        private async Task RunWritePumpAsync(CancellationToken ct)
        {
            var reader = _outputPipe.Reader;
            Exception? error = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var readResult = await reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = readResult.Buffer;

                    if (buffer.Length > 0)
                    {
                        // Write each contiguous segment to the stream.
                        // Pipe segments are typically large (4KB+), so this is
                        // usually 1 WriteAsync call per ReadAsync wake-up.
                        foreach (var segment in buffer)
                        {
                            await _stream.WriteAsync(segment, ct).ConfigureAwait(false);
                        }
                    }

                    reader.AdvanceTo(buffer.End);

                    if (readResult.IsCompleted)
                        break; // Writer (actor) completed the pipe
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { } // slopwatch-ignore: SW003 normal CTS-driven shutdown
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                await reader.CompleteAsync(error).ConfigureAwait(false);
            }
        }
    }
}
