//-----------------------------------------------------------------------
// <copyright file="ConstantFunctions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class ConstantFunctions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Func<T, long> OneLong<T>() => _ => 1L;
    }
}
