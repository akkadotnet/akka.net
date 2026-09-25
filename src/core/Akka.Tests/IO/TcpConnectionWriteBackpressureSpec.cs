//-----------------------------------------------------------------------
// <copyright file="TcpConnectionWriteBackpressureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

#nullable enable

namespace Akka.Tests.IO
{
    /// <summary>
    /// #8617: once the output pipe passes its pause threshold, a WriteAck waits for the pipe's flush
    /// and later writes queue behind it. <see cref="BlockingWriteStream"/> stalls the write pump.
    /// </summary>
    public class TcpConnectionWriteBackpressureSpec : AkkaSpec
    {
        // PipeOptions.Default pause threshold, used by the output pipe when PipeBufferSize isn't set.
        private const int PauseThreshold = 64 * 1024;
        private const int QueuedBytes = 32;
        private static readonly TimeSpan NoMsgWindow = TimeSpan.FromMilliseconds(300);

        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id) => Id = id;
            public int Id { get; }
            public override string ToString() => $"WriteAck({Id})";
        }

        private sealed class CountingOwner : IMemoryOwner<byte>
        {
            private int _disposeCount;
            public CountingOwner(int length) => Memory = new byte[length];
            public Memory<byte> Memory { get; }
            public int DisposeCount => Volatile.Read(ref _disposeCount);
            public void Dispose() => Interlocked.Increment(ref _disposeCount);
        }

        public TcpConnectionWriteBackpressureSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        private static Tcp.Write Write(int size, int id) => Tcp.Write.Create(new byte[size], new WriteAck(id));

        private async Task<IActorRef> ConnectAsync(ConnectedSocketPair pair, BlockingWriteStream stream, TestProbe handler)
        {
            var bindHandler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, pair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false, stream)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));
            await WatchAsync(connection);
            return connection;
        }

        /// <summary>
        /// Write 1 fills the pipe to its pause threshold, so its flush stays pending while the pump
        /// is stalled; writes 2 and 3 queue behind it.
        /// </summary>
        private static void StallWithQueuedWrites(IActorRef connection, TestProbe writer)
        {
            writer.Send(connection, Write(PauseThreshold, 1));
            writer.Send(connection, Write(QueuedBytes / 2, 2));
            writer.Send(connection, Write(QueuedBytes / 2, 3));
        }

        private static async Task ExpectAcksAsync(TestProbe probe, int from, int to)
        {
            for (var id = from; id <= to; id++)
                (await probe.ExpectMsgAsync<WriteAck>()).Id.Should().Be(id);
        }

        private static async Task ExpectFailedWritesAsync(TestProbe probe, int from, int to)
        {
            for (var id = from; id <= to; id++)
            {
                var failed = await probe.ExpectMsgAsync<Tcp.CommandFailed>();
                ((WriteAck)((Tcp.Write)failed.Cmd).Ack).Id.Should().Be(id);
            }
        }

        private async Task AbortAsync(IActorRef connection, TestProbe handler)
        {
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_withhold_WriteAck_When_output_pipe_reaches_its_pause_threshold")]
        public async Task Should_withhold_WriteAck_When_output_pipe_reaches_its_pause_threshold()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            for (var id = 1; id <= 8; id++)
                handler.Send(connection, Write(16 * 1024, id));

            // 3 x 16 KB stay under the 64 KB pause threshold; the 4th write reaches it.
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 4, 8);

            await AbortAsync(connection, handler);
        }

        [Fact(DisplayName = "Should_ack_in_order_across_senders_and_CompoundWrite_parts_When_writes_queue")]
        public async Task Should_ack_in_order_across_senders_and_CompoundWrite_parts_When_writes_queue()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var other = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            handler.Send(connection, new Tcp.CompoundWrite(Write(PauseThreshold, 1),
                new Tcp.CompoundWrite(Write(16, 2), Write(16, 3))));
            other.Send(connection, Write(16, 100));
            handler.Send(connection, Write(16, 4));

            await handler.ExpectNoMsgAsync(NoMsgWindow);
            await other.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(50));

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 4);
            (await other.ExpectMsgAsync<WriteAck>()).Id.Should().Be(100);

            await AbortAsync(connection, handler);
            stream.BytesWritten.Should().Be(PauseThreshold + 64);
        }

        [Fact(DisplayName = "Should_fail_write_without_ack_When_flush_reports_the_output_completed")]
        public async Task Should_fail_write_without_ack_When_flush_reports_the_output_completed()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var connection = Sys.ActorOf(Props.Create(() =>
                new CompletedOutputConnection(settings, pair.Server, bindHandler.Ref)));
            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));
            await WatchAsync(connection);

            handler.Send(connection, Write(16, 1));

            await ExpectFailedWritesAsync(handler, 1, 1);
            await handler.ExpectMsgAsync<Tcp.ErrorClosed>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_ack_every_write_before_Closed_When_Close_arrives_with_writes_pending")]
        public async Task Should_ack_every_write_before_Closed_When_Close_arrives_with_writes_pending()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            handler.Send(connection, Tcp.Close.Instance);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.Closed>();
            await ExpectTerminatedAsync(connection);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
        }

        [Fact(DisplayName = "Should_ack_every_write_before_ConfirmedClosed_When_ConfirmedClose_arrives_with_writes_pending")]
        public async Task Should_ack_every_write_before_ConfirmedClosed_When_ConfirmedClose_arrives_with_writes_pending()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            handler.Send(connection, Tcp.ConfirmedClose.Instance);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            stream.CompleteReads(); // peer FIN
            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>();
            await ExpectTerminatedAsync(connection);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
        }

        [Fact(DisplayName = "Should_drain_queued_writes_before_Closed_When_Close_upgrades_a_pending_ConfirmedClose")]
        public async Task Should_drain_queued_writes_before_Closed_When_Close_upgrades_a_pending_ConfirmedClose()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            handler.Send(connection, Tcp.ConfirmedClose.Instance);
            handler.Send(connection, Tcp.Close.Instance);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.Closed>();
            await ExpectTerminatedAsync(connection);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
        }

        [Fact(DisplayName = "Should_ack_every_write_before_PeerClosed_When_peer_closes_with_writes_pending")]
        public async Task Should_ack_every_write_before_PeerClosed_When_peer_closes_with_writes_pending()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            stream.CompleteReads(); // peer FIN
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.PeerClosed>();
            await ExpectTerminatedAsync(connection);
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
        }

        [Fact(DisplayName = "Should_fail_pending_and_queued_writes_When_Abort_arrives_with_writes_pending")]
        public async Task Should_fail_pending_and_queued_writes_When_Abort_arrives_with_writes_pending()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            handler.Send(connection, Tcp.Abort.Instance);

            await ExpectFailedWritesAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact(DisplayName = "Should_fail_pending_and_queued_writes_When_handler_dies_with_writes_pending")]
        public async Task Should_fail_pending_and_queued_writes_When_handler_dies_with_writes_pending()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var writer = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, writer);
            await writer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            Sys.Stop(handler.Ref);

            await ExpectFailedWritesAsync(writer, 1, 3);
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_reply_WritingResumed_When_idle_or_once_pending_writes_drain")]
        public async Task Should_reply_WritingResumed_When_idle_or_once_pending_writes_drain()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            handler.Send(connection, Tcp.ResumeWriting.Instance);
            await handler.ExpectMsgAsync<Tcp.WritingResumed>();

            StallWithQueuedWrites(connection, handler);
            handler.Send(connection, Tcp.ResumeWriting.Instance);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.WritingResumed>();

            await AbortAsync(connection, handler);
        }

        [Fact(DisplayName = "Should_dispose_owned_segments_once_When_writes_wait_behind_a_pending_flush")]
        public async Task Should_dispose_owned_segments_once_When_writes_wait_behind_a_pending_flush()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            var pending = new CountingOwner(PauseThreshold);
            var queued = new CountingOwner(16);
            handler.Send(connection, Tcp.Write.Create(OwnedSequenceSegment.Create(pending, PauseThreshold), new WriteAck(1)));
            handler.Send(connection, Tcp.Write.Create(OwnedSequenceSegment.Create(queued, 16), new WriteAck(2)));

            // The pending write was copied into the pipe, so it is freed although its ack waits;
            // the queued write keeps its buffer until it reaches the pipe.
            await AwaitAssertAsync(() => pending.DisposeCount.Should().Be(1));
            await handler.ExpectNoMsgAsync(NoMsgWindow);
            queued.DisposeCount.Should().Be(0);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 2);
            queued.DisposeCount.Should().Be(1);
            pending.DisposeCount.Should().Be(1);

            await AbortAsync(connection, handler);
        }

        [Fact(DisplayName = "Should_dispose_queued_owned_segments_once_When_Abort_drops_them")]
        public async Task Should_dispose_queued_owned_segments_once_When_Abort_drops_them()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            var queued = new CountingOwner(16);
            handler.Send(connection, Write(PauseThreshold, 1));
            handler.Send(connection, Tcp.Write.Create(OwnedSequenceSegment.Create(queued, 16), new WriteAck(2)));
            handler.Send(connection, Tcp.Abort.Instance);

            await ExpectFailedWritesAsync(handler, 1, 2);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
            queued.DisposeCount.Should().Be(1);
        }

        /// <summary>
        /// A connection whose transport reports the output as completed on every flush, as the pipe
        /// does once its write pump has exited.
        /// </summary>
        private sealed class CompletedOutputConnection : TcpConnection
        {
            private readonly IActorRef _bindHandler;

            public CompletedOutputConnection(TcpSettings settings, Socket socket, IActorRef bindHandler)
                : base(settings, socket, false)
            {
                _bindHandler = bindHandler;
            }

            protected override void PreStart() => CompleteConnect(_bindHandler, Array.Empty<Inet.SocketOption>());

            protected override ITransportConnection CreateTransport() => new CompletedOutputTransport();
        }

        private sealed class CompletedOutputTransport : ITransportConnection
        {
            private static readonly ValueTask<FlushResult> Completed =
                new(new FlushResult(isCanceled: false, isCompleted: true));

            private readonly Pipe _input = new();
            private readonly TaskCompletionSource<bool> _never = new();

            public PipeReader Input => _input.Reader;
            public Task ReadCompleted => _never.Task;
            public Task WriteCompleted => _never.Task;
            public bool HasReadError => false;
            public Exception? ReadError => null;

            public ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Completed;
            public ValueTask<FlushResult> WriteAsync(ReadOnlySequence<byte> data, CancellationToken ct = default) => Completed;
            public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default) => Completed;
            public Task ShutdownAsync() => Task.CompletedTask;
            public Task CloseAsync() => Task.CompletedTask;
            public void Abort() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
