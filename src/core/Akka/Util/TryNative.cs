// //-----------------------------------------------------------------------
// // <copyright file="TryNative.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Util
{
    /// <summary>
    /// Wraps possible exceptions into <see cref="Try{T}"/> result
    /// </summary>
    public static class TryNative
    {
        /// <summary>
        /// Gets <see cref="Try{T}"/> result from function execution
        /// </summary>
        public static Try<T> Apply<T>(Func<T> func)
        {
            try
            {
                return new Try<T>(func());
            }
            catch (Exception ex)
            {
                return new Try<T>(ex);
            }
        }
    }
}