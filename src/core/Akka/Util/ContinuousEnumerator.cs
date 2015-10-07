//-----------------------------------------------------------------------
// <copyright file="ContinuousEnumerator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="T"></typeparam>
    internal sealed class ContinuousEnumerator<T> : IEnumerator<T>
    {
        /// <summary>
        /// The raw iterator from some <see cref="IEnumerable{T}"/> object
        /// </summary>
        private readonly IEnumerator<T> _internalEnumerator;

        public ContinuousEnumerator(IEnumerator<T> internalEnumerator)
        {
            _internalEnumerator = internalEnumerator;
        }

        public void Dispose()
        {
            _internalEnumerator.Dispose();
        }

        public bool MoveNext()
        {
            if (!_internalEnumerator.MoveNext())
            {
                _internalEnumerator.Reset();
                return _internalEnumerator.MoveNext();
            }
            return true;
        }

        public void Reset()
        {
            _internalEnumerator.Reset();
        }

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
        public static ContinuousEnumerator<T> GetContinuousEnumerator<T>(this IEnumerable<T> collection)
        {
            return new ContinuousEnumerator<T>(collection.GetEnumerator());
        }
    }
}

