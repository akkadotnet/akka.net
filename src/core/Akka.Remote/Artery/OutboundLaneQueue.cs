//-----------------------------------------------------------------------
// <copyright file="OutboundLaneQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The bounded queue of one ordinary outbound lane. The <see cref="Association"/> owns it, so it
    /// outlives every materialization of the lane's stream. Any thread may write, without taking a
    /// lock; one <see cref="OutboundLaneSource"/> at a time reads it.
    ///
    /// <para>
    /// A write fails when the queue holds <see cref="Capacity"/> envelopes or has been completed,
    /// exactly like a full or completed bounded channel in <c>Wait</c> mode. The bound is an
    /// interlocked count reserved before the envelope is enqueued.
    /// </para>
    ///
    /// <para>
    /// <b>Wake-up.</b> A source that finds the queue empty stores its callback and sets
    /// <c>_waiting</c>, then looks again. A writer enqueues, then checks <c>_waiting</c>. Both the
    /// set and the enqueue are full fences, so at least one side sees the other: either the source
    /// finds the envelope, or the writer finds the flag. Whoever clears the flag owns the wake-up,
    /// so it happens at most once; a wake-up that finds nothing to do is harmless.
    /// </para>
    ///
    /// <para>
    /// Consumer-side state (envelopes carried over from a stopped stream, and the shutdown drain)
    /// is guarded by a lock that only the reading stream and the transport's shutdown take, never a
    /// writer.
    /// </para>
    /// </summary>
    internal sealed class OutboundLaneQueue
    {
        private readonly ConcurrentQueue<IOutboundEnvelope> _items = new();
        private int _count;
        private int _waiting;
        private Action? _wake;
        private volatile bool _completed;

        // Consumer side, guarded by _consumerLock.
        private readonly object _consumerLock = new();

        // Envelopes a stopped ArteryBatchEncodeStage had taken from this queue but not sent; read
        // before _items so the lane keeps its order.
        private Queue<IOutboundEnvelope>? _carriedOver;

        // Set by DrainToDropped: from then on, whatever is left when the stream stops is reported.
        private Action<IOutboundEnvelope>? _onDrained;
        private int _closed;

        // Runs the wake-up on the thread pool, as a channel's waiting reader is resumed, so a
        // writer never pays for scheduling the stream and a woken stream finds what was written
        // meanwhile. One instance, because at most one wake-up is outstanding.
        private readonly WakeUp _wakeUp;

        public OutboundLaneQueue(int capacity)
        {
            Capacity = capacity;
            _wakeUp = new WakeUp(this);
        }

        /// <summary>Most envelopes the queue accepts from producers.</summary>
        public int Capacity { get; }

        /// <summary>Envelopes currently queued by producers (carried-over envelopes excluded).</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>Whether anything, queued or carried over, is waiting to be read.</summary>
        public bool IsEmpty
        {
            get
            {
                if (Count > 0)
                    return false;
                lock (_consumerLock)
                    return _carriedOver is not { Count: > 0 };
            }
        }

        /// <summary>Whether <see cref="Complete"/> has been called.</summary>
        public bool IsCompleted => _completed;

        /// <summary>Enqueues <paramref name="envelope"/> unless the queue is full or completed. Never blocks.</summary>
        public bool TryWrite(IOutboundEnvelope envelope)
        {
            if (_completed)
                return false;

            if (Interlocked.Increment(ref _count) > Capacity)
            {
                Interlocked.Decrement(ref _count);
                return false;
            }

            _items.Enqueue(envelope);
            WakeConsumer();

            // Completed and drained while this write was in flight: nothing reads the queue any more.
            if (Volatile.Read(ref _closed) == 1)
                ReportLeftovers();
            return true;
        }

        /// <summary>Takes the oldest envelope, if any.</summary>
        public bool TryRead([NotNullWhen(true)] out IOutboundEnvelope? envelope)
        {
            lock (_consumerLock)
                return TryDequeue(out envelope);
        }

        /// <summary>
        /// Takes the oldest envelope. If there is none and the queue is not completed,
        /// <paramref name="wake"/> is registered and invoked, once, by the next write or by
        /// <see cref="Complete"/>.
        /// </summary>
        public bool TryReadOrWait(Action wake, [NotNullWhen(true)] out IOutboundEnvelope? envelope)
        {
            while (true)
            {
                if (TryRead(out envelope))
                    return true;
                if (_completed)
                    return false;

                _wake = wake;
                Interlocked.Exchange(ref _waiting, 1);
                if (_items.IsEmpty && !_completed)
                    return false;

                // Something arrived meanwhile. If a writer already claimed the wake-up it will
                // call back; otherwise take the flag back and read again.
                if (Interlocked.Exchange(ref _waiting, 0) == 0)
                    return false;
            }
        }

        /// <summary>
        /// The reading stream has stopped: withdraws its wake-up. After <see cref="DrainToDropped"/>
        /// nothing will read the queue again, so whatever is left is reported as drained.
        /// </summary>
        public void ReleaseConsumer()
        {
            Interlocked.Exchange(ref _waiting, 0);
            _wake = null;
            if (_onDrained is not null)
                ReportLeftovers();
        }

        /// <summary>Refuses further writes; envelopes already queued can still be read.</summary>
        public void Complete()
        {
            _completed = true;
            WakeConsumer();
        }

        /// <summary>
        /// Transport shutdown, after <see cref="Complete"/>: hands every envelope still queued to
        /// <paramref name="onDrained"/>. A live stream may still win some of them; whatever is left
        /// when it stops is handed over then (see <see cref="ReleaseConsumer"/>).
        /// </summary>
        public void DrainToDropped(Action<IOutboundEnvelope> onDrained)
        {
            lock (_consumerLock)
                _onDrained = onDrained;
            ReportLeftovers();
        }

        /// <summary>
        /// Keeps <paramref name="held"/>, oldest first, to be read before anything else. Returns
        /// <see langword="false"/> once the queue is completed; the caller then reports them as dropped.
        /// </summary>
        public bool TryCarryOver(Queue<IOutboundEnvelope> held)
        {
            lock (_consumerLock)
            {
                if (_completed)
                    return false;

                // Anything still carried was read after `held`, so it goes behind it.
                if (_carriedOver is { } older)
                    foreach (var envelope in older)
                        held.Enqueue(envelope);

                _carriedOver = held;
            }

            WakeConsumer();
            return true;
        }

        private bool TryDequeue([NotNullWhen(true)] out IOutboundEnvelope? envelope)
        {
            if (_carriedOver is { Count: > 0 } carried)
            {
                envelope = carried.Dequeue();
                return true;
            }

            if (!_items.TryDequeue(out envelope))
                return false;

            Interlocked.Decrement(ref _count);
            return true;
        }

        private void ReportLeftovers()
        {
            List<IOutboundEnvelope>? leftovers = null;
            Action<IOutboundEnvelope>? onDrained;
            lock (_consumerLock)
            {
                onDrained = _onDrained;
                if (onDrained is null)
                    return;

                // Closed before looking, with a full fence: a write that enqueues after this look
                // sees the flag and reports its own envelope.
                Interlocked.Exchange(ref _closed, 1);
                while (TryDequeue(out var envelope))
                    (leftovers ??= new List<IOutboundEnvelope>()).Add(envelope);
            }

            if (leftovers is not null)
                foreach (var envelope in leftovers)
                    onDrained(envelope);
        }

        private void WakeConsumer()
        {
            // Enqueue and Complete's volatile write come first; Exchange is a full fence.
            if (Volatile.Read(ref _waiting) == 1 && Interlocked.Exchange(ref _waiting, 0) == 1)
                ThreadPool.UnsafeQueueUserWorkItem(_wakeUp, preferLocal: false);
        }

        private sealed class WakeUp : IThreadPoolWorkItem
        {
            private readonly OutboundLaneQueue _queue;

            public WakeUp(OutboundLaneQueue queue) => _queue = queue;

            public void Execute() => _queue._wake?.Invoke();
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// The only reader of an <see cref="OutboundLaneQueue"/> while its stream runs: pushes one
    /// envelope per pull, takes nothing from the queue without demand, and completes once the
    /// queue is completed and empty.
    /// </summary>
    internal sealed class OutboundLaneSource : GraphStage<SourceShape<IOutboundEnvelope>>
    {
        private readonly OutboundLaneQueue _queue;

        public OutboundLaneSource(OutboundLaneQueue queue)
        {
            _queue = queue;
            Shape = new SourceShape<IOutboundEnvelope>(Out);
        }

        private Outlet<IOutboundEnvelope> Out { get; } = new("OutboundLaneSource.out");

        public override SourceShape<IOutboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly OutboundLaneSource _stage;
            private readonly Action _wake;

            public Logic(OutboundLaneSource stage) : base(stage.Shape)
            {
                _stage = stage;
                _wake = GetAsyncCallback(OnWake);
                SetHandler(stage.Out, this);
            }

            public override void OnPull() => PushOrWait();

            public override void PostStop() => _stage._queue.ReleaseConsumer();

            private void OnWake()
            {
                if (IsAvailable(_stage.Out))
                    PushOrWait();
            }

            private void PushOrWait()
            {
                if (_stage._queue.TryReadOrWait(_wake, out var envelope))
                    Push(_stage.Out, envelope);
                else if (_stage._queue.IsCompleted && _stage._queue.IsEmpty)
                    CompleteStage();
            }
        }
    }
}
