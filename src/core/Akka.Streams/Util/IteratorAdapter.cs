using System;
using System.Collections.Generic;

namespace Akka.Streams.Util
{
    /// <summary>
    /// Interface matching Java's iterator semantics.
    /// Should only be needed in rare circumstances, where knowing whether there are
    /// more elements without consuming them makes the code easier to write.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal interface IIterator<out T>
    {
        bool HasNext();
        T Next();
    }

    internal class IteratorAdapter<T> : IIterator<T>
    {
        private readonly IEnumerator<T> _enumerator;
        private bool? _hasNext;

        public IteratorAdapter(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

        public bool HasNext()
        {
            if (_hasNext == null)
            {
                _hasNext = _enumerator.MoveNext();
            }

            return _hasNext.Value;
        }

        public T Next()
        {
            if (!HasNext())
                throw new InvalidOperationException();

            _hasNext = null;

            return _enumerator.Current;
        }
    }
}