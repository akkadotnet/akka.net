//-----------------------------------------------------------------------
// <copyright file="Iterator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
    internal struct Iterator<T>
    {
        private readonly IList<T> _enumerator;
        private int _index;

        public Iterator(IEnumerable<T> enumerator)
        {
            _index = 0;
            _enumerator = enumerator.ToList();
        }

        public T Next()
        {
            return _index != _enumerator.Count 
                ? _enumerator[_index++] 
                : default;
        }

        public bool IsEmpty()
        {
            return _index == _enumerator.Count;
        }

        public IEnumerable<T> ToVector()
        {
            return _enumerator.Skip(_index);
        }
    }
}
