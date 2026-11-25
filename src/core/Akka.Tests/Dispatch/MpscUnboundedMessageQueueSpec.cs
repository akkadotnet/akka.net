//-----------------------------------------------------------------------
// <copyright file="MpscUnboundedMessageQueueSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Dispatch
{
    /// <summary>
    /// Drives the default mailbox's queue (<see cref="MpscUnboundedMessageQueue{TIndex}"/>, in both cache-line
    /// layouts) the way a mailbox does: many producers enqueue and then try to
    /// set a Scheduled flag; whoever sets it schedules a run on two dedicated threads that strictly alternate,
    /// so the single consumer changes threads after every run. A run dequeues up to a throughput budget, clears
    /// the flag and reschedules itself if HasMessages is still true. A lost message, a duplicate, a reordering within one producer, two runs at once, or a lost
    /// wake-up (HasMessages false while a completed enqueue is in the queue) fails the test.
    /// </summary>
    public class MpscUnboundedMessageQueueSpec
    {
        private sealed record Msg(int Producer, int Seq);

        private static IMessageQueue NewQueue(bool lines128) => lines128
            ? new MpscUnboundedMessageQueue<PaddedIndex128>()
            : new MpscUnboundedMessageQueue<PaddedIndex64>();

        private sealed class MailboxModel : IDisposable
        {
            private readonly IMessageQueue _queue;
            private readonly int[] _next;
            private readonly long _total;
            private readonly int _maxThroughput;
            private readonly ConcurrentDictionary<int, bool> _consumerThreads = new();
            private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _consumerGate = new();
            private readonly Thread[] _consumers;
            private bool _runScheduled;
            private bool _running;
            private bool _stopping;
            private int _nextConsumer;
            private int _status; // 0 = idle, 1 = scheduled
            private int _inRun;
            private long _received;
            private long _runs;
            private volatile string? _failure;

            public MailboxModel(IMessageQueue queue, int producers, long total, int maxThroughput)
            {
                _queue = queue;
                _next = new int[producers];
                _total = total;
                _maxThroughput = maxThroughput;
                _consumers = new Thread[2];
                for (var i = 0; i < _consumers.Length; i++)
                {
                    var consumer = i;
                    _consumers[i] = new Thread(() => Consume(consumer)) { IsBackground = true };
                    _consumers[i].Start();
                }
            }

            public IMessageQueue Queue => _queue;
            public long Received => Volatile.Read(ref _received);
            public long Runs => Volatile.Read(ref _runs);
            public int ConsumerThreads => _consumerThreads.Count;
            public Task Done => _done.Task;
            public string? Failure => _failure;

            public void Send(int producer, int seq)
            {
                _queue.Enqueue(ActorRefs.Nobody, new Envelope(new Msg(producer, seq), ActorRefs.NoSender));
                if (Volatile.Read(ref _status) == 0 && Interlocked.CompareExchange(ref _status, 1, 0) == 0)
                    Schedule();
            }

            private void Schedule()
            {
                lock (_consumerGate)
                {
                    _runScheduled = true;
                    Monitor.PulseAll(_consumerGate);
                }
            }

            private void Consume(int consumer)
            {
                while (true)
                {
                    lock (_consumerGate)
                    {
                        while (!_stopping && (!_runScheduled || _nextConsumer != consumer))
                            Monitor.Wait(_consumerGate);
                        if (_stopping) return;
                        _runScheduled = false;
                        _running = true;
                    }

                    Run();

                    lock (_consumerGate)
                    {
                        // Transfer the turn only after Run has returned, including its idle/reschedule edge.
                        // A producer may already have scheduled another run, but it cannot start early.
                        _running = false;
                        _nextConsumer = 1 - consumer;
                        Monitor.PulseAll(_consumerGate);
                    }
                }
            }

            public bool WaitForIdle(TimeSpan timeout)
            {
                var sw = Stopwatch.StartNew();
                lock (_consumerGate)
                {
                    while (_runScheduled || _running)
                    {
                        var remaining = timeout - sw.Elapsed;
                        if (remaining <= TimeSpan.Zero || !Monitor.Wait(_consumerGate, remaining)) return false;
                    }
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_consumerGate)
                {
                    _stopping = true;
                    Monitor.PulseAll(_consumerGate);
                }
                foreach (var consumer in _consumers) consumer.Join();
            }

            private void Fail(string why)
            {
                _failure ??= why;
                _done.TrySetResult(false);
            }

            private void Run()
            {
                try
                {
                    RunCore();
                    if (Received == _total) _done.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Fail(ex.ToString());
                }
            }

            private void RunCore()
            {
                if (Interlocked.Exchange(ref _inRun, 1) != 0) Fail("two consumers ran at once");
                Interlocked.Increment(ref _runs);
                _consumerThreads.TryAdd(Environment.CurrentManagedThreadId, true);
                var budget = 1 + Random.Shared.Next(_maxThroughput);
                while (budget-- > 0 && _queue.TryDequeue(out var e))
                {
                    var m = (Msg)e.Message;
                    if (m.Seq != _next[m.Producer])
                        Fail($"producer {m.Producer}: expected seq {_next[m.Producer]}, got {m.Seq}");
                    _next[m.Producer] = m.Seq + 1;
                    Interlocked.Increment(ref _received);
                }

                Volatile.Write(ref _inRun, 0);
                Interlocked.Exchange(ref _status, 0); // SetAsIdle
                if (_queue.HasMessages && Interlocked.CompareExchange(ref _status, 1, 0) == 0) // RegisterForExecution(false, false)
                    Schedule();
            }

            public void AssertAllDelivered(int perProducer)
            {
                foreach (var n in _next) n.Should().Be(perProducer);
                Received.Should().Be(_total);
                _queue.HasMessages.Should().BeFalse();
                _queue.Count.Should().Be(0);
                _queue.TryDequeue(out _).Should().BeFalse();
            }
        }

        private static async Task RunModel(bool lines128, int producers, int perProducer, int maxThroughput, Action<int, int, MailboxModel> produce)
        {
            using var model = new MailboxModel(NewQueue(lines128), producers, (long)producers * perProducer, maxThroughput);
            using var start = new Barrier(producers);
            var stopObserver = false;
            // a third party reading Count and HasMessages concurrently, as diagnostics do
            var observer = new Thread(() =>
            {
                while (!Volatile.Read(ref stopObserver))
                {
                    var c = model.Queue.Count;
                    if (c < 0 || c > (long)producers * perProducer) throw new InvalidOperationException("Count " + c);
                    _ = model.Queue.HasMessages;
                }
            }) { IsBackground = true };
            observer.Start();

            var threads = new Thread[producers];
            for (var p = 0; p < producers; p++)
            {
                var id = p;
                threads[p] = new Thread(() =>
                {
                    start.SignalAndWait();
                    for (var i = 0; i < perProducer; i++) produce(id, i, model);
                }) { IsBackground = true };
                threads[p].Start();
            }

            foreach (var t in threads) t.Join();
            var finished = await Task.WhenAny(model.Done, Task.Delay(TimeSpan.FromSeconds(60)));
            Volatile.Write(ref stopObserver, true);
            observer.Join();

            model.Failure.Should().BeNull();
            finished.Should().Be(model.Done, "every message must be delivered (received {0} of {1}; HasMessages {2}, Count {3}): a lost message or a lost wake-up",
                model.Received, (long)producers * perProducer, model.Queue.HasMessages, model.Queue.Count);
            model.AssertAllDelivered(perProducer);
            model.ConsumerThreads.Should().BeGreaterThan(1, "the consumer must hop between threads");
        }

        [Theory]
        [InlineData(false, 1, 300_000, 30)]
        [InlineData(false, 4, 100_000, 30)]
        [InlineData(false, 8, 50_000, 5)]
        [InlineData(false, 16, 20_000, 30)]
        [InlineData(true, 1, 300_000, 30)]
        [InlineData(true, 8, 50_000, 5)]
        public async Task Producers_with_a_thread_hopping_consumer_lose_duplicate_and_reorder_nothing(bool lines128, int producers, int perProducer, int maxThroughput)
        {
            // Several fresh queues per case, so segment growth (32, 64, ... slots) and the freeze of full
            // segments happen many times while producers race the consumer.
            for (var round = 0; round < 5; round++)
                await RunModel(lines128, producers, perProducer, maxThroughput, (p, i, m) => m.Send(p, i));
        }

        [Theory]
        [InlineData(false, 1, 100_000)]
        [InlineData(false, 2, 50_000)]
        [InlineData(false, 4, 25_000)]
        [InlineData(true, 2, 50_000)]
        public async Task Queue_oscillating_between_empty_and_non_empty_never_loses_a_wake_up(bool lines128, int producers, int perProducer)
        {
            // Producers send in short bursts (1 to 40, so sometimes across a segment boundary) separated by pauses
            // long enough for the consumer to drain the queue and go idle, so the queue keeps flipping between empty
            // and non-empty and the idle/scheduled edge is crossed on most bursts.
            await RunModel(lines128, producers, perProducer, 30, (p, i, m) =>
            {
                m.Send(p, i);
                if (Random.Shared.Next(20) == 0)
                {
                    var spins = Random.Shared.Next(2_000);
                    for (var s = 0; s < spins; s++) Thread.SpinWait(1);
                    if (spins < 50) Thread.Yield();
                }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Single_producer_waiting_for_each_message_crosses_the_parked_consumer_edge_every_time(bool lines128)
        {
            const int count = 50_000;
            using var model = new MailboxModel(NewQueue(lines128), 1, count, 30);
            for (var i = 0; i < count; i++)
            {
                model.Send(0, i);
                model.WaitForIdle(TimeSpan.FromSeconds(60)).Should().BeTrue("the consumer must finish each run before the next send");
            }
            var finished = await Task.WhenAny(model.Done, Task.Delay(TimeSpan.FromSeconds(5)));
            model.Failure.Should().BeNull();
            finished.Should().Be(model.Done, "received {0} of {1}", model.Received, count);
            model.AssertAllDelivered(count);
            model.Runs.Should().BeGreaterThan(count / 10, "most messages should have found the consumer parked");
            model.Runs.Should().Be(count, "each send must find the consumer parked");
            model.ConsumerThreads.Should().BeGreaterThan(1, "the consumer must hop between threads");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Count_is_exact_without_concurrency_across_segment_boundaries(bool lines128)
        {
            var q = NewQueue(lines128);
            var expected = new System.Collections.Generic.Queue<int>();
            var next = 0;
            var rnd = new Random(12345);
            for (var step = 0; step < 200_000; step++)
            {
                if (rnd.Next(100) < 52)
                {
                    q.Enqueue(ActorRefs.Nobody, new Envelope(next, ActorRefs.NoSender));
                    expected.Enqueue(next++);
                }
                else
                {
                    var had = expected.Count > 0;
                    q.TryDequeue(out var e).Should().Be(had);
                    if (had) e.Message.Should().Be(expected.Dequeue());
                }

                q.Count.Should().Be(expected.Count);
                q.HasMessages.Should().Be(expected.Count > 0);
            }
        }

        [Fact]
        public void Dequeued_messages_are_not_kept_alive_by_the_queue()
        {
            var q = NewQueue(false);
            // fill past the first segment so both a frozen segment and the live tail segment are drained
            var refs = EnqueueAndDrain(q, 100);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            foreach (var r in refs) r.IsAlive.Should().BeFalse("a dequeued message must not stay reachable from the queue");
            GC.KeepAlive(q);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] EnqueueAndDrain(IMessageQueue q, int n)
        {
            var refs = new WeakReference[n];
            for (var i = 0; i < n; i++)
            {
                var m = new object();
                refs[i] = new WeakReference(m);
                q.Enqueue(ActorRefs.Nobody, new Envelope(m, ActorRefs.NoSender));
            }

            for (var i = 0; i < n; i++) q.TryDequeue(out _).Should().BeTrue();
            return refs;
        }

        [Fact]
        public void CleanUp_moves_every_message_in_order_and_leaves_the_queue_empty_and_usable()
        {
            var q = NewQueue(false);
            var deadLetters = new UnboundedMessageQueue();
            for (var i = 0; i < 1000; i++) q.Enqueue(ActorRefs.Nobody, new Envelope(i, ActorRefs.NoSender));
            q.CleanUp(ActorRefs.Nobody, deadLetters);

            q.HasMessages.Should().BeFalse();
            q.Count.Should().Be(0);
            deadLetters.Count.Should().Be(1000);
            for (var i = 0; i < 1000; i++)
            {
                deadLetters.TryDequeue(out var e).Should().BeTrue();
                e.Message.Should().Be(i);
            }

            q.Enqueue(ActorRefs.Nobody, new Envelope("after", ActorRefs.NoSender));
            q.TryDequeue(out var last).Should().BeTrue();
            last.Message.Should().Be("after");
        }

        [Fact]
        public async Task Frozen_segment_not_yet_linked_reads_as_empty_and_its_producer_reschedules_the_mailbox()
        {
            var q = new MpscUnboundedMessageQueue<PaddedIndex64>();
            const int firstSegment = 32;
            using var model = new MailboxModel(q, 1, firstSegment + 1, 30);
            for (var i = 0; i < firstSegment; i++) model.Send(0, i);
            model.WaitForIdle(TimeSpan.FromSeconds(10)).Should().BeTrue("the last run must go idle before freezing the segment");
            model.Received.Should().Be(firstSegment);

            // Play the producer that found the first segment full: it holds the segment's lock, has frozen the
            // segment, and has not linked the next one yet (for example it was preempted, or is allocating it).
            var full = q.TailSegment;
            Task late;
            lock (full)
            {
                full.Freeze();

                // The consumer must report empty at once instead of waiting for that producer.
                var dequeue = Task.Run(() => q.TryDequeue(out _));
                dequeue.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("TryDequeue must not wait for the producer that is growing the queue");
                dequeue.Result.Should().BeFalse();
                q.HasMessages.Should().BeFalse();
                q.Count.Should().Be(0);

                // A send in this window waits for the lock, then enqueues into the new segment and schedules the
                // mailbox, which went idle above.
                late = Task.Run(() => model.Send(0, firstSegment));
                late.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse();
                q.LinkNext(full);
            }

            await late.WaitAsync(TimeSpan.FromSeconds(10));
            var finished = await Task.WhenAny(model.Done, Task.Delay(TimeSpan.FromSeconds(10)));
            model.Failure.Should().BeNull();
            finished.Should().Be(model.Done, "the message sent while the queue was growing must not be stranded");
            model.AssertAllDelivered(firstSegment + 1);
        }

        [Fact]
        public void Default_mailbox_creates_the_internal_queue_and_UnboundedMessageQueue_is_unchanged()
        {
            var queue = new UnboundedMailbox().Create(ActorRefs.Nobody, null);
            queue.Should().BeAssignableTo<IUnboundedMessageQueueSemantics>();
            queue.GetType().GetGenericTypeDefinition().Should().Be(typeof(MpscUnboundedMessageQueue<>));
            queue.Should().NotBeAssignableTo<UnboundedMessageQueue>();
        }
    }
}
