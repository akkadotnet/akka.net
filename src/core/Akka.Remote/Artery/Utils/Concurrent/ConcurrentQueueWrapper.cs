using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Remote.Artery.Utils.Concurrent
{
    public class ConcurrentQueueWrapper<T> : AbstractQueue<T> where T: class
    {
        private readonly ConcurrentQueue<T> _backingQueue;
        private readonly int _capacity;

        public ConcurrentQueueWrapper(): this(new ConcurrentQueue<T>(), int.MaxValue)
        { }

        public ConcurrentQueueWrapper(int capacity) : this(new ConcurrentQueue<T>(), capacity)
        { }

        public ConcurrentQueueWrapper(IEnumerable<T> items): this(new ConcurrentQueue<T>(items), int.MaxValue)
        { }

        public ConcurrentQueueWrapper(ConcurrentQueue<T> backing, int capacity)
        {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be greater than 0");
            _capacity = capacity;

            _backingQueue = backing;
        }
        

        /// <inheritdoc/>
        public override bool Offer(T e)
        { 
            _backingQueue.Enqueue(e);
            return true;
        }

        /// <inheritdoc/>
        public override T Poll() => _backingQueue.TryDequeue(out var result) ? result : default;

        /// <inheritdoc/>
        public override T Peek()
            => _backingQueue.TryPeek(out var result) ? result : default;

        /// <inheritdoc/>
        public override T[] ToArray()
            => _backingQueue.ToArray();

        /// <inheritdoc/>
        public override T[] ToArray(T[] a)
        {
            if (a.Length < _backingQueue.Count)
                a = new T[_backingQueue.Count];

            var index = 0;
            foreach (var item in _backingQueue)
            {
                a[index++] = item;
            }

            if (a.Length > _backingQueue.Count)
                a[index] = null;

            return a;
        }

        /// <inheritdoc/>
        public override void Clear()
        {
            while(_backingQueue.TryDequeue(out _))
            { }
        }

        /// <inheritdoc/>
        public override bool Contains(T item)
            => _backingQueue.Contains(item);

        /// <inheritdoc/>
        public override void CopyTo(T[] array, int arrayIndex)
        {
            if(array is null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex must be greater or equal to zero.");
            if(array.Length - arrayIndex < _backingQueue.Count)
                throw new IndexOutOfRangeException("Array does not have enough elements to hold a copy of queue.");


        }

        /// <inheritdoc/>
        public override bool Remove(T item)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override int Count => _backingQueue.Count;

        /// <inheritdoc/>
        public override bool IsReadOnly { get; } = false;

        public override IEnumerator<T> GetEnumerator()
            => _backingQueue.GetEnumerator();

    }
}
