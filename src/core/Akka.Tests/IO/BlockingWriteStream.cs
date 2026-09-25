//-----------------------------------------------------------------------
// <copyright file="BlockingWriteStream.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Akka.Tests.IO
{
    /// <summary>
    /// Test stream for a <c>TcpIncomingConnection</c>: the first write blocks until
    /// <see cref="ReleaseFirstWrite"/>, which stalls the transport's write pump. Reads block
    /// until <see cref="CompleteReads"/> signals EOF.
    /// </summary>
    internal sealed class BlockingWriteStream : Stream
    {
        private readonly object _sync = new();
        private readonly List<int> _writeSizes = new();
        private readonly TaskCompletionSource<bool> _firstWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _readsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _writeCount;

        public Task FirstWriteStarted => _firstWriteStarted.Task;

        public IReadOnlyList<int> WriteSizes
        {
            get
            {
                lock (_sync)
                {
                    return _writeSizes.ToArray();
                }
            }
        }

        public long BytesWritten => WriteSizes.Sum(x => (long)x);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseFirstWrite()
        {
            _releaseFirstWrite.TrySetResult(true);
        }

        /// <summary>Makes every pending and future read return 0 (peer EOF).</summary>
        public void CompleteReads()
        {
            _readsCompleted.TrySetResult(true);
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _readsCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var writeIndex = Interlocked.Increment(ref _writeCount);
            lock (_sync)
            {
                _writeSizes.Add(buffer.Length);
            }

            if (writeIndex == 1)
            {
                _firstWriteStarted.TrySetResult(true);
                await _releaseFirstWrite.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal sealed class ConnectedSocketPair : IDisposable
    {
        private ConnectedSocketPair(Socket client, Socket server)
        {
            Client = client;
            Server = server;
        }

        public Socket Client { get; }
        public Socket Server { get; }

        public static async Task<ConnectedSocketPair> CreateAsync()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);

            var endpoint = (IPEndPoint)listener.LocalEndPoint!;
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                var connectTask = client.ConnectAsync(endpoint);
                var server = await listener.AcceptAsync();
                await connectTask;
                return new ConnectedSocketPair(client, server);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
