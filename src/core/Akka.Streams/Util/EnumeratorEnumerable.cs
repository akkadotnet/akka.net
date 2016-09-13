//-----------------------------------------------------------------------
// <copyright file="EnumeratorEnumerable.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

        public IEnumerator<T> GetEnumerator() => _enumeratorFactory();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class ContinuallyEnumerable<T> : IEnumerable<T>
    {
        public sealed class ContinuallyEnumerator : IEnumerator<T>
        {
            Func<IEnumerator<T>> _enumeratorFactory;
            private IEnumerator<T> _current;

            public ContinuallyEnumerator(Func<IEnumerator<T>> enumeratorFactory)
            {
                _enumeratorFactory = enumeratorFactory;
            }

            public void Dispose() => _enumeratorFactory = null;

            public bool MoveNext()
            {
                if (_current == null || !_current.MoveNext())
                {
                    _current = _enumeratorFactory();
                    if(!_current.MoveNext())
                        throw new ArgumentException("empty iterator");
                }

                return true;
            }

            public void Reset() => _current = _enumeratorFactory();

            public T Current => _current.Current;

            object IEnumerator.Current => Current;
        }

        private readonly ContinuallyEnumerator _continuallyEnumerator;

        public ContinuallyEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _continuallyEnumerator = new ContinuallyEnumerator(enumeratorFactory);
        }

        public IEnumerator<T> GetEnumerator() => _continuallyEnumerator;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}