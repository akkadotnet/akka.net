using System.Collections.Generic;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IBuffer<T> where T : class
    {
        int Used { get; }
        bool IsFull { get; }
        bool IsEmpty { get; }
        bool NonEmpty { get; }

        void Enqueue(T element);
        T Dequeue();

        T Peek();
        void Clear();
        void DropHead();
        void DropTail();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class BoundedBuffer<T> : IBuffer<T> where T : class
    {
        #region internal classes

        private sealed class FixedQueue : IBuffer<T>
        {
            private const int Size = 16;
            private const int Mask = 15;

            private readonly int _capacity;
            private readonly T[] _queue  = new T[Size];
            private readonly BoundedBuffer<T> _boundedBuffer;
            private int _head;
            private int _tail;

            public FixedQueue(int capacity, BoundedBuffer<T> boundedBuffer)
            {
                _capacity = capacity;
                _boundedBuffer = boundedBuffer;
            }

            public int Used => _tail - _head;
            public bool IsFull => Used == _capacity;
            public bool IsEmpty => _tail == _head;
            public bool NonEmpty => !IsEmpty;

            public void Enqueue(T element)
            {
                if (_tail - _head == Size)
                {
                    var queue = new DynamicQueue(_head);
                    while (NonEmpty)
                        queue.Enqueue(Dequeue());
                    queue.Enqueue(element);
                    _boundedBuffer._q = queue;
                }
                else
                {
                    _queue[_tail & Mask] = element;
                    _tail += 1;
                }
            }

            public T Dequeue()
            {
                var pos = _head & Mask;
                var ret = _queue[pos];
                _queue[pos] = null;
                _head += 1;
                return ret;
            }

            public T Peek() => _tail == _head ? null : _queue[_head & Mask];

            public void Clear()
            {
                while (NonEmpty)
                    Dequeue();
            }

            public void DropHead() => Dequeue();

            public void DropTail()
            {
                _tail -= 1;
                _queue[_tail & Mask] = null;
            }
        }

        private sealed class DynamicQueue : LinkedList<T>, IBuffer<T>
        {
            private readonly int _capacity;

            public DynamicQueue(int capacity)
            {
                _capacity = capacity;
            }

            public int Used => Count;
            public bool IsFull => Count == _capacity;
            public bool IsEmpty => Count == 0;
            public bool NonEmpty => !IsEmpty;

            public void Enqueue(T element) => AddLast(element);

            public T Dequeue()
            {
                var result = First.Value;
                RemoveFirst();
                return result;
            }

            public T Peek() => First.Value;

            public void DropHead() => RemoveFirst();

            public void DropTail() => RemoveLast();
        }

        #endregion
        
        private IBuffer<T> _q;

        public BoundedBuffer(int capacity)
        {
            _q = new FixedQueue(capacity, this);
        }

        public int Used => _q.Used;

        public bool IsFull => _q.IsFull;

        public bool IsEmpty => _q.IsEmpty;

        public bool NonEmpty => _q.NonEmpty;

        public void Enqueue(T element) => _q.Enqueue(element);

        public T Dequeue() => _q.Dequeue();

        public T Peek() => _q.Peek();

        public void Clear() => _q.Clear();

        public void DropHead() => _q.DropHead();

        public void DropTail() => _q.DropTail();
    }
}
