// -----------------------------------------------------------------------
// <copyright file="OutboundLaneQueueSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class OutboundLaneQueueSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public OutboundLaneQueueSpec(ITestOutputHelper output) : base(output) { }

    private static IOutboundEnvelope Envelope(int id) => new OutboundEnvelope(id, null, null);
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<IOutboundEnvelope> ReadAsync(OutboundLaneQueue queue)
    {
        while (true)
        {
            var wake = Signal();
            if (queue.TryReadOrWait(() => wake.TrySetResult(true), out var envelope))
                return envelope;
            await wake.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }

    [Fact(DisplayName = "Should_AcceptExactlyCapacity_When_ManyWritersFillAnIdleLane")]
    public async Task Should_AcceptExactlyCapacity_When_ManyWritersFillAnIdleLane()
    {
        const int capacity = 17;
        var queue = new OutboundLaneQueue(capacity);
        for (var round = 0; round < 12; round++)
        {
            var start = Signal();
            var writers = Enumerable.Range(0, 8).Select(writer => Task.Run(async () =>
            {
                await start.Task;
                return Enumerable.Range(writer * 32, 32)
                    .Where(id => queue.TryWrite(Envelope(id))).ToArray();
            })).ToArray();
            start.SetResult(true);
            var accepted = (await Task.WhenAll(writers).WaitAsync(Timeout)).SelectMany(ids => ids).ToArray();
            Assert.Equal(capacity, accepted.Length);
            Assert.Equal(capacity, queue.Count);
            Assert.False(queue.TryWrite(Envelope(-1)));

            var read = new List<int>();
            while (queue.TryRead(out var envelope))
                read.Add((int)envelope.Message);
            Assert.Equal(accepted.OrderBy(id => id), read.OrderBy(id => id));
            Assert.Equal(0, queue.Count);
            Assert.True(queue.IsEmpty);
        }
    }

    [Fact(DisplayName = "Should_ConsumeEveryAcceptedEnvelopeOnceInWriterOrder_When_WritersAndReaderRace")]
    public async Task Should_ConsumeEveryAcceptedEnvelopeOnceInWriterOrder_When_WritersAndReaderRace()
    {
        const int writersCount = 4, perWriter = 256;
        var queue = new OutboundLaneQueue(17);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(Timeout);
        var start = Signal();
        var writers = Enumerable.Range(0, writersCount).Select(writer => Task.Run(async () =>
        {
            await start.Task;
            for (var seq = 0; seq < perWriter; seq++)
            {
                var envelope = Envelope(writer * perWriter + seq);
                while (!queue.TryWrite(envelope))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }
        })).ToArray();
        var received = new List<int>();
        start.SetResult(true);
        try
        {
            for (var i = 0; i < writersCount * perWriter; i++)
                received.Add((int)(await ReadAsync(queue)).Message);
            await Task.WhenAll(writers).WaitAsync(Timeout);
        }
        finally
        {
            deadline.Cancel();
            queue.ReleaseConsumer(null);
        }

        Assert.Equal(Enumerable.Range(0, writersCount * perWriter), received.OrderBy(id => id));
        for (var writer = 0; writer < writersCount; writer++)
            Assert.Equal(Enumerable.Range(writer * perWriter, perWriter),
                received.Where(id => id / perWriter == writer));
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryRead(out _));
    }

    [Fact(DisplayName = "Should_WakeWithoutANudge_When_EmptyAndNonemptyAlternate")]
    public async Task Should_WakeWithoutANudge_When_EmptyAndNonemptyAlternate()
    {
        var queue = new OutboundLaneQueue(1);
        for (var id = 0; id < 384; id++)
        {
            var envelope = Envelope(id);
            var wake = Signal();
            var calls = 0;
            Action onWake = () =>
            {
                Interlocked.Increment(ref calls);
                wake.TrySetResult(true);
            };

            if (id % 3 == 0)
            {
                // Deterministic hand-over: the reader is parked before the only write.
                Assert.False(queue.TryReadOrWait(onWake, out _));
                Assert.True(await Task.Run(() => queue.TryWrite(envelope)));
                await wake.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                Assert.Equal(1, Volatile.Read(ref calls));
                Assert.True(queue.TryRead(out var read));
                Assert.Same(envelope, read);
            }
            else if (id % 3 == 1)
            {
                // The opposite hand-over: the write finishes before the read starts.
                Assert.True(await Task.Run(() => queue.TryWrite(envelope)));
                Assert.True(queue.TryReadOrWait(onWake, out var read));
                Assert.Same(envelope, read);
                Assert.Equal(0, Volatile.Read(ref calls));
            }
            else
            {
                // Also race the empty-read / waiting-flag window; no follow-up write can rescue it.
                var start = Signal();
                var writer = Task.Run(async () => { await start.Task; return queue.TryWrite(envelope); });
                var reader = Task.Run(async () => { await start.Task; return await ReadAsync(queue); });
                start.SetResult(true);
                Assert.True(await writer.WaitAsync(Timeout));
                Assert.Same(envelope, await reader.WaitAsync(Timeout));
            }
            Assert.True(queue.IsEmpty);
            Assert.Equal(0, queue.Count);
        }
        queue.ReleaseConsumer(null);
    }

    [Fact(DisplayName = "Should_AccountForEveryEnvelopeOnce_When_CompletionDrainAndWritersRace")]
    public async Task Should_AccountForEveryEnvelopeOnce_When_CompletionDrainAndWritersRace()
    {
        const int attempts = 256;
        for (var round = 0; round < 24; round++)
        {
            var queue = new OutboundLaneQueue(17);
            var delivered = new int[attempts];
            var dropped = new int[attempts];
            var accepted = new bool[attempts];
            var start = Signal();
            var stopReader = Signal();
            void Drop(IOutboundEnvelope envelope) => Interlocked.Increment(ref dropped[(int)envelope.Message]);

            // Both outcomes are exercised even if completion wins the entire racing burst.
            accepted[0] = queue.TryWrite(Envelope(0));
            Assert.True(queue.TryRead(out var first));
            Interlocked.Increment(ref delivered[(int)first!.Message]);
            accepted[1] = queue.TryWrite(Envelope(1));
            var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(async () =>
            {
                await start.Task;
                for (var id = writer + 2; id < attempts; id += 4)
                {
                    accepted[id] = queue.TryWrite(Envelope(id));
                    if (!accepted[id])
                        Drop(Envelope(id)); // Same ownership rule as the transport's refused-send path.
                    await Task.Yield();
                }
            })).ToArray();
            var reader = Task.Run(async () =>
            {
                await start.Task;
                while (!stopReader.Task.IsCompleted)
                {
                    if (queue.TryRead(out var envelope))
                        Interlocked.Increment(ref delivered[(int)envelope.Message]);
                    else
                        await Task.Yield();
                }
                queue.ReleaseConsumer(null);
            });
            var shutdown = Task.Run(async () =>
            {
                await start.Task;
                queue.Complete();
                queue.DrainToDropped(Drop);
                stopReader.SetResult(true);
            });
            start.SetResult(true);
            await Task.WhenAll(writers.Append(reader).Append(shutdown)).WaitAsync(Timeout);
            queue.ReleaseConsumer(null);
            Assert.True(accepted[0] && accepted[1]);
            for (var id = 0; id < attempts; id++)
            {
                Assert.Equal(1, delivered[id] + dropped[id]);
                if (!accepted[id])
                    Assert.Equal(0, delivered[id]);
            }
            Assert.Equal(0, queue.Count);
            Assert.True(queue.IsEmpty);
            Assert.False(queue.TryWrite(Envelope(-1)));
        }
    }

    // Freeze the ConcurrentQueue tail by taking a snapshot, then hold its segment-growth lock.
    // A real TryWrite reserves its count but cannot publish until the lock is released. Reflection
    // only controls scheduling here: no queue fields or production instructions are changed.
    private static Task<bool> StartPausedWrite(OutboundLaneQueue queue, IOutboundEnvelope envelope, Action whilePaused)
    {
        var items = Private<ConcurrentQueue<IOutboundEnvelope>>(queue, "_items");
        using var snapshot = items.GetEnumerator();
        var growthLock = Private<object>(items, "_crossSegmentLock");
        Task<bool> writer;
        lock (growthLock)
        {
            writer = Task.Factory.StartNew(() => queue.TryWrite(envelope), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(SpinWait.SpinUntil(() => queue.Count == 1, Timeout), "writer did not reserve capacity");
            Assert.False(writer.IsCompleted, "the writer must be paused before publishing its envelope");
            whilePaused();
        }
        return writer;
    }

    private static T Private<T>(object instance, string field) =>
        (T)(instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing scheduling seam {field}")).GetValue(instance)!;

    [Fact(DisplayName = "Should_DropLateAcceptedWriteOnce_When_ShutdownDrainsBeforePublication")]
    public async Task Should_DropLateAcceptedWriteOnce_When_ShutdownDrainsBeforePublication()
    {
        var queue = new OutboundLaneQueue(1);
        var envelope = Envelope(0);
        var dropped = new ConcurrentQueue<IOutboundEnvelope>();
        var writer = StartPausedWrite(queue, envelope, () =>
        {
            queue.Complete();
            queue.DrainToDropped(dropped.Enqueue);
            queue.ReleaseConsumer(null);
            Assert.Empty(dropped);
        });
        Assert.True(await writer.WaitAsync(Timeout));
        Assert.Same(envelope, Assert.Single(dropped));
        Assert.Equal(0, queue.Count);
        queue.ReleaseConsumer(null);
        queue.DrainToDropped(dropped.Enqueue);
        Assert.Same(envelope, Assert.Single(dropped));
        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsEmpty);
    }

    [Fact(DisplayName = "Should_ReadCarryOverBeforeNewTraffic_When_AStageRestartsTwice")]
    public void Should_ReadCarryOverBeforeNewTraffic_When_AStageRestartsTwice()
    {
        var queue = new OutboundLaneQueue(3);
        var envelopes = Enumerable.Range(0, 5).Select(Envelope).ToArray();
        foreach (var envelope in envelopes.Take(3))
            Assert.True(queue.TryWrite(envelope));
        var held = new Queue<IOutboundEnvelope>();
        for (var i = 0; i < 2; i++)
        {
            Assert.True(queue.TryRead(out var envelope));
            held.Enqueue(envelope!);
        }
        foreach (var envelope in envelopes.Skip(3))
            Assert.True(queue.TryWrite(envelope));
        queue.ReleaseConsumer(null);
        Assert.True(queue.TryCarryOver(held));
        Assert.Equal(3, queue.Count);
        Assert.False(queue.TryWrite(Envelope(-1)));

        Assert.True(queue.TryReadOrWait(() => throw new InvalidOperationException("unexpected wake"), out var again));
        Assert.Same(envelopes[0], again);
        queue.ReleaseConsumer(null);
        Assert.True(queue.TryCarryOver(new Queue<IOutboundEnvelope>(new[] { again! })));
        var received = new List<IOutboundEnvelope>();
        while (queue.TryRead(out var envelope))
            received.Add(envelope);
        Assert.Equal(envelopes, received);
        Assert.Equal(0, queue.Count);
        queue.Complete();
        Assert.False(queue.TryCarryOver(new Queue<IOutboundEnvelope>(envelopes)));
    }

    [Fact(DisplayName = "Should_WakeOnlyTheReplacementReader_When_PreviousConsumerWasReleased")]
    public async Task Should_WakeOnlyTheReplacementReader_When_PreviousConsumerWasReleased()
    {
        var queue = new OutboundLaneQueue(1);
        var oldCalls = 0;
        Action oldWake = () => Interlocked.Increment(ref oldCalls);
        Assert.False(queue.TryReadOrWait(oldWake, out _));
        queue.ReleaseConsumer(oldWake);
        // Run the same work item as a delayed pool wake after the old stage stopped.
        Private<IThreadPoolWorkItem>(queue, "_wakeUp").Execute();
        Assert.Equal(0, Volatile.Read(ref oldCalls));
        var wake = Signal();
        Assert.False(queue.TryReadOrWait(() => wake.TrySetResult(true), out _));
        var envelope = Envelope(0);
        Assert.True(queue.TryWrite(envelope));
        await wake.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.True(queue.TryRead(out var read));
        Assert.Same(envelope, read);
        Assert.Equal(0, Volatile.Read(ref oldCalls));

        var completed = Signal();
        Assert.False(queue.TryReadOrWait(() => completed.TrySetResult(true), out _));
        queue.Complete();
        await completed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.False(queue.TryReadOrWait(() => throw new InvalidOperationException("completed reader parked"), out _));
        queue.ReleaseConsumer(null);
    }

    [Fact(DisplayName = "Should_DrainAndComplete_When_SourceAttachesAfterCompletion")]
    public async Task Should_DrainAndComplete_When_SourceAttachesAfterCompletion()
    {
        var queue = new OutboundLaneQueue(2);
        var envelopes = Enumerable.Range(0, 3).Select(Envelope).ToArray();
        Assert.True(queue.TryCarryOver(new Queue<IOutboundEnvelope>(envelopes.Take(1))));
        foreach (var envelope in envelopes.Skip(1))
            Assert.True(queue.TryWrite(envelope));
        queue.Complete();
        var read = await Source.FromGraph(new OutboundLaneSource(queue))
            .RunWith(Sink.Seq<IOutboundEnvelope>(), Sys.Materializer())
            .WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(envelopes, read);
        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsEmpty);
    }

    [Fact(DisplayName = "Should_DeliverEveryAcceptedEnvelopeAndComplete_When_ManyWritersRaceGracefulFlush")]
    public async Task Should_DeliverEveryAcceptedEnvelopeAndComplete_When_ManyWritersRaceGracefulFlush()
    {
        const int attempts = 2048;
        for (var round = 0; round < 24; round++)
        {
            var queue = new OutboundLaneQueue(17);
            var accepted = new bool[attempts];
            var dropped = new int[attempts];
            var start = Signal();
            accepted[0] = queue.TryWrite(Envelope(0));
            var read = Source.FromGraph(new OutboundLaneSource(queue))
                .RunWith(Sink.Seq<IOutboundEnvelope>(), Sys.Materializer());
            var writers = Enumerable.Range(0, 8).Select(writer => Task.Run(async () =>
            {
                await start.Task;
                for (var id = writer + 1; id < attempts; id += 8)
                {
                    accepted[id] = queue.TryWrite(Envelope(id));
                    if (!accepted[id])
                        Interlocked.Increment(ref dropped[id]); // Refused-send ownership.
                    if (id % 32 < 8)
                        await Task.Yield();
                }
            })).ToArray();
            var completion = Task.Run(async () =>
            {
                await start.Task;
                queue.Complete();
            });
            start.SetResult(true);
            await Task.WhenAll(writers.Append(completion)).WaitAsync(Timeout);
            var delivered = await read.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.True(accepted[0]);
            Assert.Equal(Enumerable.Range(0, attempts).Where(id => accepted[id]),
                delivered.Select(envelope => (int)envelope.Message).OrderBy(id => id));
            var deliveries = new int[attempts];
            foreach (var envelope in delivered)
                deliveries[(int)envelope.Message]++;
            for (var id = 0; id < attempts; id++)
                Assert.Equal(1, deliveries[id] + dropped[id]);
            Assert.True(queue.IsCompleted);
            Assert.True(queue.IsEmpty);
            Assert.Equal(0, queue.Count);
        }
    }

    [Fact(DisplayName = "Should_ResumeGracefulFlush_When_CompletionOvertakesAReservedWrite")]
    public async Task Should_ResumeGracefulFlush_When_CompletionOvertakesAReservedWrite()
    {
        var queue = new OutboundLaneQueue(1);
        var delivered = new TaskCompletionSource<IOutboundEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0;
        Action? onWake = null;
        onWake = () =>
        {
            // The same read-or-wait step performed by OutboundLaneSource.OnWake.
            if (queue.TryReadOrWait(onWake!, out var envelope))
                delivered.TrySetResult(envelope);
            Interlocked.Increment(ref polls);
        };
        Assert.False(queue.TryReadOrWait(onWake, out _));
        var envelope = Envelope(0);
        var writer = StartPausedWrite(queue, envelope, () =>
        {
            queue.Complete();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref polls) == 1, Timeout),
                "the completion wake must run while the writer is still paused");
            Assert.False(queue.IsEmpty); // Count includes the unpublished reservation.
        });
        try
        {
            Assert.True(await writer.WaitAsync(Timeout));
            Output?.WriteLine($"After publication: polls={Volatile.Read(ref polls)}, count={queue.Count}, completed={queue.IsCompleted}");
            Assert.Same(envelope, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            var dropped = new List<IOutboundEnvelope>();
            queue.DrainToDropped(dropped.Add);
            queue.ReleaseConsumer(onWake);
            Output?.WriteLine($"Shutdown fallback dropped {dropped.Count} envelope(s); graceful delivery completed={delivered.Task.IsCompleted}");
        }
    }

    [Fact(DisplayName = "Should_PreserveReplacementWaiter_When_PreviousStageStopsLate")]
    public async Task Should_PreserveReplacementWaiter_When_PreviousStageStopsLate()
    {
        var queue = new OutboundLaneQueue(1);
        var oldWake = Signal();
        var replacementWake = Signal();
        Action oldCallback = () => oldWake.TrySetResult(true);
        Assert.False(queue.TryReadOrWait(oldCallback, out _));

        // WatchTermination signals downstream cancellation before it propagates to the source.
        // A replacement can therefore register before the old source executes PostStop.
        Assert.False(queue.TryReadOrWait(() => replacementWake.TrySetResult(true), out _));
        queue.ReleaseConsumer(oldCallback); // The old source's delayed PostStop.
        var envelope = Envelope(0);
        Assert.True(queue.TryWrite(envelope));
        try
        {
            await replacementWake.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.True(queue.TryRead(out var read));
            Assert.Same(envelope, read);
        }
        finally
        {
            Output?.WriteLine($"Delayed old release: replacement woken={replacementWake.Task.IsCompleted}, queued={queue.Count}");
            queue.Complete();
            var dropped = new List<IOutboundEnvelope>();
            queue.DrainToDropped(dropped.Add);
            Output?.WriteLine($"Shutdown fallback dropped {dropped.Count} envelope(s)");
            queue.ReleaseConsumer(null);
        }
    }
}
