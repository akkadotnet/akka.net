//-----------------------------------------------------------------------
// <copyright file="TcpConnectionBatchingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    public class TcpConnectionBatchingSpec : AkkaSpec
    {
        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id)
            {
                Id = id;
            }

            public int Id { get; }
        }

        private sealed class BlockingWriteStream : Stream
        {
            private readonly object _sync = new();
            private readonly List<int> _writeSizes = new();
            private readonly TaskCompletionSource<bool> _firstWriteStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _releaseFirstWrite =
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
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
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

        public TcpConnectionBatchingSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Fact]
        public async Task TcpConnection_should_batch_small_writes_that_arrive_while_a_previous_write_is_in_flight()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings,
                socketPair.Server,
                bindHandler.Ref,
                Array.Empty<Inet.SocketOption>(),
                false,
                stream)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(1)));
            await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(3));

            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(2)));
            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(3)));
            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(4)));

            // With the pipe-based ITransportConnection, acks fire when bytes enter the pipe
            // buffer (memcpy), not when the stream write completes. All acks arrive immediately.
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(2);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(3);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(4);

            stream.ReleaseFirstWrite();

            // The write pump still batches at the stream level: the first write (32 bytes)
            // was already in-flight when writes #2-4 arrived, so those accumulate in the
            // pipe buffer and get flushed as a single 96-byte write to the stream.
            await AwaitAssertAsync(() =>
            {
                stream.WriteSizes.Should().Equal(32, 96);
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(3));

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        private sealed class ConnectedSocketPair : IDisposable
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
}
