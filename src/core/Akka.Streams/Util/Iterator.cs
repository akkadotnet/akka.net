using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.Streams.Util {
    public abstract class Iterator
    {
        public static IEnumerator<T> Continually<T>(Func<T> getNext)
        {
            return new InfiniteIterator<T>(getNext);
        }
    }

    /// <summary>
    /// Iterator to support scala Iterator.Continually type of iterators
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class InfiniteIterator<T> : IEnumerator<T>
    {
        private readonly Func<T> getNext;
        private bool disposed = false;

        public InfiniteIterator(Func<T> getNext)
        {
            this.getNext = getNext;
            Current = default(T);
        }

        public void Dispose()
        {
            Current = default(T);
            disposed = true;
        }

        public bool MoveNext()
        {
            if (!disposed)
                Current = getNext();

            return !disposed;
        }

        public void Reset()
        {

        }

        public T Current { get; private set; }

        object IEnumerator.Current => Current;
    }
}