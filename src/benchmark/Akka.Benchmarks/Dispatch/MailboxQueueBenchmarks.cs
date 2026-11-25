//-----------------------------------------------------------------------
// <copyright file="MailboxQueueBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Dispatch
{
    /// <summary>
    /// The two queues compared: <see cref="UnboundedMessageQueue"/> (over ConcurrentQueue, the default mailbox queue
    /// before) and the internal single-consumer queue the default <see cref="UnboundedMailbox"/> creates now.
    /// </summary>
    public static class MailboxQueues
    {
        public const string Concurrent = "ConcurrentQueue";
        public const string Mpsc = "MpscUnboundedMessageQueue";

        public static IMessageQueue Create(string kind) =>
            kind == Concurrent ? new UnboundedMessageQueue() : MpscUnboundedMessageQueue.Create();
    }

    /// <summary>
    /// One thread, the way a mailbox is used when the sender and the actor share a thread: enqueue a burst of
    /// <see cref="Burst"/> envelopes, then drain them (Burst = 1 is plain enqueue+dequeue). Reported per message.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(launchCount: 1, warmupCount: 5, iterationCount: 15)]
    public class MailboxQueueSingleThreadBenchmarks
    {
        private const int Messages = 3_000; // divisible by every Burst value
        private IMessageQueue _queue;
        private Envelope _envelope;

        [Params(MailboxQueues.Concurrent, MailboxQueues.Mpsc)]
        public string Queue { get; set; }

        [Params(1, 8, 30)]
        public int Burst { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _queue = MailboxQueues.Create(Queue);
            _envelope = new Envelope("hit", ActorRefs.NoSender);
            for (var i = 0; i < 4096; i++) _queue.Enqueue(ActorRefs.Nobody, _envelope); // grow past the first segments, as a busy mailbox would
            while (_queue.TryDequeue(out _)) { }
        }

        [Benchmark(OperationsPerInvoke = Messages)]
        public void EnqueueThenDrain()
        {
            var q = _queue;
            var e = _envelope;
            for (var i = 0; i < Messages; i += Burst)
            {
                for (var j = 0; j < Burst; j++) q.Enqueue(ActorRefs.Nobody, e);
                while (q.TryDequeue(out _)) { }
            }
        }
    }

    /// <summary>
    /// <see cref="Producers"/> threads together enqueue <see cref="Total"/> envelopes while the benchmark thread is
    /// the single consumer, polling TryDequeue until it has them all. Reported per message. Producers + 1 threads run
    /// at once, so the 8-producer case oversubscribes an 8-logical-core machine.
    /// <para>
    /// With one producer the single-consumer queue is the SLOWER one here, most likely because a consumer that does no work per message
    /// keeps up with the producer and reads the slots it is writing, so the lines move between the two cores on
    /// every message, while ConcurrentQueue's consumer, slowed by its compare-exchange, falls behind and the two work
    /// on different lines. Once the consumer spends some tens of nanoseconds per message, as any actor does, the
    /// two queues converge. This case is kept so that the cost stays visible.
    /// </para>
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(launchCount: 1, warmupCount: 5, iterationCount: 15)]
    public class MailboxQueueContendedBenchmarks
    {
        private const int Total = 1 << 18; // divisible by every Producers value
        private IMessageQueue _queue;
        private Envelope _envelope;
        private Thread[] _producers;
        private Barrier _start;
        private volatile bool _stop;

        [Params(MailboxQueues.Concurrent, MailboxQueues.Mpsc)]
        public string Queue { get; set; }

        [Params(1, 4, 8)]
        public int Producers { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _queue = MailboxQueues.Create(Queue);
            _envelope = new Envelope("hit", ActorRefs.NoSender);
            _start = new Barrier(Producers + 1);
            _producers = new Thread[Producers];
            for (var p = 0; p < Producers; p++)
            {
                _producers[p] = new Thread(Produce) { IsBackground = true };
                _producers[p].Start();
            }
        }

        private void Produce()
        {
            var q = _queue;
            var e = _envelope;
            while (true)
            {
                _start.SignalAndWait();
                if (_stop) return;
                for (int i = 0, n = Total / Producers; i < n; i++) q.Enqueue(ActorRefs.Nobody, e);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _stop = true;
            _start.SignalAndWait();
            foreach (var t in _producers) t.Join();
        }

        [Benchmark(OperationsPerInvoke = Total)]
        public int ProducersToOneConsumer()
        {
            var q = _queue;
            var remaining = Total;
            _start.SignalAndWait();
            while (remaining > 0)
                if (q.TryDequeue(out _)) remaining--;
            return remaining;
        }
    }

    /// <summary>Bytes retained by one empty mailbox queue: there is one per actor.</summary>
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
    public class MailboxQueueFootprintBenchmarks
    {
        [Params(MailboxQueues.Concurrent, MailboxQueues.Mpsc)]
        public string Queue { get; set; }

        [Benchmark]
        public IMessageQueue CreateEmptyQueue() => MailboxQueues.Create(Queue);
    }
}
