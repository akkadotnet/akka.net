using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.Streams.Util
{
    public class EnumeratorEnumerable<T> : IEnumerable<T>
    {
        private readonly Func<IEnumerator<T>> _enumeratorFactory;

        public EnumeratorEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _enumeratorFactory = enumeratorFactory;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _enumeratorFactory();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}