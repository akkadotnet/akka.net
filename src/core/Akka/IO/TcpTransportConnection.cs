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
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// TCP implementation of <see cref="ITransportConnection"/>.
    /// Owns two pipes (input + output) and two pump loops that bridge them to a network stream,
    /// which may be a plaintext <see cref="NetworkStream"/> or an authenticated TLS stream.
    /// </summary>
    public sealed class TcpTransportConnection : ITransportConnection, ITransportConnectionLifecycle
    {
        private readonly Socket _socket;
        private readonly object _lifecycleGate = new();
        private Stream? _stream;
        private readonly Pipe _inputPipe;
        private readonly Pipe _outputPipe;
        private readonly CancellationTokenSource _cts = new();
        private Task _ready = Task.CompletedTask;
        private bool _started;
        private bool _disposed;
        private bool _tlsWriteShutdown;

        /// <summary>
        /// Creates a transport connection from an already-connected socket.
        /// Starts the read and write pump loops immediately.
        /// </summary>
        public TcpTransportConnection(Socket socket, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
            : this(socket, new NetworkStream(socket, ownsSocket: false), inputPipeOptions, outputPipeOptions,
                startImmediately: true)
        {
        }

        /// <summary>
        /// Creates a transport connection from an existing stream (for TLS or testing).
        /// </summary>
        public TcpTransportConnection(Socket socket, Stream stream, PipeOptions? inputPipeOptions = null,
            PipeOptions? outputPipeOptions = null)
            : this(socket, stream, inputPipeOptions, outputPipeOptions, startImmediately: true)
        {
        }

        private TcpTransportConnection(Socket socket, Stream? stream, PipeOptions? inputPipeOptions,
            PipeOptions? outputPipeOptions, bool startImmediately)
        {
            _socket = socket;
            _stream = stream;

            _inputPipe = new Pipe(inputPipeOptions ?? PipeOptions.Default);
            _outputPipe = new Pipe(outputPipeOptions ?? PipeOptions.Default);

            if (startImmediately)
                StartPumps();
        }

        internal static ITransportConnection CreateForIncoming(Socket socket, Stream? existingStream,
            PipeOptions? inputPipeOptions, PipeOptions? outputPipeOptions, TlsServerSettings? tlsSettings,
            ILoggingAdapter log)
        {
            var stream = existingStream ?? (tlsSettings is null ? new NetworkStream(socket, ownsSocket: false) : null);
            var transport = new TcpTransportConnection(socket, stream, inputPipeOptions, outputPipeOptions,
                startImmediately: false);

            if (tlsSettings is not null)
            {
                var remotePeer = socket.RemoteEndPoint?.ToString() ?? "unknown remote peer";
                transport.BeginTlsInitialization(tlsSettings.HandshakeTimeout,
                    () => tlsSettings.CreateAuthenticationOptions(remotePeer, log),
                    (sslStream, cancellationToken, options) =>
                        sslStream.AuthenticateAsServerAsync(options, cancellationToken));
            }

            return transport;
        }

        internal static ITransportConnection CreateForOutgoing(Socket socket, EndPoint remoteAddress,
            PipeOptions? inputPipeOptions, PipeOptions? outputPipeOptions, TlsClientSettings? tlsSettings,
            ILoggingAdapter log)
        {
            var stream = tlsSettings is null ? new NetworkStream(socket, ownsSocket: false) : null;
            var transport = new TcpTransportConnection(socket, stream, inputPipeOptions, outputPipeOptions,
                startImmediately: false);

            if (tlsSettings is not null)
            {
                var remotePeer = socket.RemoteEndPoint?.ToString() ?? remoteAddress.ToString() ?? "unknown remote peer";
                var targetHost = tlsSettings.TargetHost ?? (remoteAddress switch
                {
                    DnsEndPoint dnsEndPoint => dnsEndPoint.Host,
                    IPEndPoint ipEndPoint => ipEndPoint.Address.ToString(),
                    _ => remoteAddress.ToString() ?? "unknown remote peer"
                });
                transport.BeginTlsInitialization(tlsSettings.HandshakeTimeout,
                    () => tlsSettings.CreateAuthenticationOptions(targetHost, remotePeer, log),
                    (sslStream, cancellationToken, options) =>
                        sslStream.AuthenticateAsClientAsync(options, cancellationToken));
            }

            return transport;
        }

        private void BeginTlsInitialization<TOptions>(TimeSpan timeout, Func<TOptions> createOptions,
            Func<SslStream, CancellationToken, TOptions, Task> authenticate)
            where TOptions : class
        {
            var ownerToken = _cts.Token;

            _ready = Task.Run(async () =>
            {
                using var timeoutCancellation = new CancellationTokenSource();
                timeoutCancellation.CancelAfter(timeout);
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    ownerToken, timeoutCancellation.Token);

                try
                {
                    ownerToken.ThrowIfCancellationRequested();
                    var options = createOptions();
                    ownerToken.ThrowIfCancellationRequested();
                    var networkStream = new NetworkStream(_socket, ownsSocket: false);
                    var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
                    if (!PublishAuthenticatedStream(sslStream))
                    {
                        await sslStream.DisposeAsync().ConfigureAwait(false);
                        throw new OperationCanceledException("Transport was aborted during TLS initialization.", ownerToken);
                    }

                    await authenticate(sslStream, linkedCancellation.Token, options).ConfigureAwait(false);
                }
                catch (OperationCanceledException e) when
                    (timeoutCancellation.IsCancellationRequested && !ownerToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"TLS handshake timed out after {timeout}.", e);
                }
            });
        }

        private bool PublishAuthenticatedStream(Stream stream)
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return false;

                _stream = stream;
                return true;
            }
        }

        Task ITransportConnectionLifecycle.Ready => _ready;

        void ITransportConnectionLifecycle.Start() => Start();

        private void Start()
        {
            if (!_ready.IsCompletedSuccessfully)
                throw new InvalidOperationException("Transport cannot start before initialization succeeds.");

            lock (_lifecycleGate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TcpTransportConnection));
                if (_started)
                    return;
                if (_stream is null)
                    throw new InvalidOperationException("The transport stream was not initialized.");

                StartPumps();
            }
        }

        private void StartPumps()
        {
            var stream = _stream ?? throw new InvalidOperationException("The transport stream was not initialized.");
            _started = true;
            ReadCompleted = RunReadPumpAsync(stream, _cts.Token);
            WriteCompleted = RunWritePumpAsync(stream, _cts.Token);
        }

        public PipeReader Input => _inputPipe.Reader;

        /// <inheritdoc/>
        public Task ReadCompleted { get; private set; } = Task.CompletedTask;

        /// <inheritdoc/>
        public Task WriteCompleted { get; private set; } = Task.CompletedTask;

        /// <inheritdoc/>
        public bool HasReadError => Volatile.Read(ref _hasReadError);

        /// <inheritdoc/>
        public Exception? ReadError => Volatile.Read(ref _readError);

        private bool _hasReadError;
        private Exception? _readError;

        public void Write(ReadOnlySequence<byte> data)
        {
            var writer = _outputPipe.Writer;
            foreach (var segment in data)
            {
                writer.Write(segment.Span);
            }
        }

        public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
        {
            return _outputPipe.Writer.FlushAsync(ct);
        }

        public async Task ShutdownAsync()
        {
            var stream = _stream;
            if (!_started)
            {
                Abort();
                return;
            }

            // Complete the output pipe — write pump will drain and exit
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for write pump to finish flushing
            await WriteCompleted.ConfigureAwait(false);

            // Send TLS close_notify before the TCP FIN. SslStream keeps its read side available,
            // so ConfirmedClose continues to receive application data from the peer.
            await ShutdownTlsWriteAsync(stream).ConfigureAwait(false);

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
            var stream = _stream;
            if (!_started)
            {
                Abort();
                return;
            }

            // Complete the output pipe — write pump will drain and exit
            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for write pump to finish flushing
            await WriteCompleted.ConfigureAwait(false);

            Exception? tlsShutdownFailure = null;
            try
            {
                await ShutdownTlsWriteAsync(stream).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                tlsShutdownFailure = e;
            }

            // Cancel to unblock the read pump (which may be blocked on stream.ReadAsync)
            _cts.Cancel();

            // Wait for read pump to exit — it may throw OperationCanceledException (from CTS cancel)
            // or IOException/SocketException (from stream close). Both are expected during shutdown.
            try { await ReadCompleted.ConfigureAwait(false); }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected cancellation or I/O error during shutdown

            // Close the stream and socket
            try
            {
                if (stream is not null)
                    await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _socket.Close();
            }

            if (tlsShutdownFailure is not null)
                ExceptionDispatchInfo.Capture(tlsShutdownFailure).Throw();
        }

        private async Task ShutdownTlsWriteAsync(Stream? stream)
        {
            if (_tlsWriteShutdown || stream is not SslStream sslStream)
                return;

            await sslStream.ShutdownAsync().ConfigureAwait(false);
            _tlsWriteShutdown = true;
        }

        public void Abort()
        {
            Stream? stream;
            bool started;
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                stream = _stream;
                started = _started;
            }

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

            if (!started)
            {
                ObserveInitializationCompletion(_ready);
                _cts.Dispose();
            }

            // Dispose the stream — ObjectDisposedException if already disposed.
            stream?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_started)
            {
                Abort();
                return;
            }

            var stream = _stream;

            lock (_lifecycleGate)
                _disposed = true;

            _cts.Cancel();

            await _outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            await _inputPipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for pump tasks — they may throw OperationCanceledException or I/O errors during shutdown.
            try
            {
                await Task.WhenAll(ReadCompleted, WriteCompleted).ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested) { } // slopwatch-ignore: SW003 expected errors during disposal

            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            _socket.Dispose();
            _cts.Dispose();
        }

        private static void ObserveInitializationCompletion(Task ready)
        {
            _ = ready.ContinueWith(task =>
            {
                if (task.IsFaulted)
                    _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /* ================================================================= */
        /*  Read pump: Stream → Input Pipe                                   */
        /* ================================================================= */

        private async Task RunReadPumpAsync(Stream stream, CancellationToken ct)
        {
            var writer = _inputPipe.Writer;
            Exception? error = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var memory = writer.GetMemory();
                    var bytesRead = await stream.ReadAsync(memory, ct).ConfigureAwait(false);

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

        private async Task RunWritePumpAsync(Stream stream, CancellationToken ct)
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
                            await stream.WriteAsync(segment, ct).ConfigureAwait(false);
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
