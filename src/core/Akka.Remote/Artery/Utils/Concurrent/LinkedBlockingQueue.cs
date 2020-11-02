using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Pattern;
using Akka.Remote.Artery.Internal;
using Akka.Streams;
using Akka.Util;

namespace Akka.Remote.Artery.Utils.Concurrent
{
    /// <summary>
    /// An optionally-bounded {@linkplain BlockingQueue blocking queue} based on
    /// linked nodes.
    /// This queue orders elements FIFO (first-in-first-out).
    /// The <em>head</em> of the queue is that element that has been on the
    /// queue the longest time.
    /// The <em>tail</em> of the queue is that element that has been on the
    /// queue the shortest time. New elements
    /// are inserted at the tail of the queue, and the queue retrieval
    /// operations obtain elements at the head of the queue.
    /// Linked queues typically have higher throughput than array-based queues but
    /// less predictable performance in most concurrent applications.
    ///
    /// <p>The optional capacity bound constructor argument serves as a
    /// way to prevent excessive queue expansion. The capacity, if unspecified,
    /// is equal to {@link Integer#MAX_VALUE}.  Linked nodes are
    /// dynamically created upon each insertion unless this would bring the
    /// queue above capacity.</p>
    ///
    /// <p>This class and its iterator implement all of the <em>optional</em>
    /// methods of the {@link Collection} and {@link Iterator} interfaces.</p>
    /// </summary>
    /// <typeparam name="T">the type of elements held in this queue</typeparam>
    public class LinkedBlockingQueue<T> : AbstractQueue<T>, IBlockingQueue<T> where T : class
    {
        /*
         * A variant of the "two lock queue" algorithm.  The putLock gates
         * entry to put (and offer), and has an associated condition for
         * waiting puts.  Similarly for the takeLock.  The "count" field
         * that they both rely on is maintained as an atomic to avoid
         * needing to get both locks in most cases. Also, to minimize need
         * for puts to get takeLock and vice-versa, cascading notifies are
         * used. When a put notices that it has enabled at least one take,
         * it signals taker. That taker in turn signals others if more
         * items have been entered since the signal. And symmetrically for
         * takes signalling puts. Operations such as remove(Object) and
         * iterators acquire both locks.
         *
         * Visibility between writers and readers is provided as follows:
         *
         * Whenever an element is enqueued, the putLock is acquired and
         * count updated.  A subsequent reader guarantees visibility to the
         * enqueued Node by either acquiring the putLock (via fullyLock)
         * or by acquiring the takeLock, and then reading n = count.get();
         * this gives visibility to the first n items.
         *
         * To implement weakly consistent iterators, it appears we need to
         * keep all Nodes GC-reachable from a predecessor dequeued Node.
         * That would cause two problems:
         * - allow a rogue Iterator to cause unbounded memory retention
         * - cause cross-generational linking of old Nodes to new Nodes if
         *   a Node was tenured while live, which generational GCs have a
         *   hard time dealing with, causing repeated major collections.
         * However, only non-deleted Nodes need to be reachable from
         * dequeued Nodes, and reachability does not necessarily have to
         * be of the kind understood by the GC.  We use the trick of
         * linking a Node that has just been dequeued to itself.  Such a
         * self-link implicitly means to advance to head.next.
         */

        /// <summary>
        /// Linked list node class.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal class Node<T>
        {
            public T Item;

            /// <summary>
            /// One of:
            /// - the real successor Node
            /// - this Node, meaning the successor is head.next
            /// - null, meaning there is no successor (this is the last node)
            /// </summary>
            public Node<T> Next;

            public Node(T x)
            {
                Item = x;
            }
        }

        /** The capacity bound, or Integer.MAX_VALUE if none */
        private readonly int _capacity;

        /** Current number of elements */
        private readonly AtomicInt _count = new AtomicInt();

        private readonly AtomicBoolean _dirty = new AtomicBoolean();

        /**
         * Head of linked list.
         * Invariant: head.item == null
         */
        private Node<T> _head;

        /**
         * Tail of linked list.
         * Invariant: last.next == null
         */
        private Node<T> _last;

        /** Lock held by take, poll, etc */
        private readonly object _takeLock = new object();

        /** Wait queue for waiting takes */
        private readonly object _notEmpty = new object();

        /** Lock held by put, offer, etc */
        private readonly object _putLock = new object();

        /** Wait queue for waiting puts */
        private readonly object _notFull = new object();

