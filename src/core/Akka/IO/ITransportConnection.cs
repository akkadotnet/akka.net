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
        /// Initializes the transport before a connection is announced. Implementations that require no
        /// initialization may return a completed task. Repeated calls observe the same initialization operation.
        /// </summary>
        /// <param name="cancellationToken">Cancels the initialization operation when supplied on the call that starts it.</param>
        /// <returns>A task that completes when the transport is ready.</returns>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>Starts the transport's I/O pumps after successful initialization. Repeated calls are idempotent.</summary>
        void Start();

        /// <summary>
        /// Pipe reader for inbound data (data received from the remote peer).
        /// The actor reads from this to get <see cref="Tcp.Received"/> data.
        /// </summary>
        PipeReader Input { get; }

        /// <summary>
        /// Copies <paramref name="data"/> into the output buffer without flushing. Never refuses bytes;
        /// backpressure comes from <see cref="FlushAsync"/>, and only one flush may be pending at a time.
        /// </summary>
        void Write(ReadOnlySequence<byte> data);

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
        /// Aborts the connection immediately without flushing and cancels pending initialization.
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
