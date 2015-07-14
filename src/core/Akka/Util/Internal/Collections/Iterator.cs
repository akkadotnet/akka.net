//-----------------------------------------------------------------------
// <copyright file="Iterator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
    public class Iterator<T>
    {
        private readonly IList<T> _enumerator;
        private int _index;

        public Iterator(IEnumerable<T> enumerator)
        {
            _enumerator = enumerator.ToList();
        }

        public T Next()
        {
            return _index != _enumerator.Count 
                ? _enumerator[_index++] 
                : default(T);
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