        /// <summary>
        /// Signals a waiting take. Called only from put/offer (which do not
        /// otherwise ordinarily lock takeLock.)
        /// </summary>
        private void SignalNotEmpty()
        {
            var takeLock = _takeLock;
            lock (takeLock)
            {
                Monitor.Pulse(_notEmpty);
            }
        }

        /// <summary>
        /// Signals a waiting put. Called only from take/poll.
        /// </summary>
        private void SignalNotFull()
        {
            var putLock = _putLock;
            lock (putLock)
            {
                Monitor.Pulse(_notFull);
            }
        }

        /// <summary>
        /// Links node at end of queue.
        /// </summary>
        /// <param name="node"></param>
        private void Enqueue(Node<T> node)
        {
#if DEBUG
            Debug.Assert(Monitor.IsEntered(_takeLock));
            Debug.Assert(_head.Item is null);
#endif
            _dirty.GetAndSet(true);
            _last = _last.Next = node;
        }

        /// <summary>
        /// Removes a node from head of queue.
        /// </summary>
        /// <returns>
        /// the node
        /// </returns>
        private T Dequeue()
        {
#if DEBUG
            Debug.Assert(Monitor.IsEntered(_takeLock));
            Debug.Assert(_head.Item is null);
#endif
            _dirty.GetAndSet(true);
            var h = _head;
            var first = h.Next;
            h.Next = h;
            _head = first;
            var x = first.Item;
            first.Item = null;
            return x;
        }

        /// <summary>
        /// Creates a {@code LinkedBlockingQueue} with a capacity of {@link Integer#MAX_VALUE}.
        /// </summary>
        public LinkedBlockingQueue() : this(int.MaxValue)
        { }

        /// <summary>
        /// Creates a {@code LinkedBlockingQueue} with the given (fixed) capacity.
        /// </summary>
        /// <param name="capacity"></param>
        
        public LinkedBlockingQueue(int capacity)
        {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be greater than 0");
            _capacity = capacity;

            _last = _head = new Node<T>(null);
        }

        /// <summary>
        /// Creates a {@code LinkedBlockingQueue} with a capacity of
        /// {@link Integer#MAX_VALUE}, initially containing the elements of the
        /// given collection,
        /// added in traversal order of the collection's iterator.
        /// </summary>
        /// <param name="c"></param>
        public LinkedBlockingQueue(IEnumerable<T> c):this(int.MaxValue)
        {
            if(c is null)
                throw new ArgumentNullException(nameof(c));

            var putLock = _putLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(putLock, ref lockTaken); // Never contended, but necessary for visibility
                var n = 0;
                foreach (var e in c)
                {
                    if (e is null)
                        throw new NullReferenceException();
                    if (n == _capacity)
                        throw new IllegalStateException("Queue full");
                    Enqueue(new Node<T>(e));
                    ++n;
                }

                _count.GetAndSet(n);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(putLock);
            }
        }

        // this doc comment is overridden to remove the reference to collections
        // greater in size than Integer.MAX_VALUE
        /// <summary>
        /// Returns the number of elements in this queue.
        /// </summary>
        public override int Count => _count.Value;

        // this doc comment is a modified copy of the inherited doc comment,
        // without the reference to unlimited queues.
        /// <summary>
        /// Returns the number of additional elements that this queue can ideally
        /// (in the absence of memory or resource constraints) accept without
        /// blocking. This is always equal to the initial capacity of this queue
        /// less the current {@code size} of this queue.
        ///
        /// <p>Note that you <em>cannot</em> always tell if an attempt to insert
        /// an element will succeed by inspecting {@code remainingCapacity}
        /// because it may be the case that another thread is about to
        /// insert or remove an element.</p>
        /// </summary>
        public int RemainingCapacity => _capacity - _count.Value;

        /// <summary>
        /// Inserts the specified element at the tail of this queue, waiting if
        /// necessary for space to become available.
        /// </summary>
        /// <param name="e"></param>
        public void Put(T e)
        {
            if(e is null)
                throw new ArgumentNullException(nameof(e));

            int c;
            var node = new Node<T>(e);
            var putLock = _putLock;
            var lockTaken = false;
            var count = _count;
            try
            {
                /*
                 * Note that count is used in wait guard even though it is
                 * not protected by lock. This works because count can
                 * only decrease at this point (all other puts are shut
                 * out by lock), and we (or some other waiting put) are
                 * signaled if it ever changes from capacity. Similarly
                 * for all other uses of count in other wait guards.
                 */
                Monitor.Enter(putLock, ref lockTaken);
                while (count.Value == _capacity)
                {
                    Monitor.Wait(_notFull);
                }
                Enqueue(node);
                c = count.GetAndIncrement();
                if(c + 1 < _capacity)
                    Monitor.Pulse(_notFull);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(putLock);
            }
            if(c == 0)
                SignalNotEmpty();
        }

