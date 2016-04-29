//-----------------------------------------------------------------------
// <copyright file="IteratorAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal interface IIterator<out T>
    {
        bool HasNext();
        T Next();
    }

    internal class IteratorAdapter<T> : IIterator<T>
    {
        private readonly IEnumerator<T> _enumerator;
        private bool? _hasNext;
        private Exception _exception;

        public IteratorAdapter(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

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

        public T Next()
        {
            if (!HasNext())
                throw new InvalidOperationException();
            if (_exception != null)
                throw _exception;

            _hasNext = null;
            _exception = null;

            return _enumerator.Current;
        }
    }
}