//-----------------------------------------------------------------------
// <copyright file="IteratorAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Streams.Util
{
    /// <summary>
    /// Interface matching Java's iterator semantics.
    /// Should only be needed in rare circumstances, where knowing whether there are
    /// more elements without consuming them makes the code easier to write.
    /// </summary>
    /// <typeparam name="T">Type of values returned by the iterator.</typeparam>
    internal interface IIterator<out T>
    {
        /// <summary>
        /// Tests whether the iterator has a next element without returning it.
        /// </summary>
        /// <returns><see langword="true"/> if an element is available; otherwise, <see langword="false"/>.</returns>
        bool HasNext();
        /// <summary>
        /// Returns the next element and advances the iterator.
        /// </summary>
        /// <returns>The next element.</returns>
        T Next();
    }

    /// <summary>
    /// Adapter that exposes an <see cref="IEnumerator{T}"/> through the peek-before-consume <see cref="IIterator{T}"/> contract.
    /// </summary>
    /// <typeparam name="T">Type of values returned by the iterator.</typeparam>
    internal sealed class IteratorAdapter<T> : IIterator<T>
    {
        private readonly IEnumerator<T> _enumerator;
        private bool? _hasNext;
        private Exception _exception;

        /// <summary>
        /// Creates an adapter for an enumerator.
        /// </summary>
        /// <param name="enumerator">Enumerator that supplies the elements.</param>
        public IteratorAdapter(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

        /// <summary>
        /// Determines whether the underlying enumerator has an element available.
        /// </summary>
        /// <returns><see langword="true"/> if an element or a deferred enumerator exception is available; otherwise, <see langword="false"/>.</returns>
        public bool HasNext()
        {
            if (_hasNext == null)
            {
                try
                {
                    _hasNext = _enumerator.MoveNext();
                    _exception = null;
                }
                catch (Exception e)
                {
                    // capture exception and throw it when Next() is called
                    _exception = e;
                    _hasNext = true;
                }
            }

            return _hasNext.Value;
        }

        /// <summary>
        /// Returns the current element after confirming that one is available.
        /// </summary>
        /// <exception cref="InvalidOperationException">The underlying enumerator has no next element.</exception>
        /// <exception cref="AggregateException">The underlying enumerator threw while advancing.</exception>
        /// <returns>The current element.</returns>
        public T Next()
        {
            if (!HasNext())
                throw new InvalidOperationException();
            if (_exception != null)
                throw new AggregateException(_exception);

            _hasNext = null;
            _exception = null;

            return _enumerator.Current;
        }
    }
}
