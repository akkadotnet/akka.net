using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Akka.Remote.Artery
{
    // ARTERY: This is a very rudimentary implementation of a pool, need to check to see if this is enough.
    internal class ObjectPool<T>
    {
        public int Capacity { get; }
        public Func<T> Create { get; }
        public Action<T> Clear { get; }

        private readonly ConcurrentQueue<T> _storage = new ConcurrentQueue<T>();

        public ObjectPool(int capacity, Func<T> create, Action<T> clear)
        {
            Capacity = capacity;
            Create = create;
            Clear = clear;
        }

        public T Acquire()
        {
            if (_storage.TryDequeue(out var obj))
                return obj;
            obj = Create();
            return obj;
        }

        public bool Release(T obj)
        {
            if (_storage.Count == Capacity) return false;

            Clear(obj);
            _storage.Enqueue(obj);
            return true;
        }
    }
}
