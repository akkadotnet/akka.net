//-----------------------------------------------------------------------
// <copyright file="ITransportConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.IO
{
    /// <summary>
    /// Abstraction over a bidirectional transport connection (TCP, TLS, QUIC, test).
    ///
    /// Encapsulates all I/O machinery: pipes, pump loops, buffer management, and flush batching.
    /// The actor writes bytes in, reads bytes out, and never touches streams or sockets directly.
    /// </summary>
    public interface ITransportConnection : IAsyncDisposable
    {
        /// <summary>
        /// Pipe reader for inbound data (data received from the remote peer).
        /// The actor reads from this to get <see cref="Tcp.Received"/> data.
        /// </summary>
        PipeReader Input { get; }

        /// <summary>
        /// Writes data to the transport. Bytes are copied into an internal buffer
        /// and will be flushed to the underlying stream by the write pump.
        /// Returns when bytes are accepted into the buffer (not when sent on the wire).
        /// Goes async when backpressure is active (buffer full).
        /// </summary>
        ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

        /// <summary>
        /// Writes a multi-segment sequence to the transport. Each segment is copied
        /// into the internal buffer. This avoids per-segment syscalls.
        /// </summary>
        ValueTask<FlushResult> WriteAsync(ReadOnlySequence<byte> data, CancellationToken ct = default);

        /// <summary>
        /// Explicitly flushes any buffered data to the write pump.
        /// Useful for low-throughput scenarios where writes don't fill the buffer.
        /// Under high throughput, the buffer auto-flushes at the pause threshold.
        /// </summary>
        ValueTask<FlushResult> FlushAsync(CancellationToken ct = default);

        /// <summary>
        /// Half-close: flushes remaining writes, sends FIN, keeps reading.
        /// </summary>
        Task ShutdownAsync();

        /// <summary>
        /// Full close: flushes remaining writes, closes the connection.
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Abort: RST the connection immediately, no flush.
        /// </summary>
        void Abort();

        /// <summary>
        /// Completes when the read pump finishes. Check <see cref="Task.IsFaulted"/>
        /// to determine whether the read ended due to an I/O error (vs. normal EOF).
        /// The input <see cref="PipeWriter"/> is always completed WITHOUT passing the exception,
        /// so buffered data is preserved and the actor can drain it before checking this task.
        /// </summary>
        Task ReadCompleted { get; }

        /// <summary>
        /// Completes when the write pump finishes (all buffered data flushed to the stream).
        /// </summary>
        Task WriteCompleted { get; }

        /// <summary>
        /// Returns true if the read pump encountered an I/O error.
        /// This is set BEFORE the input pipe writer is completed, so the actor can
        /// check it synchronously when handling a completed pipe read to distinguish
        /// error-EOF from normal EOF, even before the <see cref="ReadCompleted"/> task
        /// has been observed.
        /// </summary>
        bool HasReadError { get; }

        /// <summary>
        /// The exception that caused the read pump to fail, or null if no error.
        /// Set at the same time as <see cref="HasReadError"/>.
        /// </summary>
        Exception? ReadError { get; }
    }
}
