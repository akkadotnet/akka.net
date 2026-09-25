//-----------------------------------------------------------------------
// <copyright file="TcpConnectionWriteBackpressureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
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

        private Task<IActorRef> ConnectAsync(ConnectedSocketPair pair, BlockingWriteStream stream, TestProbe handler,
            bool keepOpenOnPeerClosed = false, Action<IActorRef>? beforeRegister = null)
        {
            var settings = TcpSettings.Create(Sys);
            return RegisterAsync(bindHandler => Props.Create(() => new TcpIncomingConnection(
                settings, pair.Server, bindHandler, Array.Empty<Inet.SocketOption>(), false, stream)),
                new Tcp.Register(handler.Ref, keepOpenOnPeerClosed), beforeRegister);
        }

        private Task<IActorRef> ConnectAsync(ConnectedSocketPair pair, FakeTransport transport, TestProbe handler)
        {
            var settings = TcpSettings.Create(Sys);
            return RegisterAsync(bindHandler => Props.Create(() => new FakeTransportConnection(
                settings, pair.Server, bindHandler, transport)), new Tcp.Register(handler.Ref), null);
        }

        private async Task<IActorRef> RegisterAsync(Func<IActorRef, Props> props, Tcp.Register register,
            Action<IActorRef>? beforeRegister)
        {
            var bindHandler = CreateTestProbe();
            var connection = Sys.ActorOf(props(bindHandler.Ref));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            beforeRegister?.Invoke(connection);
            bindHandler.Send(connection, register);
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
        }

        [Fact(DisplayName = "Should_fail_write_without_ack_When_flush_reports_the_output_completed")]
        public async Task Should_fail_write_without_ack_When_flush_reports_the_output_completed()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            var handler = CreateTestProbe();
            var transport = new FakeTransport(_ => new ValueTask<FlushResult>(new FlushResult(false, isCompleted: true)));
            var connection = await ConnectAsync(pair, transport, handler);

            handler.Send(connection, Write(16, 1));

            await ExpectFailedWritesAsync(handler, 1, 1);
            await handler.ExpectMsgAsync<Tcp.ErrorClosed>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_send_ErrorClosed_to_Close_sender_When_a_queued_write_fails_while_closing")]
        public async Task Should_send_ErrorClosed_to_Close_sender_When_a_queued_write_fails_while_closing()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            var handler = CreateTestProbe();
            var closer = CreateTestProbe();
            var firstFlush = new TaskCompletionSource<FlushResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Write 1's flush stays pending; the write pump "dies" before write 2 reaches the pipe.
            var transport = new FakeTransport(n => n == 1
                ? new ValueTask<FlushResult>(firstFlush.Task)
                : throw new IOException("write pump failed"));
            var connection = await ConnectAsync(pair, transport, handler);

            handler.Send(connection, Write(16, 1));
            handler.Send(connection, Write(16, 2));
            closer.Send(connection, Tcp.Close.Instance);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            firstFlush.SetResult(new FlushResult(false, false));
            await ExpectAcksAsync(handler, 1, 1);
            await ExpectFailedWritesAsync(handler, 2, 2);
            await closer.ExpectMsgAsync<Tcp.ErrorClosed>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_fail_pending_and_queued_writes_When_write_pump_fails_while_open")]
        public async Task Should_fail_pending_and_queued_writes_When_write_pump_fails_while_open()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            await handler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            stream.FailFirstWrite(new IOException("connection reset"));

            await ExpectFailedWritesAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.ErrorClosed>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_send_ErrorClosed_to_Close_sender_When_write_pump_fails_while_closing")]
        public async Task Should_send_ErrorClosed_to_Close_sender_When_write_pump_fails_while_closing()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var closer = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler);

            StallWithQueuedWrites(connection, handler);
            closer.Send(connection, Tcp.Close.Instance);
            await closer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            stream.FailFirstWrite(new IOException("connection reset"));

            await ExpectFailedWritesAsync(handler, 1, 3);
            await closer.ExpectMsgAsync<Tcp.ErrorClosed>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact(DisplayName = "Should_ack_every_write_before_ConfirmedClosed_When_peer_closed_first_with_keepOpenOnPeerClosed")]
        public async Task Should_ack_every_write_before_ConfirmedClosed_When_peer_closed_first_with_keepOpenOnPeerClosed()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler, keepOpenOnPeerClosed: true);

            StallWithQueuedWrites(connection, handler);
            stream.CompleteReads(); // peer FIN
            await handler.ExpectMsgAsync<Tcp.PeerClosed>();
            handler.Send(connection, Tcp.ConfirmedClose.Instance);
            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await handler.ExpectMsgAsync<Tcp.ConfirmedClosed>();
            await ExpectTerminatedAsync(connection);
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
        }

        [Fact(DisplayName = "Should_withhold_WriteAck_When_writes_buffered_before_Register_pass_the_pause_threshold")]
        public async Task Should_withhold_WriteAck_When_writes_buffered_before_Register_pass_the_pause_threshold()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var handler = CreateTestProbe();
            var connection = await ConnectAsync(pair, stream, handler,
                beforeRegister: c => StallWithQueuedWrites(c, handler));

            await handler.ExpectNoMsgAsync(NoMsgWindow);

            stream.ReleaseFirstWrite();
            await ExpectAcksAsync(handler, 1, 3);
            await AbortAsync(connection, handler);
            stream.BytesWritten.Should().Be(PauseThreshold + QueuedBytes);
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

        private sealed class FakeTransportConnection : TcpConnection
        {
            private readonly IActorRef _bindHandler;
            private readonly FakeTransport _transport;

            public FakeTransportConnection(TcpSettings settings, Socket socket, IActorRef bindHandler, FakeTransport transport)
                : base(settings, socket, false)
            {
                _bindHandler = bindHandler;
                _transport = transport;
            }

            protected override void PreStart() => CompleteConnect(_bindHandler, Array.Empty<Inet.SocketOption>());

            protected override ITransportConnection CreateTransport() => _transport;
        }

        /// <summary>A transport whose Nth write returns (or throws) whatever the test decides.</summary>
        private sealed class FakeTransport : ITransportConnection
        {
            private readonly Func<int, ValueTask<FlushResult>> _onWrite;
            private readonly Pipe _input = new();
            private readonly TaskCompletionSource<bool> _never = new();
            private int _writes;

            public FakeTransport(Func<int, ValueTask<FlushResult>> onWrite) => _onWrite = onWrite;

            public PipeReader Input => _input.Reader;
            public Task ReadCompleted => _never.Task;
            public Task WriteCompleted => _never.Task;
            public bool HasReadError => false;
            public Exception? ReadError => null;

            public ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => _onWrite(++_writes);
            public ValueTask<FlushResult> WriteAsync(ReadOnlySequence<byte> data, CancellationToken ct = default) => _onWrite(++_writes);
            public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default) => default;
            public Task ShutdownAsync() => Task.CompletedTask;
            public Task CloseAsync() => Task.CompletedTask;
            public void Abort() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
