//-----------------------------------------------------------------------
// <copyright file="Iterator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class Iterator<T>
    {
        private readonly IList<T> _enumerator;
        private int _index;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enumerator">TBD</param>
        public Iterator(IEnumerable<T> enumerator)
        {
            _enumerator = enumerator.ToList();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public T Next()
        {
            return _index != _enumerator.Count 
                ? _enumerator[_index++] 
                : default(T);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public bool IsEmpty()
        {
            return _index == _enumerator.Count;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<T> ToVector()
        {
            return _enumerator.Skip(_index);
        }
    }
}
