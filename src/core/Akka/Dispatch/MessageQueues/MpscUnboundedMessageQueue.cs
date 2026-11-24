//-----------------------------------------------------------------------
// <copyright file="MpscUnboundedMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>
    /// INTERNAL API. Creates the message queue of the default <see cref="UnboundedMailbox"/>.
    /// </summary>
    internal static class MpscUnboundedMessageQueue
    {
        // ConcurrentQueue pads with 128-byte lines where CoreLib is built with them (Arm64, LoongArch64) and with
        // 64-byte lines elsewhere. Akka.dll is architecture neutral, so pick the layout when the process starts.
        private static readonly bool Uses128ByteLines =
            RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.LoongArch64;

        public static IMessageQueue Create() => Uses128ByteLines
            ? new MpscUnboundedMessageQueue<PaddedIndex128>()
            : new MpscUnboundedMessageQueue<PaddedIndex64>();
    }

    /// <summary>
    /// INTERNAL API. A segment's consumer index (Head) and producer index (Tail), each on its own cache line.
    /// Implemented by structs, so every member inlines into the specialized queue code.
    /// </summary>
    internal interface IPaddedIndex
    {
        /// <summary>Plain read and write, for the consumer, the only thread that writes Head.</summary>
        int Head { get; set; }
        int VolatileHead { get; }
        int VolatileTail { get; }
        bool TryReserve(int tail); // compare-exchange Tail from tail to tail + 1
        void AddToTail(int value);
    }

    /// <summary>
    /// INTERNAL API. 64-byte lines: Head and Tail 128 bytes apart, so they are never in the same adjacent-line
    /// prefetch pair, and a line of padding before Head and after Tail.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4 * 64)]
    internal struct PaddedIndex64 : IPaddedIndex
    {
        [FieldOffset(1 * 64)] private int _head;
        [FieldOffset(3 * 64)] private int _tail;

        public int Head { get => _head; set => _head = value; }
        public int VolatileHead => Volatile.Read(ref _head);
        public int VolatileTail => Volatile.Read(ref _tail);
        public bool TryReserve(int tail) => Interlocked.CompareExchange(ref _tail, tail + 1, tail) == tail;
        public void AddToTail(int value) => Interlocked.Add(ref _tail, value);
    }

    /// <summary>INTERNAL API. 128-byte lines, laid out as ConcurrentQueue's PaddedHeadAndTail is on Arm64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 3 * 128)]
    internal struct PaddedIndex128 : IPaddedIndex
    {
        [FieldOffset(1 * 128)] private int _head;
        [FieldOffset(2 * 128)] private int _tail;

        public int Head { get => _head; set => _head = value; }
        public int VolatileHead => Volatile.Read(ref _head);
        public int VolatileTail => Volatile.Read(ref _tail);
        public bool TryReserve(int tail) => Interlocked.CompareExchange(ref _tail, tail + 1, tail) == tail;
        public void AddToTail(int value) => Interlocked.Add(ref _tail, value);
    }

    /// <summary>
    /// INTERNAL API. The message queue of the default <see cref="UnboundedMailbox"/>: unbounded, FIFO, any number
    /// of producers and ONE consumer at a time - the mailbox run that holds the Scheduled bit, or
    /// <see cref="CleanUp"/> called from that run. It must not be used where two threads may dequeue at once.
    /// <para>
    /// Built like ConcurrentQueue: a chain of ring segments with a sequence number per slot, growing from 32 to 1M
    /// slots, and enqueue reserves a slot with one compare-exchange on the tail segment's Tail. Because nothing else
    /// dequeues, dequeue, <see cref="HasMessages"/> and <see cref="Count"/> need no interlocked operation.
    /// </para>
    /// <para>
    /// Correctness. An enqueue takes effect at its successful compare-exchange on Tail, which reserves slot
    /// <c>tail &amp; mask</c>; the producer then stores the envelope and publishes it by setting the slot's sequence to
    /// <c>tail + 1</c>. HasMessages and Count (Tail - Head) include a reserved slot that is not published yet, and
    /// TryDequeue, which only takes a slot whose sequence is <c>Head + 1</c>, waits for it as ConcurrentQueue does,
    /// so FIFO order holds and the mailbox never goes idle over it. A full segment is frozen by adding FreezeOffset
    /// to its Tail before the next segment is linked; every later compare-exchange on it fails, and the consumer
    /// leaves it once Head has reached <c>Tail - FreezeOffset</c>. If the consumer finds a segment frozen and drained
    /// but the next one not linked yet, it reports the queue empty, as ConcurrentQueue does: the producer that froze
    /// the segment has not completed its enqueue, and it schedules the mailbox after it does. Only the consumer
    /// writes Head; another thread reading Count or HasMessages may see an older Head, which can over-count but never
    /// misses a message whose enqueue completed before the read.
    /// </para>
    /// </summary>
    internal sealed class MpscUnboundedMessageQueue<TIndex> : IMessageQueue, IUnboundedMessageQueueSemantics
        where TIndex : struct, IPaddedIndex
    {
        private const int InitialSegmentLength = 32, MaxSegmentLength = 1024 * 1024; // as ConcurrentQueue

        private volatile Segment _head; // written only by the consumer
        private volatile Segment _tail; // written only while holding the lock of the segment it replaces

        public MpscUnboundedMessageQueue() => _head = _tail = new Segment(InitialSegmentLength);

        internal Segment TailSegment => _tail;

        /// <inheritdoc cref="IMessageQueue"/>
        public bool HasMessages
        {
            get
            {
                for (var s = _head; s != null; s = s.Next)
                    if (s.Count > 0) return true;
                return false;
            }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public int Count
        {
            get
            {
                var count = 0;
                for (var s = _head; s != null; s = s.Next) count += s.Count;
                return count;
            }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            while (true)
            {
                var tail = _tail;
                if (tail.TryEnqueue(in envelope)) return;
                lock (tail) // full: one producer freezes it and links the next segment; segments are private, so the lock is too
                {
                    if (tail != _tail) continue; // another producer already did
                    tail.Freeze();
                    LinkNext(tail);
                }
            }
        }

        /// <summary>Links a segment after the frozen tail segment. Caller holds the tail segment's lock.</summary>
        internal void LinkNext(Segment frozenTail)
        {
            var next = new Segment(Math.Min(frozenTail.Slots.Length * 2, MaxSegmentLength));
            frozenTail.Next = next;
            _tail = next;
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public bool TryDequeue(out Envelope envelope)
        {
            var segment = _head;
            var spinner = new SpinWait();
            while (true)
            {
                var slots = segment.Slots;
                var head = segment.Index.Head;
                ref var slot = ref slots[head & (slots.Length - 1)];
                if (Volatile.Read(ref slot.Sequence) == head + 1)
                {
                    envelope = slot.Item;
                    slot.Item = default; // do not keep the message alive
                    Volatile.Write(ref slot.Sequence, head + slots.Length); // hand the slot back to producers
                    segment.Index.Head = head + 1;
                    return true;
                }

                var next = segment.Next; // read before Tail: once Next is linked, Tail is already frozen
                var reserved = segment.Index.VolatileTail - head;
                if (reserved == 0 || reserved == segment.FreezeOffset) // nothing reserved and unread in this segment
                {
                    if (next == null)
                    {
                        // Empty; or frozen by a producer that has not linked the next segment yet. That producer's
                        // enqueue has not taken effect, and it schedules the mailbox once it has.
                        envelope = default;
                        return false;
                    }

                    _head = segment = next; // frozen and drained
                    continue;
                }

                spinner.SpinOnce(sleep1Threshold: -1); // a slot is reserved but its envelope not yet published
            }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            while (TryDequeue(out var msg))
            {
                deadletters.Enqueue(owner, msg);
            }
        }

        internal sealed class Segment
        {
            internal readonly Slot[] Slots;
            internal readonly int FreezeOffset;
            internal volatile Segment? Next;
            internal TIndex Index;

            internal Segment(int length)
            {
                Slots = new Slot[length];
                for (var i = 0; i < length; i++) Slots[i].Sequence = i;
                FreezeOffset = length * 2;
            }

            internal int Count
            {
                get
                {
                    var head = Index.VolatileHead;
                    var n = Index.VolatileTail - head;
                    return Math.Clamp(n >= FreezeOffset ? n - FreezeOffset : n, 0, Slots.Length);
                }
            }

            /// <summary>Makes every later reservation in this segment fail. Caller holds this segment's lock.</summary>
            internal void Freeze() => Index.AddToTail(FreezeOffset);

            internal bool TryEnqueue(in Envelope envelope)
            {
                while (true)
                {
                    var tail = Index.VolatileTail;
                    ref var slot = ref Slots[tail & (Slots.Length - 1)];
                    var diff = Volatile.Read(ref slot.Sequence) - tail;
                    if (diff == 0)
                    {
                        if (Index.TryReserve(tail))
                        {
                            slot.Item = envelope;
                            Volatile.Write(ref slot.Sequence, tail + 1);
                            return true;
                        }
                    }
                    else if (diff < 0)
                    {
                        return false; // full, or frozen
                    }
                    // otherwise another producer reserved this slot first: retry with the new Tail
                }
            }
        }

        internal struct Slot
        {
            public Envelope Item;
            public int Sequence;
        }
    }
}
