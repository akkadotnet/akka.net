//-----------------------------------------------------------------------
// <copyright file="Vector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Vector
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="number">TBD</param>
        /// <returns>TBD</returns>
        public static Func<Func<T>, IList<T>> Fill<T>(int number)
        {
            return func => Enumerable
                .Range(1, number)
                .Select(_ => func())
                .ToList();
        } 
    }
}
