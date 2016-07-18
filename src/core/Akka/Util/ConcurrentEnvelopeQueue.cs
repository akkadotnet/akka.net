using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// A lighter replacement for <see cref="System.Collections.Concurrent.ConcurrentQueue{Envelope}"/>
    /// </summary>
    /// <remarks>
    /// This implemenatation is aware of the <see cref="Envelope"/> structure using its two fields <see cref="Envelope.Message"/> and <see cref="Envelope.Sender"/> to persist data 
    /// without additional overhead of the status field. Writing and reading every <see cref="Segment._messages"/> item with <see cref="Volatile"/> ensures proper ordering without the burden of maintaining the envelope's struct status.
    /// </remarks>
    public sealed class ConcurrentEnvelopeQueue
    {
        private volatile Segment _head;
        private volatile Segment _tail;

        private const int SegmentSize = 32;
        private const int SegmentSizeMask = SegmentSize - 1;

        /// <summary>
        /// Inititalizes this queue.
        /// </summary>
        public ConcurrentEnvelopeQueue()
        {
            _head = _tail = new Segment(0L, this);
        }

        /// <summary>
        /// Returns true if the queue is empty.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                var segment = _head;
                if (!segment.IsEmpty)
                    return false;
                if (segment.Next == null)
                    return true;
                var spinWait = new SpinWait();
                for (; segment.IsEmpty; segment = _head)
                {
                    if (segment.Next == null)
                        return true;
                    spinWait.SpinOnce();
                }
                return false;
            }
        }

        /// <summary>
        /// Counts elements in the queue.
        /// </summary>
        public int Count
        {
            get
            {
                Segment head;
                Segment tail;
                int headLow;
                int tailHigh;
                this.GetHeadTailPositions(out head, out tail, out headLow, out tailHigh);
                if (head == tail)
                    return tailHigh - headLow + 1;
                return 32 - headLow + 32*(int) (tail.Index - head.Index - 1L) + (tailHigh + 1);
            }
        }

        private void GetHeadTailPositions(out Segment head, out Segment tail, out int headLow, out int tailHigh)
        {
            head = _head;
            tail = _tail;
            headLow = head.Low;
            tailHigh = tail.High;
            var spinWait = new SpinWait();
            while (head != _head || tail != _tail || (headLow != head.Low || tailHigh != tail.High) ||
                   head.Index > tail.Index)
            {
                spinWait.SpinOnce();
                head = _head;
                tail = _tail;
                headLow = head.Low;
                tailHigh = tail.High;
            }
        }

        /// <summary>
        /// Enqueues <paramref name="item"/>.
        /// </summary>
        public void Enqueue(ref Envelope item)
        {
            var spinWait = new SpinWait();
            while (!_tail.TryAppend(ref item))
                spinWait.SpinOnce();
        }

        /// <summary>
        /// Tries to dequeue an envelope.
        /// </summary>
        /// <returns>If dequeue succeeded.</returns>
        public bool TryDequeue(out Envelope result)
        {
            while (!IsEmpty)
            {
                if (_head.TryRemove(out result))
                    return true;
            }
            result = default(Envelope);
            return false;
        }

        private class Segment
        {
            internal readonly long Index;
            private volatile IActorRef[] _senders;
            private volatile object[] _messages;
            private volatile Segment _next;
            private volatile int _low;
            private volatile int _high;
            private volatile ConcurrentEnvelopeQueue _source;

            internal Segment Next => _next;

            internal bool IsEmpty => Low > High;

            internal int Low => Math.Min(_low, SegmentSize);

            internal int High => Math.Min(_high, SegmentSizeMask);

            internal Segment(long index, ConcurrentEnvelopeQueue source)
            {
                _senders = new IActorRef[SegmentSize];
                _messages = new object[SegmentSize];
                _high = -1;
                Index = index;
                _source = source;
            }

            void Grow()
            {
                _next = new Segment(Index + 1L, _source);
                _source._tail = _next;
            }

            internal bool TryAppend(ref Envelope value)
            {
                if (_high >= SegmentSizeMask)
                    return false;
                int index;
                try
                {
                }
                finally
                {
                    index = Interlocked.Increment(ref _high);
                    if (index <= SegmentSizeMask)
                    {
                        _senders[index] = value.Sender;
                        Volatile.Write(ref _messages[index], value.Message);
                    }
                    if (index == SegmentSizeMask)
                        this.Grow();
                }
                return index <= SegmentSizeMask;
            }

            internal bool TryRemove(out Envelope result)
            {
                var sw1 = new SpinWait();
                var low = Low;
                for (var high = High; low <= high; high = High)
                {
                    if (Interlocked.CompareExchange(ref _low, low + 1, low) == low)
                    {
                        var sw2 = new SpinWait();
                        object msg;
                        while ((msg = Volatile.Read(ref _messages[low])) == null)
                            sw2.SpinOnce();
                        var sender = _senders[low];

                        _senders[low] = null;
                        _messages[low] = null;

                        if (low + 1 >= SegmentSize)
                        {
                            var sw3 = new SpinWait();
                            while (_next == null)
                                sw3.SpinOnce();
                            _source._head = _next;
                        }

                        result = new Envelope {Message = msg, Sender = sender};

                        return true;
                    }
                    sw1.SpinOnce();
                    low = Low;
                }
                result = default(Envelope);
                return false;
            }
        }
    }
}