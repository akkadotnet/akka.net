// -----------------------------------------------------------------------
// <copyright file="ArteryQuicReadStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://dotnetfoundation.org/>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Akka;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryQuicReadStageSpec : AkkaSpec
    {
        public ArteryQuicReadStageSpec(ITestOutputHelper output) : base(output) { }

        [Fact(DisplayName = "Immediate QUIC reads consume each ValueTask once and retain independent chunks through EOF")]
        public async Task Should_RetainChunks_When_ReadsCompleteImmediately()
        {
            using var stream = new ImmediateStream(new byte[] { 13, 14 }, new byte[] { 42 });
            var eof = 0;
            var counters = new ArteryQuicIoCounters();
            var chunks = await Source.FromGraph(new ArteryQuicReadStage(stream, CancellationToken.None,
                    counters, () => eof++))
                .RunWith(Sink.Seq<ReadOnlySequence<byte>>(), Sys.Materializer());
            Assert.Equal(2, chunks.Count);
            Assert.Equal(new byte[] { 13, 14 }, chunks[0].ToArray());
            Assert.Equal(new byte[] { 42 }, chunks[1].ToArray());
            Assert.Equal(1, eof);
            Assert.Equal(2, counters.Snapshot.ReadOperations);
            Assert.Equal(3, counters.Snapshot.ReadBytes);
            Assert.Equal(3, stream.Reads.Count);
            Assert.All(stream.Reads, read => Assert.Equal(1, read.GetResultCount));
        }

        [Fact(DisplayName = "Deferred QUIC reads preserve demand, consume completion once and keep published bytes after EOF")]
        public async Task Should_PreserveDemandAndStorage_When_ReadsAreDeferred()
        {
            using var stream = new DeferredStream();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var eof = 0;
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            Source.FromGraph(new ArteryQuicReadStage(stream, deadline.Token, null, () => eof++))
                .RunWith(Sink.FromSubscriber(subscriber), Sys.Materializer());
            var subscription = await subscriber.ExpectSubscriptionAsync(deadline.Token);
            subscription.Request(1);
            var first = await stream.NextAsync(deadline.Token);
            Assert.Equal(deadline.Token, first.Token);
            Assert.False(stream.HasQueuedRead);
            first.Buffer.Span[0] = 13;
            first.Complete(1);
            var retained = await subscriber.ExpectNextAsync(deadline.Token);
            Assert.False(stream.HasQueuedRead);

            subscription.Request(1);
            var second = await stream.NextAsync(deadline.Token);
            second.Buffer.Span[0] = 42;
            second.Complete(1);
            Assert.Equal(new byte[] { 42 }, (await subscriber.ExpectNextAsync(deadline.Token)).ToArray());
            subscription.Request(1);
            var final = await stream.NextAsync(deadline.Token);
            final.Complete(0);
            await subscriber.ExpectCompleteAsync(deadline.Token);
            Assert.Equal(new byte[] { 13 }, retained.ToArray());
            Assert.Equal(1, eof);
            Assert.Equal(1, first.GetResultCount);
            Assert.Equal(1, second.GetResultCount);
            Assert.Equal(1, final.GetResultCount);
            Assert.False(stream.Disposed);
        }

        [Theory(DisplayName = "QUIC read invocation and completed ValueTask failures fail the graph without reporting EOF")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_FailWithoutEof_When_ReadFailsSynchronously(bool invocationThrows)
        {
            using var stream = new FailureStream(invocationThrows);
            var eof = 0;
            var completion = Source.FromGraph(new ArteryQuicReadStage(stream, CancellationToken.None,
                    null, () => eof++))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), Sys.Materializer());
            await Assert.ThrowsAsync<IOException>(async () => await completion);
            Assert.Equal(0, eof);
        }

        [Theory(DisplayName = "Deferred QUIC faults and cancellation fail the graph instead of confirming natural EOF")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_FailWithoutEof_When_DeferredReadFails(bool canceled)
        {
            using var stream = new DeferredStream();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var lifetime = new CancellationTokenSource();
            var eof = 0;
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            Source.FromGraph(new ArteryQuicReadStage(stream, lifetime.Token, null, () => eof++))
                .RunWith(Sink.FromSubscriber(subscriber), Sys.Materializer());
            (await subscriber.ExpectSubscriptionAsync(deadline.Token)).Request(1);
            var read = await stream.NextAsync(deadline.Token);
            if (canceled)
                lifetime.Cancel();
            read.Fail(canceled ? new OperationCanceledException(lifetime.Token) : new IOException("read failed"));
            var error = await subscriber.ExpectErrorAsync(deadline.Token);
            Assert.IsAssignableFrom(canceled ? typeof(OperationCanceledException) : typeof(IOException), error);
            Assert.Equal(1, read.GetResultCount);
            Assert.Equal(0, eof);
        }

        [Theory(DisplayName = "A stopped QUIC source still consumes late native success or failure without owning stream disposal")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_ConsumeLateCompletion_When_DownstreamCancels(bool failed)
        {
            using var stream = new DeferredStream();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var eof = 0;
            var subscriber = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var terminated = Source.FromGraph(new ArteryQuicReadStage(stream, deadline.Token, null, () => eof++))
                .WatchTermination((_, done) => done)
                .To(Sink.FromSubscriber(subscriber)).Run(Sys.Materializer());
            var subscription = await subscriber.ExpectSubscriptionAsync(deadline.Token);
            subscription.Request(1);
            var read = await stream.NextAsync(deadline.Token);
            subscription.Cancel();
            await terminated.WaitAsync(deadline.Token);
            Assert.Equal(0, read.GetResultCount);
            Assert.False(stream.Disposed);
            read.Buffer.Span[0] = 87;
            if (failed)
                read.Fail(new IOException("late native failure"));
            else
                read.Complete(0);
            await read.Consumed.WaitAsync(deadline.Token);
            await read.ContinuationReturned.WaitAsync(deadline.Token);
            Assert.Equal(1, read.GetResultCount);
            Assert.Equal(0, eof);
            Assert.False(stream.HasQueuedRead);
            Assert.False(stream.Disposed);
        }

        private sealed class PendingRead : IValueTaskSource<int>
        {
            private ManualResetValueTaskSourceCore<int> _source = new() { RunContinuationsAsynchronously = true };
            private readonly TaskCompletionSource<Done> _consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<Done> _continuationReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _getResultCount;

            internal PendingRead(Memory<byte> buffer, CancellationToken token)
            {
                Buffer = buffer;
                Token = token;
            }

            internal Memory<byte> Buffer { get; }
            internal CancellationToken Token { get; }
            internal int GetResultCount => Volatile.Read(ref _getResultCount);
            internal Task Consumed => _consumed.Task;
            internal Task ContinuationReturned => _continuationReturned.Task;
            internal ValueTask<int> Operation => new(this, _source.Version);
            internal void Complete(int count) => _source.SetResult(count);
            internal void Fail(Exception error) => _source.SetException(error);

            public int GetResult(short token)
            {
                try
                {
                    return _source.GetResult(token);
                }
                finally
                {
                    Interlocked.Increment(ref _getResultCount);
                    _consumed.TrySetResult(Done.Instance);
                }
            }

            public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
            public void OnCompleted(Action<object?> continuation, object? state, short token,
                ValueTaskSourceOnCompletedFlags flags) => _source.OnCompleted(static state =>
                {
                    var invocation = ((PendingRead Read, Action<object?> Continue, object? State))state!;
                    try
                    {
                        invocation.Continue(invocation.State);
                    }
                    finally
                    {
                        invocation.Read._continuationReturned.TrySetResult(Done.Instance);
                    }
                }, (this, continuation, state), token, flags);
        }

        private sealed class ImmediateStream(params byte[][] chunks) : MemoryStream
        {
            internal List<PendingRead> Reads { get; } = new();

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var read = new PendingRead(buffer, cancellationToken);
                var chunk = Reads.Count < chunks.Length ? chunks[Reads.Count] : Array.Empty<byte>();
                chunk.CopyTo(buffer.Span);
                Reads.Add(read);
                read.Complete(chunk.Length);
                return read.Operation;
            }
        }

        // Deliberately ignores cancellation, like a native operation that has not settled yet.
        private sealed class DeferredStream : MemoryStream
        {
            private readonly Channel<PendingRead> _reads = Channel.CreateUnbounded<PendingRead>();
            private PendingRead? _last;
            internal bool Disposed { get; private set; }
            internal bool HasQueuedRead => _reads.Reader.TryPeek(out _);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_last is { GetResultCount: 0 })
                    throw new InvalidOperationException("Overlapping read or unconsumed completion.");
                _last = new PendingRead(buffer, cancellationToken);
                if (!_reads.Writer.TryWrite(_last))
                    throw new InvalidOperationException("Cannot record the read request.");
                return _last.Operation;
            }

            internal ValueTask<PendingRead> NextAsync(CancellationToken token) => _reads.Reader.ReadAsync(token);

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class FailureStream(bool invocationThrows) : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (invocationThrows)
                    throw new IOException("read invocation failed");
                return ValueTask.FromException<int>(new IOException("completed read failed"));
            }
        }
    }
}
