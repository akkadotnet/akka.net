//-----------------------------------------------------------------------
// <copyright file="StableListPriorityQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// Priority queue implemented using a simple list with binary search for inserts.
    /// This specific implementation is cheap in terms of memory but weak in terms of performance.
    /// See http://visualstudiomagazine.com/articles/2012/11/01/priority-queues-with-c.aspx for original implementation
    /// This specific version is adapted for Envelopes only and calculates a priority of envelope.Message
    /// </summary>
    public sealed class StableListPriorityQueue
    {
        private struct WrappedEnvelope
        {
            public WrappedEnvelope(Envelope envelope, int sequenceNumber)
            {
                Envelope = envelope;
                SequenceNumber = sequenceNumber;
            }

            public Envelope Envelope { get; }
            public int SequenceNumber { get; }
        }

        private class WrappedEnvelopeComparator
        {
            private readonly Func<object, int> priorityCalculator;

            public WrappedEnvelopeComparator(Func<object, int> priorityCalculator)
            {
                this.priorityCalculator = priorityCalculator;
            }

            public int Compare(WrappedEnvelope x, WrappedEnvelope y)
            {
                var baseCompare = priorityCalculator(x.Envelope.Message).CompareTo(priorityCalculator(y.Envelope.Message));
                if (baseCompare != 0) return baseCompare;
                return x.SequenceNumber.CompareTo(y.SequenceNumber);
            }
        }

        private readonly List<WrappedEnvelope> _data;
        private readonly WrappedEnvelopeComparator comparator;

        /// <summary>
        /// The default priority generator.
        /// </summary>
        internal static readonly Func<object, int> DefaultPriorityCalculator = message => 1;
        private int sequenceNumber;

        /// <summary>
        /// Creates a new priority queue.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the queue.</param>
        /// <param name="priorityCalculator">The calculator function for assigning message priorities.</param>
        public StableListPriorityQueue(int initialCapacity, Func<object, int> priorityCalculator)
        {
            _data = new List<WrappedEnvelope>(initialCapacity);
            comparator = new WrappedEnvelopeComparator(priorityCalculator);
        }

        /// <summary>
        /// Enqueues a message into the priority queue.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        public void Enqueue(Envelope item)
        {
            int seq = Interlocked.Increment(ref sequenceNumber);
            var wrappedItem = new WrappedEnvelope(item, seq);

            _data.Add(wrappedItem);
            var ci = _data.Count - 1; // child index; start at end
            while (ci > 0)
            {
                var pi = (ci - 1) / 2; // parent index
                if (comparator.Compare(_data[ci], _data[pi]) >= 0) break; // child item is larger than (or equal) parent so we're done
                var tmp = _data[ci]; _data[ci] = _data[pi]; _data[pi] = tmp;
                ci = pi;
            }
        }

        /// <summary>
        /// Dequeues the highest priority message at the front of the priority queue.
        /// </summary>
        /// <returns>The highest priority message <see cref="Envelope"/>.</returns>
        public Envelope Dequeue()
        {
            // assumes pq is not empty; up to calling code
            var li = _data.Count - 1; // last index (before removal)
            var frontItem = _data[0];   // fetch the front
            _data[0] = _data[li];
            _data.RemoveAt(li);

            --li; // last index (after removal)
            var pi = 0; // parent index. start at front of pq
            while (true)
            {
                var ci = pi * 2 + 1; // left child index of parent
                if (ci > li) break;  // no children so done
                var rc = ci + 1;     // right child
                if (rc <= li && comparator.Compare(_data[rc], _data[ci]) < 0) // if there is a rc (ci + 1), and it is smaller than left child, use the rc instead
                    ci = rc;
                if (comparator.Compare(_data[pi], _data[ci]) <= 0) break; // parent is smaller than (or equal to) smallest child so done
                var tmp = _data[pi]; _data[pi] = _data[ci]; _data[ci] = tmp; // swap parent and child
                pi = ci;
            }
            return frontItem.Envelope;
        }

        /// <summary>
        /// Peek at the message at the front of the priority queue.
        /// </summary>
        /// <returns>The highest priority message <see cref="Envelope"/>.</returns>
        public Envelope Peek()
        {
            return _data[0].Envelope;
        }

        /// <summary>
        /// Counts the number of items in the priority queue.
        /// </summary>
        /// <returns>The total number of items in the queue.</returns>
        public int Count()
        {
            return _data.Count;
        }

        /// <summary>
        /// Converts the queue to a string representation.
        /// </summary>
        /// <returns>A string representation of the queue.</returns>
        public override string ToString()
        {
            var s = "";
            for (var i = 0; i < _data.Count; ++i)
                s += _data[i].ToString() + " ";
            s += "count = " + _data.Count;
            return s;
        }

        /// <summary>
        /// Checks the integrity of the StableListPriorityQueue.
        /// </summary>
        /// <returns><c>true</c> if the list is consistent, false otherwise.</returns>
        /// <remarks>
        /// WARNING: high performance impact. Call during testing only.
        /// </remarks>
        public bool IsConsistent()
        {
            // is the heap property true for all data?
            if (_data.Count == 0) return true;
            var li = _data.Count - 1; // last index
            for (var pi = 0; pi < _data.Count; ++pi) // each parent index
            {
                var lci = 2 * pi + 1; // left child index
                var rci = 2 * pi + 2; // right child index

                if (lci <= li && comparator.Compare(_data[pi], _data[lci]) > 0) return false; // if lc exists and it's greater than parent then bad.
                if (rci <= li && comparator.Compare(_data[pi], _data[rci]) > 0) return false; // check the right child too.
            }
            return true; // passed all checks
        }
    }
}