        /// <summary>
        /// Inserts the specified element at the tail of this queue, waiting if
        /// necessary up to the specified wait time for space to become available.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="timeout"></param>
        /// <returns>
        /// {@code true} if successful, or {@code false} if
        /// the specified waiting time elapses before space is available
        /// </returns>
        public bool Offer(T e, TimeSpan timeout)
        {
            if(e is null)
                throw new ArgumentNullException(nameof(e));

            int c;
            var node = new Node<T>(e);
            var putLock = _putLock;
            var lockTaken = false;
            var count = _count;
            try
            {
                Monitor.Enter(putLock, ref lockTaken);
                while (count.Value == _capacity)
                {
                    if (!Monitor.Wait(_notFull, timeout))
                        return false;
                }
                Enqueue(node);
                c = count.GetAndIncrement();
                if(c + 1 < _capacity)
                    Monitor.Pulse(_notFull);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(putLock);
            }
            if(c == 0)
                SignalNotEmpty();
            return true;
        }

        /// <summary>
        /// Inserts the specified element at the tail of this queue if it is
        /// possible to do so immediately without exceeding the queue's capacity,
        /// returning {@code true} upon success and {@code false} if this queue
        /// is full.
        /// When using a capacity-restricted queue, this method is generally
        /// preferable to method {@link BlockingQueue#add add}, which can fail to
        /// insert an element only by throwing an exception.
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public override bool Offer(T e)
        {
            if(e is null)
                throw new ArgumentNullException(nameof(e));

            var count = _count;
            if (count.Value == _capacity)
                return false;
            int c;
            var node = new Node<T>(e);
            var putLock = _putLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(putLock, ref lockTaken);
                if (count.Value == _capacity)
                    return false;
                Enqueue(node);
                c = count.GetAndIncrement();
                if (c + 1 < _capacity)
                    Monitor.Pulse(_notFull);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(putLock);
            }
            if(c == 0)
                SignalNotEmpty();
            return true;
        }

        ///<inheritdoc/>
        public T Take()
        {
            T x;
            int c;
            var count = _count;
            var takeLock = _takeLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(takeLock, ref lockTaken);
                while (count.Value == 0)
                {
                    Monitor.Wait(_notEmpty);
                }

                x = Dequeue();
                c = count.GetAndDecrement();
                if (c > 1)
                    Monitor.Pulse(_notEmpty);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(takeLock);
            }
            if(c == _capacity)
                SignalNotFull();
            return x;
        }

        ///<inheritdoc/>
        public T Poll(TimeSpan timeout)
        {
            T x;
            int c;
            var count = _count;
            var takeLock = _takeLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(takeLock, ref lockTaken);
                while (count.Value == 0)
                {
                    if (!Monitor.Wait(_notEmpty, timeout))
                        return null;
                }

                x = Dequeue();
                c = count.GetAndDecrement();
                if (c > 1)
                    Monitor.Pulse(_notEmpty);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(takeLock);
            }
            if(c == _capacity)
                SignalNotFull();
            return x;
        }

        ///<inheritdoc/>
        public override T Poll()
        {
            var count = _count;
            if (count.Value == 0)
                return null;
            T x;
            int c;
            var takeLock = _takeLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(takeLock, ref lockTaken);
                if (count.Value == 0)
                    return null;
                x = Dequeue();
                c = count.GetAndDecrement();
                if (c > 1)
                    Monitor.Pulse(_notEmpty);
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(takeLock);
            }
            if(c == _capacity)
                SignalNotFull();
            return x;
        }

        ///<inheritdoc/>
        public override T Peek()
        {
            var count = _count;
            if (count.Value == 0)
                return null;
            var takeLock = _takeLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(takeLock, ref lockTaken);
                return count.Value > 0 ? _head.Next.Item : null;
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(takeLock);
            }
        }

        /// <summary>
        /// Unlinks interior Node p with predecessor pred.
        /// </summary>
        /// <param name="p"></param>
        /// <param name="pred"></param>
        private void Unlink(Node<T> p, Node<T> pred)
        {
#if DEBUG
            Debug.Assert(Monitor.IsEntered(_putLock));
            Debug.Assert(Monitor.IsEntered(_takeLock));
#endif
            // p.next is not changed, to allow iterators that are
            // traversing p to maintain their weak-consistency guarantee.
            p.Item = null;
            pred.Next = p.Next;
            if (_last == p)
                _last = pred;
            if(_count.GetAndDecrement() == _capacity)
                Monitor.Pulse(_notFull);
        }

        /// <summary>
        /// Removes a single instance of the specified element from this queue,
        /// if it is present.  More formally, removes an element {@code e} such
        /// that {@code o.equals(e)}, if this queue contains one or more such
        /// elements.
        /// Returns {@code true} if this queue contained the specified element
        /// (or equivalently, if this queue changed as a result of the call).
        /// </summary>
        /// <param name="item">element to be removed from this queue, if present</param>
        /// <returns>
        /// {@code true} if this queue changed as a result of the call
        /// </returns>
        public override bool Remove(T item)
        {
            if (item is null)
                return false;

            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                Node<T> pred, p;
                for (pred = _head, p = pred.Next;
                    p != null;
                    pred = p, p = p.Next)
                {
                    if (item.Equals(p.Item))
                    {
                        Unlink(p, pred);
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                // fully unlock
                if(putLocked)
                    Monitor.Exit(putLock);
                if(takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        /// <summary>
        /// Returns {@code true} if this queue contains the specified element.
        /// More formally, returns {@code true} if and only if this queue contains
        /// at least one element {@code e} such that {@code o.equals(e)}.
        /// </summary>
        /// <param name="item">object to be checked for containment in this queue</param>
        /// <returns>{@code true} if this queue contains the specified element</returns>
        public override bool Contains(T item)
        {
            if (item is null)
                return false;

            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                for (var p = _head.Next; p != null; p = p.Next)
                {
                    if (item.Equals(p.Item))
                        return true;
                }
                return false;
            }
            finally
            {
                // fully unlock
                if (putLocked)
                    Monitor.Exit(putLock);
                if (takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        /// <summary>
        /// Returns an array containing all of the elements in this queue, in
        /// proper sequence.
        ///
        /// <p>The returned array will be "safe" in that no references to it are
        /// maintained by this queue.  (In other words, this method must allocate
        /// a new array).  The caller is thus free to modify the returned array.</p>
        ///
        /// <p>This method acts as bridge between array-based and collection-based
        /// APIs.</p>
        /// </summary>
        /// <returns>an array containing all of the elements in this queue</returns>
        public override T[] ToArray()
        {
            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                var size = _count.Value;
                var a = new T[size];
                var k = 0;
                for (var p = _head.Next; p != null; p = p.Next)
                {
                    a[k++] = p.Item;
                }
                return a;
            }
            finally
            {
                // fully unlock
                if (putLocked)
                    Monitor.Exit(putLock);
                if (takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        /// <summary>
        /// Returns an array containing all of the elements in this queue, in
        /// proper sequence; the runtime type of the returned array is that of
        /// the specified array.  If the queue fits in the specified array, it
        /// is returned therein.  Otherwise, a new array is allocated with the
        /// runtime type of the specified array and the size of this queue.
        ///
        /// <p>If this queue fits in the specified array with room to spare
        /// (i.e., the array has more elements than this queue), the element in
        /// the array immediately following the end of the queue is set to
        /// {@code null}.</p>
        ///
        /// <p>Like the {@link #toArray()} method, this method acts as bridge between
        /// array-based and collection-based APIs.  Further, this method allows
        /// precise control over the runtime type of the output array, and may,
        /// under certain circumstances, be used to save allocation costs.</p>
        ///
        /// Note that {@code toArray(new Object[0])} is identical in function to
        /// {@code toArray()}.
        /// </summary>
        /// <param name="a">
        /// the array into which the elements of the queue are to
        /// be stored, if it is big enough; otherwise, a new array of the
        /// same runtime type is allocated for this purpose
        /// </param>
        /// <returns>
        /// an array containing all of the elements in this queue
        /// </returns>
        public override T[] ToArray(T[] a)
        {
            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                var size = _count.Value;
                if(a is null || a.Length < size)
                    a = new T[size];

                var k = 0;
                for (var p = _head.Next; p != null; p = p.Next)
                {
                    a[k++] = p.Item;
                }

                if (a.Length > k)
                    a[k] = null;
                return a;
            }
            finally
            {
                // fully unlock
                if (putLocked)
                    Monitor.Exit(putLock);
                if (takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        public override string ToString()
        {
            var a = ToArray();
            return a.Length == 0 ? "[]" : $"[{string.Join(", ", a.Select(i => i.ToString()))}]";
        }

        /// <summary>
        /// Atomically removes all of the elements from this queue.
        /// The queue will be empty after this call returns.
        /// </summary>
        public override void Clear()
        {
            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                Node<T> p, h;
                for (h = _head; (p = h.Next) != null; h = p)
                {
                    h.Next = h;
                    p.Item = null;
                }

                _head = _last;
#if DEBUG
                Debug.Assert(_head.Item == null && _head.Next == null);
#endif
                if(_count.GetAndSet(0) == _capacity)
                    Monitor.Pulse(_notFull);
            }
            finally
            {
                // fully unlock
                if (putLocked)
                    Monitor.Exit(putLock);
                if (takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        public int DrainTo(ICollection<T> c)
            => DrainTo(c, int.MaxValue);

        public int DrainTo(ICollection<T> c, int maxElements)
        {
            if(c is null)
                throw new ArgumentNullException(nameof(c));
            if(ReferenceEquals(c, this))
                throw new IllegalArgumentException();
            if (maxElements <= 0)
                return 0;

            var signalNotFull = false;
            var takeLock = _takeLock;
            var lockTaken = false;
            try
            {
                Monitor.Enter(takeLock, ref lockTaken);

                var n = Math.Min(maxElements, _count.Value);
                // _count.Value provides visibility to first n Node
                var h = _head;
                var i = 0;
                try
                {
                    while (i < n)
                    {
                        var p = h.Next;
                        c.Add(p.Item);
                        p.Item = null;
                        h.Next = h;
                        h = p;
                        ++i;
                    }
                    return n;
                }
                finally
                {
                    // Restore invariants even if c.add() threw
                    if (i > 0)
                    {
#if DEBUG
                        Debug.Assert(h.Item == null);
#endif
                        _head = h;
                        signalNotFull = _count.GetAndAdd(-i) == _capacity;
                    }
                }
            }
            finally
            {
                if(lockTaken)
                    Monitor.Exit(takeLock);
                if(signalNotFull)
                    SignalNotFull();
            }
        }

        public override IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        public override bool IsReadOnly => true;

        public override void CopyTo(T[] array, int arrayIndex)
        {
            if(array is null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex must be greater or equal to zero.");

            var putLock = _putLock;
            var takeLock = _takeLock;
            var putLocked = false;
            var takeLocked = false;
            try
            {
                // fully lock
                Monitor.Enter(putLock, ref putLocked);
                Monitor.Enter(takeLock, ref takeLocked);

                if(array.Length - arrayIndex < _count.Value)
                    throw new IndexOutOfRangeException("Array does not have enough elements to hold a copy of queue.");

                for (var p = _head.Next; p != null; p = p.Next)
                {
                    array[arrayIndex++] = p.Item;
                }
            }
            finally
            {
                // fully unlock
                if (putLocked)
                    Monitor.Exit(putLock);
                if (takeLocked)
                    Monitor.Exit(takeLock);
            }
        }

        public sealed class Enumerator : IEnumerator<T>
        {
            private readonly LinkedBlockingQueue<T> _parent;

            private Node<T> _current;

            internal Enumerator(LinkedBlockingQueue<T> parent)
            {
                parent._dirty.GetAndSet(false);
                _parent = parent;
                _current = _parent._head;
            }

            public bool MoveNext()
            {
                if(_parent._dirty)
                    throw new InvalidOperationException("LinkedBlockingQueue was modified.");

                if (_current.Next is null)
                    return false;

                var putLock = _parent._putLock;
                var takeLock = _parent._takeLock;
                var putLocked = false;
                var takeLocked = false;

                try
                {
                    // fully lock
                    Monitor.Enter(putLock, ref putLocked);
                    Monitor.Enter(takeLock, ref takeLocked);

                    _current = _current.Next;
                    Current = _current.Item;
                }
                finally
                {
                    // fully unlock
                    if (putLocked)
                        Monitor.Exit(putLock);
                    if (takeLocked)
                        Monitor.Exit(takeLock);
                }

                return true;
            }

            public void Reset()
            {
                if (_parent._dirty)
                    throw new InvalidOperationException("LinkedBlockingQueue was modified.");

                _current = _parent._head;
                Current = _current.Item;
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                _current = null;
                Current = null;
            }
        }
    }
}
