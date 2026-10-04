//-----------------------------------------------------------------------
// <copyright file="Keep.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Convenience functions for often-encountered purposes like keeping only the
    /// left (first) or only the right (second) of two input values.
    /// </summary> 
    public static class Keep
    {
        /// <summary>
        /// Selects the first value and discards the second.
        /// </summary>
        /// <typeparam name="TLeft">The type of the value to keep.</typeparam>
        /// <typeparam name="TRight">The type of the value to discard.</typeparam>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><paramref name="left"/>.</returns>
        public static TLeft Left<TLeft, TRight>(TLeft left, TRight right) => left;

        /// <summary>
        /// Selects the second value and discards the first.
        /// </summary>
        /// <typeparam name="TLeft">The type of the value to discard.</typeparam>
        /// <typeparam name="TRight">The type of the value to keep.</typeparam>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><paramref name="right"/>.</returns>
        public static TRight Right<TLeft, TRight>(TLeft left, TRight right) => right;

        /// <summary>
        /// Keeps both values as a tuple in their original order.
        /// </summary>
        /// <typeparam name="TLeft">The type of the first value.</typeparam>
        /// <typeparam name="TRight">The type of the second value.</typeparam>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns>A tuple containing <paramref name="left"/> followed by <paramref name="right"/>.</returns>
        public static (TLeft, TRight) Both<TLeft, TRight>(TLeft left, TRight right) => (left, right);

        /// <summary>
        /// Discards both values and returns <see cref="NotUsed"/>.
        /// </summary>
        /// <typeparam name="TLeft">The type of the first value.</typeparam>
        /// <typeparam name="TRight">The type of the second value.</typeparam>
        /// <param name="left">The first value, which is discarded.</param>
        /// <param name="right">The second value, which is discarded.</param>
        /// <returns>The <see cref="NotUsed"/> singleton.</returns>
        public static NotUsed None<TLeft, TRight>(TLeft left, TRight right) => NotUsed.Instance;

        private static readonly RuntimeMethodHandle KeepRightMethodhandle = typeof(Keep).GetMethod(nameof(Right)).MethodHandle;

        /// <summary>
        /// Determines whether the supplied function is the <see cref="Right{TLeft,TRight}"/> selector.
        /// </summary>
        /// <typeparam name="T1">The type of the first input.</typeparam>
        /// <typeparam name="T2">The type of the second input.</typeparam>
        /// <typeparam name="T3">The function's result type.</typeparam>
        /// <param name="fn">The function to inspect.</param>
        /// <returns><see langword="true"/> when <paramref name="fn"/> is the generic <see cref="Right{TLeft,TRight}"/> method; otherwise, <see langword="false"/>.</returns>
        public static bool IsRight<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.GetMethodInfo().IsGenericMethod && fn.GetMethodInfo().GetGenericMethodDefinition().MethodHandle.Value == KeepRightMethodhandle.Value;
        }

        private static readonly RuntimeMethodHandle KeepLeftMethodhandle = typeof(Keep).GetMethod(nameof(Left)).MethodHandle;

        /// <summary>
        /// Determines whether the supplied function is the <see cref="Left{TLeft,TRight}"/> selector.
        /// </summary>
        /// <typeparam name="T1">The type of the first input.</typeparam>
        /// <typeparam name="T2">The type of the second input.</typeparam>
        /// <typeparam name="T3">The function's result type.</typeparam>
        /// <param name="fn">The function to inspect.</param>
        /// <returns><see langword="true"/> when <paramref name="fn"/> is the generic <see cref="Left{TLeft,TRight}"/> method; otherwise, <see langword="false"/>.</returns>
        public static bool IsLeft<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.GetMethodInfo().IsGenericMethod && fn.GetMethodInfo().GetGenericMethodDefinition().MethodHandle.Value == KeepLeftMethodhandle.Value;
        }
    }
}
