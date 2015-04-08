//-----------------------------------------------------------------------
// <copyright file="MonoConcurrentQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Akka.Util
{
    // ConcurrentQueue.cs
    //
    // Copyright (c) 2008 Jérémie "Garuma" Laval
    //
    // Permission is hereby granted, free of charge, to any person obtaining a copy
    // of this software and associated documentation files (the "Software"), to deal
    // in the Software without restriction, including without limitation the rights
    // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    // copies of the Software, and to permit persons to whom the Software is
    // furnished to do so, subject to the following conditions:
    //
    // The above copyright notice and this permission notice shall be included in
    // all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    // THE SOFTWARE.
    //
    //
    [DebuggerDisplay("Count={Count}")]
    public class MonoConcurrentQueue<T> : IProducerConsumerCollection<T>, IEnumerable<T>, ICollection,
        IEnumerable
    {
        private readonly object _syncRoot = new object();
        private int _count;
        private Node _head = new Node();
        private Node _tail;

        public MonoConcurrentQueue()
        {
            _tail = _head;
        }

        public MonoConcurrentQueue(IEnumerable<T> collection)
            : this()
        {
            foreach (T item in collection)
                Enqueue(item);
        }

        public bool IsEmpty
        {
            get { return _count == 0; }
        }

        bool IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (IEnumerator) InternalGetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return InternalGetEnumerator();
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (array.Rank > 1)
                throw new ArgumentException("The array can't be multidimensional");
            if (array.GetLowerBound(0) != 0)
                throw new ArgumentException("The array needs to be 0-based");

            var dest = array as T[];
            if (dest == null)
                throw new ArgumentException("The array cannot be cast to the collection element type", "array");
            CopyTo(dest, index);
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (index < 0)
                throw new ArgumentOutOfRangeException("index");
            if (index >= array.Length)
                throw new ArgumentException("index is equals or greater than array length", "index");

            var e = InternalGetEnumerator();
            var i = index;
            while (e.MoveNext())
            {
                if (i == array.Length - index)
                    throw new ArgumentException(
                        "The number of elements in the collection exceeds the capacity of array", "array");
                array[i++] = e.Current;
            }
        }

        public T[] ToArray()
        {
            return new List<T>(this).ToArray();
        }

        bool ICollection.IsSynchronized
        {
            get { return true; }
        }

        bool IProducerConsumerCollection<T>.TryTake(out T item)
        {
            return TryDequeue(out item);
        }

        object ICollection.SyncRoot
        {
            get { return _syncRoot; }
        }

        public int Count
        {
            get { return _count; }
        }

        public void Enqueue(T item)
        {
            var node = new Node();
            node.Value = item;

            Node oldTail = null;
            Node oldNext = null;

            var update = false;
            while (!update)
            {
                oldTail = _tail;
                oldNext = oldTail.Next;

                // Did tail was already updated ?
                if (_tail == oldTail)
                {
                    if (oldNext == null)
                    {
                        // The place is for us
                        update = Interlocked.CompareExchange(ref _tail.Next, node, null) == null;
                    }
                    else
                    {
                        // another Thread already used the place so give him a hand by putting tail where it should be
                        Interlocked.CompareExchange(ref _tail, oldNext, oldTail);
                    }
                }
            }
            // At this point we added correctly our node, now we have to update tail. If it fails then it will be done by another thread
            Interlocked.CompareExchange(ref _tail, node, oldTail);
            Interlocked.Increment(ref _count);
        }

        public bool TryDequeue(out T result)
        {
            result = default(T);
            Node oldNext = null;
            var advanced = false;

            while (!advanced)
            {
                var oldHead = _head;
                var oldTail = _tail;
                oldNext = oldHead.Next;

                if (oldHead == _head)
                {
                    // Empty case ?
                    if (oldHead == oldTail)
                    {
                        // This should be false then
                        if (oldNext != null)
                        {
                            // If not then the linked list is mal formed, update tail
                            Interlocked.CompareExchange(ref _tail, oldNext, oldTail);
                            continue;
                        }
                        result = default(T);
                        return false;
                    }
                    else
                    {
                        result = oldNext.Value;
                        advanced = Interlocked.CompareExchange(ref _head, oldNext, oldHead) == oldHead;
                    }
                }
            }

            oldNext.Value = default(T);

            Interlocked.Decrement(ref _count);

            return true;
        }

        public bool TryPeek(out T result)
        {
            result = default(T);
            var update = true;

            while (update)
            {
                var oldHead = _head;
                var oldNext = oldHead.Next;

                if (oldNext == null)
                {
                    result = default(T);
                    return false;
                }

                result = oldNext.Value;

                //check if head has been updated
                update = _head != oldHead;
            }
            return true;
        }

        internal void Clear()
        {
            _count = 0;
            _tail = _head = new Node();
        }

        private IEnumerator<T> InternalGetEnumerator()
        {
            var my_head = _head;
            while ((my_head = my_head.Next) != null)
            {
                yield return my_head.Value;
            }
        }

        private class Node
        {
            public Node Next;
            public T Value;
        }
    }
}
