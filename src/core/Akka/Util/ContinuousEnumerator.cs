//-----------------------------------------------------------------------
// <copyright file="ContinuousEnumerator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;

namespace Akka.Util
{
    /// <summary>
    /// Implements a circular <see cref="IEnumerator{T}"/> around an existing <see cref="IEnumerator{T}"/>.
    /// 
    /// This allows for continuous read-only iteration over a set.
    /// </summary>
    /// <typeparam name="T">The type of objects to enumerate</typeparam>
    internal sealed class ContinuousEnumerator<T> : IEnumerator<T>
    {
        private readonly IEnumerator<T> _internalEnumerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContinuousEnumerator{T}"/> class.
        /// </summary>
        /// <param name="internalEnumerator">The raw iterator from some <see cref="IEnumerable{T}"/> object</param>
        public ContinuousEnumerator(IEnumerator<T> internalEnumerator)
        {
            _internalEnumerator = internalEnumerator;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _internalEnumerator.Dispose();
        }

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (!_internalEnumerator.MoveNext())
            {
                _internalEnumerator.Reset();
                return _internalEnumerator.MoveNext();
            }
            return true;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _internalEnumerator.Reset();
        }

        /// <inheritdoc/>
        public T Current { get { return _internalEnumerator.Current; } }

        object IEnumerator.Current
        {
            get { return Current; }
        }
    }

    /// <summary>
    /// Extension method class for adding <see cref="ContinuousEnumerator{T}"/> support to any <see cref="IEnumerable{T}"/>
    /// instance within Akka.NET
    /// </summary>
    internal static class ContinuousEnumeratorExtensions
    {
        /// <summary>
        /// Provides a <see cref="ContinuousEnumerator{T}"/> instance for <paramref name="collection"/>.
        /// 
        /// Internally, it just wraps <paramref name="collection"/>'s internal iterator with circular iteration behavior.
        /// </summary>
        /// <param name="collection">TBD</param>
        /// <returns>TBD</returns>
        public static ContinuousEnumerator<T> GetContinuousEnumerator<T>(this IEnumerable<T> collection)
        {
            return new ContinuousEnumerator<T>(collection.GetEnumerator());
        }
    }
}

