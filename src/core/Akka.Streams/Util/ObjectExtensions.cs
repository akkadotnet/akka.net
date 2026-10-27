//-----------------------------------------------------------------------
// <copyright file="ObjectExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Util;

namespace Akka.Streams.Util
{
    /// <summary>
    /// Utility methods for object values.
    /// </summary>
    public static class ObjectExtensions
    {
        /// <summary>
        /// Checks whether a value equals the default value of its type.
        /// </summary>
        /// <typeparam name="T">Type of the value being compared.</typeparam>
        /// <param name="obj">Value to compare with <see langword="default"/>.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> equals the default value of <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
        public static bool IsDefaultForType<T>(this T obj) => EqualityComparer<T>.Default.Equals(obj, default(T));
    }
}
