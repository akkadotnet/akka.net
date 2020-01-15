//-----------------------------------------------------------------------
// <copyright file="Buffers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Akka.Annotations;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal interface IBuffer<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        int Capacity { get; }
        /// <summary>
        /// TBD
        /// </summary>
        int Used { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsFull { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsEmpty { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool NonEmpty { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        void Enqueue(T element);
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        T Dequeue();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        T Peek();
        /// <summary>
        /// TBD
        /// </summary>
        void Clear();
        /// <summary>
        /// TBD
        /// </summary>
        void DropHead();
        /// <summary>
        /// TBD
        /// </summary>
        void DropTail();
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class Buffer
    {
        private const int FixedQueueSize = 128;

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="size">TBD</param>
        /// <param name="settings">TBD</param>
        /// <returns>TBD</returns>
        public static IBuffer<T> Create<T>(int size, ActorMaterializerSettings settings) 
            => Create<T>(size, settings.MaxFixedBufferSize);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="size">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public static IBuffer<T> Create<T>(int size, IMaterializer materializer)
        {
            var m = materializer as ActorMaterializer;
            return Create<T>(size, m?.Settings.MaxFixedBufferSize ?? 1000000000);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="size">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public static IBuffer<T> Create<T>(int size, int max)
        {
            if (size < FixedQueueSize || size < max)
                return FixedSizeBuffer.Create<T>(size);

            return new BoundedBuffer<T>(size);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class FixedSizeBuffer 
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// Returns a fixed size buffer backed by an array. The buffer implementation DOES NOT check against overflow or
        /// underflow, it is the responsibility of the user to track or check the capacity of the buffer before enqueueing
        /// dequeueing or dropping.
        /// 
        /// Returns a specialized instance for power-of-two sized buffers.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="size">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="size"/> is less than 1.
        /// </exception>
        /// <returns>TBD</returns>
        [InternalApi]
        public static FixedSizeBuffer<T> Create<T>(int size)
        {
            if (size < 1)
                throw new ArgumentException("buffer size must be positive");
            if (((size - 1) & size) == 0)
                return new PowerOfTwoFixedSizeBuffer<T>(size);
            return new ModuloFixedSizeBuffer<T>(size);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal abstract class FixedSizeBuffer<T> : IBuffer<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected long ReadIndex;
        /// <summary>
        /// TBD
        /// </summary>
        protected long WriteIndex;

        private readonly T[] _buffer;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        protected FixedSizeBuffer(int capacity)
        {
            Capacity = capacity;
            _buffer = new T[capacity];
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Capacity { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public int Used => (int)(WriteIndex - ReadIndex);
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsFull => Used == Capacity;
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty => Used == 0;
        /// <summary>
        /// TBD
        /// </summary>
        public bool NonEmpty => Used != 0;

        public long RemainingCapacity => Capacity - Used;

        // for the maintenance parameter see dropHead
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <param name="maintenance">TBD</param>
        /// <returns>TBD</returns>
        protected abstract int ToOffset(long index, bool maintenance);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void Enqueue(T element)
        {
            Put(WriteIndex, element, false);
            WriteIndex++;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <param name="element">TBD</param>
        /// <param name="maintenance">TBD</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Put(long index, T element, bool maintenance) => _buffer[ToOffset(index, maintenance)] = element;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <returns>TBD</returns>
        public T Get(long index) => _buffer[ToOffset(index, false)];

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public T Peek() => Get(ReadIndex);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public T Dequeue()
        {
            var result = Get(ReadIndex);
            DropHead();
            return result;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Clear()
        {
            _buffer.Initialize();
            ReadIndex = 0;
            WriteIndex = 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void DropHead()
        {
            // this is the only place where readIdx is advanced, so give ModuloFixedSizeBuffer
            // a chance to prevent its fatal wrap-around
            Put(ReadIndex, default(T), true);
            ReadIndex++;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void DropTail()
        {
            WriteIndex--;
            Put(WriteIndex, default(T), false);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class ModuloFixedSizeBuffer<T> : FixedSizeBuffer<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="size">TBD</param>
        public ModuloFixedSizeBuffer(int size) : base(size)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <param name="maintenance">TBD</param>
        /// <returns>TBD</returns>
        protected override int ToOffset(long index, bool maintenance)
        {
            if (maintenance && ReadIndex > int.MaxValue)
            {
                // In order to be able to run perpetually we must ensure that the counters
                // donâ€™t overrun into negative territory, so set them back by as many multiples
                // of the capacity as possible when both are above Int.MaxValue.
                var shift = int.MaxValue - (int.MaxValue % Capacity);
                ReadIndex -= shift;
                WriteIndex -= shift;
            }

            return (int)(index % Capacity);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class PowerOfTwoFixedSizeBuffer<T> : FixedSizeBuffer<T> 
    {
        private readonly int _mask;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="size">TBD</param>
        public PowerOfTwoFixedSizeBuffer(int size) : base(size)
        {
            _mask = Capacity - 1;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <param name="maintenance">TBD</param>
        /// <returns>TBD</returns>
        protected override int ToOffset(long index, bool maintenance) => (int)index & _mask;
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        public BoundedBuffer(int capacity)
        {
            Capacity = capacity;
            _q = new FixedQueue(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Used => _q.Used;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsFull => _q.IsFull;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty => _q.IsEmpty;

        /// <summary>
        /// TBD
        /// </summary>
        public bool NonEmpty => _q.NonEmpty;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void Enqueue(T element) => _q.Enqueue(element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public T Dequeue() => _q.Dequeue();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public T Peek() => _q.Peek();

        /// <summary>
        /// TBD
        /// </summary>
        public void Clear() => _q.Clear();

        /// <summary>
        /// TBD
        /// </summary>
        public void DropHead() => _q.DropHead();

        /// <summary>
        /// TBD
        /// </summary>
        public void DropTail() => _q.DropTail();
    }

}
