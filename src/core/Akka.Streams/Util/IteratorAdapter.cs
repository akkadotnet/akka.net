//-----------------------------------------------------------------------
// <copyright file="IteratorAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

using System.Threading;
using System.Threading.Tasks;

namespace Akka.Streams.Util
{
    /// <summary>
    /// Interface matching Java's iterator semantics.
    /// Should only be needed in rare circumstances, where knowing whether there are
    /// more elements without consuming them makes the code easier to write.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal interface IIterator<out T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        bool HasNext();
        Task<bool> HasNextAsync();
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        T Next();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class IteratorAdapter<T> : IIterator<T>
    {
        private readonly IEnumerator<T> _enumerator;
        private bool? _hasNext;
        private Exception _exception;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumerator">TBD</param>
        public IteratorAdapter(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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
        public Task<bool> HasNextAsync()
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
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
    internal class IterableAdapter<T> : IIterator<T>
    {
        private readonly IAsyncEnumerator<T> _enumerable;
        private bool? _hasNext;
        private Exception _exception;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumerable">TBD</param>
        public IterableAdapter(IAsyncEnumerator<T> enumerable)
        {
            _enumerable = enumerable;
        }

        public bool HasNext()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public async Task<bool> HasNextAsync()
        {
            if (_hasNext == null)
            {               
                try
                {
                    _hasNext = await  _enumerable.MoveNextAsync();
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
        /// TBD
        /// </summary>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
        public T Next()
        {
            if (!HasNextAsync().Result)
                throw new InvalidOperationException();
            if (_exception != null)
                throw new AggregateException(_exception);

            _hasNext = null;
            _exception = null;

            return _enumerable.Current;
        }
    }
}
