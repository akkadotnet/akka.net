using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IBuffer<T>
    {
        int Capacity { get; }
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

    internal static class Buffer
    {
        private const int FixedQueueSize = 128;
        private const int FixedQueueMask = 127;

        public static IBuffer<T> Create<T>(int size, ActorMaterializerSettings settings) 
            => Create<T>(size, settings.MaxFixedBufferSize);

        public static IBuffer<T> Create<T>(int size, IMaterializer materializer)
        {
            var m = materializer as ActorMaterializer;
            return Create<T>(size, m != null ? m.Settings.MaxFixedBufferSize : 1000000000);
        }

        public static IBuffer<T> Create<T>(int size, int max)
        {
            if (size < FixedQueueSize || size < max)
                return FixedSizeBuffer.Create<T>(size);

            return new BoundedBuffer<T>(size);
        }
    }

    internal static class FixedSizeBuffer 
    {
        /**
         * INTERNAL API
         *
         * Returns a fixed size buffer backed by an array. The buffer implementation DOES NOT check agains overflow or
         * underflow, it is the responsibility of the user to track or check the capacity of the buffer before enqueueing
         * dequeueing or dropping.
         *
         * Returns a specialized instance for power-of-two sized buffers.
         */
        public static FixedSizeBuffer<T> Create<T>(int size)
        {
            if (size < 1) throw new ArgumentException("buffer size must be positive");
            if (((size - 1) & size) == 0)
                return new PowerOfTwoFixedSizeBuffer<T>(size);
            return new ModuloFixedSizeBuffer<T>(size);
        }
    }

    internal abstract class FixedSizeBuffer<T> : IBuffer<T>
    {
        protected long ReadIndex = 0L;
        protected long WriteIndex = 0L;

        private readonly T[] _buffer;

        protected FixedSizeBuffer(int capacity)
        {
            Capacity = capacity;
            _buffer = new T[capacity];
        }

        public int Capacity { get; }
        public int Used => (int)(WriteIndex - ReadIndex);
        public bool IsFull => Used == Capacity;
        public bool IsEmpty => Used == 0;
        public bool NonEmpty => Used != 0;

        // for the maintenance parameter see dropHead
        protected abstract int ToOffset(long index, bool maintenance);

        public void Enqueue(T element)
        {
            Put(WriteIndex, element, false);
            WriteIndex++;
        }

        public void Put(long index, T element, bool maintenance)
        {
            _buffer[ToOffset(index, maintenance)] = element;
        }

        public T Get(long index)
        {
            return _buffer[ToOffset(index, false)];
        }

        public T Peek()
        {
            return Get(ReadIndex);
        }

        public T Dequeue()
        {
            var result = Get(ReadIndex);
            DropHead();
            return result;
        }

        public void Clear()
        {
            _buffer.Initialize();
            ReadIndex = 0;
            WriteIndex = 0;
        }
        
        public void DropHead()
        {
            // this is the only place where readIdx is advanced, so give ModuloFixedSizeBuffer
            // a chance to prevent its fatal wrap-around
            Put(ReadIndex, default(T), true);
            ReadIndex++;
        }

        public void DropTail()
        {
            WriteIndex--;
            Put(WriteIndex, default(T), false);
        }
    }

    internal class ModuloFixedSizeBuffer<T> : FixedSizeBuffer<T>
    {
        public ModuloFixedSizeBuffer(int size) : base(size)
        {
        }

        protected override int ToOffset(long index, bool maintenance)
        {
            if (maintenance && ReadIndex > Int32.MaxValue)
            {
                // In order to be able to run perpetually we must ensure that the counters
                // don’t overrun into negative territory, so set them back by as many multiples
                // of the capacity as possible when both are above Int.MaxValue.
                var shift = Int32.MaxValue - (Int32.MaxValue % Capacity);
                ReadIndex -= shift;
                WriteIndex -= shift;
            }

            return (int)(index % Capacity);
        }
    }

    internal class PowerOfTwoFixedSizeBuffer<T> : FixedSizeBuffer<T> 
    {
        private readonly int _mask;
        public PowerOfTwoFixedSizeBuffer(int size) : base(size)
        {
            _mask = Capacity - 1;
        }

        protected override int ToOffset(long index, bool maintenance)
        {
            return (int)index & _mask;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class BoundedBuffer<T> : IBuffer<T>
    {
        #region internal classes

        private sealed class FixedQueue : IBuffer<T>
        {
            private const int Size = 16;
            private const int Mask = 15;
            
            private readonly T[] _queue = new T[Size];
            private readonly BoundedBuffer<T> _boundedBuffer;
            private int _head;
            private int _tail;

            public FixedQueue(BoundedBuffer<T> boundedBuffer)
            {
                _boundedBuffer = boundedBuffer;
                Capacity = boundedBuffer.Capacity;
            }

            public int Capacity { get; }
            public int Used => _tail - _head;
            public bool IsFull => Used == Capacity;
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
                _queue[pos] = default(T);
                _head += 1;
                return ret;
            }

            public T Peek() => _tail == _head ? default(T) : _queue[_head & Mask];

            public void Clear()
            {
                while (NonEmpty)
                    Dequeue();
            }

            public void DropHead() => Dequeue();

            public void DropTail()
            {
                _tail -= 1;
                _queue[_tail & Mask] = default(T);
            }
        }

        private sealed class DynamicQueue : LinkedList<T>, IBuffer<T>
        {
            public DynamicQueue(int capacity)
            {
                Capacity = capacity;
            }

            public int Capacity { get; }
            public int Used => Count;
            public bool IsFull => Count == Capacity;
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
            Capacity = capacity;
            _q = new FixedQueue(this);
        }

        public int Capacity { get; }

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
