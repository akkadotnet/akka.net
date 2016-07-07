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
}