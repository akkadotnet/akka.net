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
    /// Owns two pipes (input + output) and two pump loops that bridge them to a NetworkStream.
    /// </summary>
    public sealed class TcpTransportConnection : ITransportConnection
    {
        private readonly Socket _socket;
        private readonly Stream _stream;
        private readonly Pipe _inputPipe;
        private readonly Pipe _outputPipe;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Creates a transport connection from an already-connected socket.
        /// Starts the read and write pump loops immediately.
        /// </summary>
        public TcpTransportConnection(Socket socket, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: false);

            _inputPipe = new Pipe(inputPipeOptions ?? PipeOptions.Default);
            _outputPipe = new Pipe(outputPipeOptions ?? PipeOptions.Default);

            ReadCompleted = RunReadPumpAsync(_cts.Token);
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

            _inputPipe = new Pipe(inputPipeOptions ?? PipeOptions.Default);
            _outputPipe = new Pipe(outputPipeOptions ?? PipeOptions.Default);

            ReadCompleted = RunReadPumpAsync(_cts.Token);
            WriteCompleted = RunWritePumpAsync(_cts.Token);
        }

        public PipeReader Input => _inputPipe.Reader;

        /// <inheritdoc/>
        public Task ReadCompleted { get; }

        /// <inheritdoc/>
        public Task WriteCompleted { get; }

        /// <inheritdoc/>
        public bool HasReadError => Volatile.Read(ref _hasReadError);

        /// <inheritdoc/>
        public Exception? ReadError => Volatile.Read(ref _readError);

        private bool _hasReadError;
        private Exception? _readError;

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
        }

        public async Task CloseAsync()
        {
            // Complete the output pipe — write pump will drain and exit
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for write pump to finish flushing
            await WriteCompleted.ConfigureAwait(false);

            // Cancel to unblock the read pump (which may be blocked on stream.ReadAsync)
            _cts.Cancel();

            // Wait for read pump to exit — it may throw OperationCanceledException (from CTS cancel)
            // or IOException/SocketException (from stream close). Both are expected during shutdown.
            try { await ReadCompleted.ConfigureAwait(false); }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected cancellation or I/O error during shutdown

            // Close the stream and socket
            await _stream.DisposeAsync().ConfigureAwait(false);
            _socket.Close();
        }

        public void Abort()
        {
            // Cancel pumps immediately
            _cts.Cancel();

            // Complete pipes to unblock any pending reads/writes on them.
            // InvalidOperationException if already completed — safe to ignore.
            try { _outputPipe.Writer.Complete(); } catch (InvalidOperationException) { } // slopwatch-ignore: SW003 pipe may already be completed
            try { _inputPipe.Writer.Complete(); } catch (InvalidOperationException) { } // slopwatch-ignore: SW003 pipe may already be completed

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
            await _inputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for pump tasks — they may throw OperationCanceledException or I/O errors during shutdown.
            try
            {
                await Task.WhenAll(ReadCompleted, WriteCompleted).ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected errors during disposal

            await _stream.DisposeAsync().ConfigureAwait(false);
            _socket.Dispose();
            _cts.Dispose();
        }

        /* ================================================================= */
        /*  Read pump: Stream → Input Pipe                                   */
        /* ================================================================= */

        private async Task RunReadPumpAsync(CancellationToken ct)
        {
            var writer = _inputPipe.Writer;
            Exception? error = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var memory = writer.GetMemory();
                    var bytesRead = await _stream.ReadAsync(memory, ct).ConfigureAwait(false);

                    if (bytesRead == 0)
                        break; // EOF — peer closed

                    writer.Advance(bytesRead);

                    var flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flushResult.IsCompleted || flushResult.IsCanceled)
                        break; // Reader (actor) is done
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { } // slopwatch-ignore: SW003 normal CTS-driven shutdown
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                // Set error fields BEFORE completing the pipe writer.
                // This ensures the actor can synchronously check HasReadError
                // when it handles the PipeReadCompleted with IsCompleted,
                // even if the ReadPumpFailed message hasn't been processed yet.
                if (error != null)
                {
                    Volatile.Write(ref _readError, error);
                    Volatile.Write(ref _hasReadError, true);
                }

                // Complete the pipe writer WITHOUT passing the exception.
                // This preserves buffered data so the actor can drain it before
                // checking ReadCompleted.IsFaulted for the error.
                await writer.CompleteAsync().ConfigureAwait(false);
            }

            // If there was an error, throw it so ReadCompleted.IsFaulted is true.
            // This must happen AFTER the pipe writer is completed so buffered data
            // is available for the actor to drain.
            if (error != null)
                throw error;
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

            // If there was an error, throw it so WriteCompleted.IsFaulted is true -- mirrors
            // RunReadPumpAsync's own rethrow above. Without this, a write-side I/O failure (e.g. a
            // broken pipe discovered while flushing to a peer that vanished) would complete this
            // pump's OWN Task successfully, so nothing proactively observing WriteCompleted (see
            // TcpConnection.StartTransport's MonitorWritePumpAsync) would ever learn the write side
            // had failed -- the failure would only ever surface reactively, on whatever NEXT write
            // attempt happens to re-throw it synchronously from the now-faulted pipe (which, for an
            // otherwise-idle one-way connection, may never come).
            if (error != null)
                throw error;
        }
    }
}
