//-----------------------------------------------------------------------
// <copyright file="ObjectExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Streams.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class ObjectExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public static bool IsDefaultForType<T>(this T obj) => EqualityComparer<T>.Default.Equals(obj, default(T));
    }
}