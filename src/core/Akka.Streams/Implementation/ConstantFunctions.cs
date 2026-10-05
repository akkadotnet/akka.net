//-----------------------------------------------------------------------
// <copyright file="ConstantFunctions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Provides small constant-valued functions used by stream stages.
    /// </summary>
    internal static class ConstantFunctions
    {
        /// <summary>
        /// Creates a function that returns one for every input.
        /// </summary>
        /// <typeparam name="T">The input type accepted by the function.</typeparam>
        /// <returns>A function that returns <c>1</c> regardless of its input.</returns>
        public static Func<T, long> OneLong<T>() => _ => 1L;
    }
}
