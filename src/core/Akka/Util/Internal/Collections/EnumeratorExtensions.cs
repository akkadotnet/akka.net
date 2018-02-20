//-----------------------------------------------------------------------
// <copyright file="EnumeratorExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Util.Internal.Collections
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class EnumeratorExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumerable">TBD</param>
        /// <returns>TBD</returns>
        public static Iterator<T> Iterator<T>(this IEnumerable<T> enumerable)
        {
            return new Iterator<T>(enumerable);
        }
    }
}
