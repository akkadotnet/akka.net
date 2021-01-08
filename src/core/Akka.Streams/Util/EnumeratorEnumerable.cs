//-----------------------------------------------------------------------
// <copyright file="EnumeratorEnumerable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Initializes a new instance of the <see cref="EnumeratorEnumerable{T}"/> class.
        /// </summary>
        /// <param name="enumeratorFactory">The method used to create an <see cref="IEnumerator{T}"/> to iterate over this enumerable.</param>
        public EnumeratorEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _enumeratorFactory = enumeratorFactory;
        }

        /// <inheritdoc/>
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
            /// Initializes a new instance of the <see cref="ContinuallyEnumerable{T}.ContinuallyEnumerator" /> class.
            /// </summary>
            /// <param name="enumeratorFactory">The method used to create an <see cref="IEnumerator{T}"/> to iterate over an enumerable.</param>
            public ContinuallyEnumerator(Func<IEnumerator<T>> enumeratorFactory)
            {
                _enumeratorFactory = enumeratorFactory;
            }

            /// <inheritdoc/>
            public void Dispose() => _enumeratorFactory = null;

            /// <inheritdoc/>
            /// <exception cref="ArgumentException">
            /// This exception is thrown when the enumerator has passed the end of an enumerable.
            /// </exception>
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

            /// <inheritdoc/>
            public void Reset() => _current = _enumeratorFactory();

            /// <inheritdoc/>
            public T Current => _current.Current;

            object IEnumerator.Current => Current;
        }

        private readonly ContinuallyEnumerator _continuallyEnumerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContinuallyEnumerable{T}" /> class.
        /// </summary>
        /// <param name="enumeratorFactory">The method used to create an <see cref="IEnumerator{T}"/> to iterate over this enumerable.</param>
        public ContinuallyEnumerable(Func<IEnumerator<T>> enumeratorFactory)
        {
            _continuallyEnumerator = new ContinuallyEnumerator(enumeratorFactory);
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => _continuallyEnumerator;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
