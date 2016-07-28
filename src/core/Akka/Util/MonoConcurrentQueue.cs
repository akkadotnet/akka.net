//-----------------------------------------------------------------------
// <copyright file="MonoConcurrentQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.ICollection" /> to an <see cref="T:System.Array" />, starting at a particular <see cref="T:System.Array" /> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from <see cref="T:System.Collections.ICollection" />. The <see cref="T:System.Array" /> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array" /> at which copying begins.</param>
        /// <exception cref="ArgumentException">
        /// This excetpion can be thrown fo a number of reasons. These include:
        /// <ul>
        /// <li>The given array is multi-dimensional.</li>
        /// <li>The given array is non-zero based.</li>
        /// <li> The given array couldn't be cast to the collection element type.</li>
        /// </ul>
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="array"/> is undefined.
        /// </exception>
        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array), "Array cannot be null");
            if (array.Rank > 1)
                throw new ArgumentException("The array can't be multidimensional", nameof(array));
            if (array.GetLowerBound(0) != 0)
                throw new ArgumentException("The array needs to be 0-based", nameof(array));

            var dest = array as T[];
            if (dest == null)
                throw new ArgumentException("The array cannot be cast to the collection element type", nameof(array));
            CopyTo(dest, index);
        }

        /// <summary></summary>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the index is greater than the length of the array
        /// or the number of elements in the collection exceed that array's capactiy.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="array"/> is undefined.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the given <paramref name="index"/> is negative.
        /// </exception>
        public void CopyTo(T[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array), "Array cannot be null");
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Index cannot be less than 0");
            if (index >= array.Length)
                throw new ArgumentException("index is equals or greater than array length", nameof(index));

            var e = InternalGetEnumerator();
            var i = index;
            while (e.MoveNext())
            {
                if (i == array.Length - index)
                    throw new ArgumentException("The number of elements in the collection exceeds the capacity of array", nameof(array));
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

