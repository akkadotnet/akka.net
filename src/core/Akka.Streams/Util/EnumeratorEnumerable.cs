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
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class EnumeratorEnumerable<T> : IEnumerable<T>
    {
        private readonly Func<IEnumerator<T>> _enumeratorFactory;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumeratorFactory">TBD</param>
        public EnumeratorEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _enumeratorFactory = enumeratorFactory;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<T> GetEnumerator() => _enumeratorFactory();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class ContinuallyEnumerable<T> : IEnumerable<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ContinuallyEnumerator : IEnumerator<T>
        {
            Func<IEnumerator<T>> _enumeratorFactory;
            private IEnumerator<T> _current;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="enumeratorFactory">TBD</param>
            public ContinuallyEnumerator(Func<IEnumerator<T>> enumeratorFactory)
            {
                _enumeratorFactory = enumeratorFactory;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Dispose() => _enumeratorFactory = null;

            /// <summary>
            /// TBD
            /// </summary>
            /// <exception cref="ArgumentException">TBD</exception>
            /// <returns>TBD</returns>
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

            /// <summary>
            /// TBD
            /// </summary>
            public void Reset() => _current = _enumeratorFactory();

            /// <summary>
            /// TBD
            /// </summary>
            public T Current => _current.Current;

            object IEnumerator.Current => Current;
        }

        private readonly ContinuallyEnumerator _continuallyEnumerator;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumeratorFactory">TBD</param>
        public ContinuallyEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _continuallyEnumerator = new ContinuallyEnumerator(enumeratorFactory);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<T> GetEnumerator() => _continuallyEnumerator;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}