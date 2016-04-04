//-----------------------------------------------------------------------
// <copyright file="InterlockedSpin.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.Util.Internal
{
    /// <summary>INTERNAL!
    /// Implements helpers for performing Compare-and-swap operations using <see cref="Interlocked.CompareExchange{T}"/>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class InterlockedSpin
    {
        /// <summary>INTERNAL!
        /// Atomically updates the object <paramref name="reference"/> by calling <paramref name="updater"/> to get the new value.
        /// Note that <paramref name="updater"/> may be called many times so it should be idempotent.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        /// <returns>The updated value.</returns>
        public static T Swap<T>(ref T reference, Func<T, T> updater) where T : class
        {
            var spinWait = new SpinWait();
            while(true)
            {
                var current = reference;
                var updated = updater(current);
                if(CompareExchange(ref reference, current, updated)) return updated;
                spinWait.SpinOnce();
            }
        }


        /// <summary>INTERNAL!
        /// Atomically updates the int <paramref name="reference"/> by calling <paramref name="updateIfTrue"/> to get the new value.
        /// <paramref name="updateIfTrue"/> returns a Tuple&lt;should update, the new int value, the return value&gt;
        /// If the first item in the tuple is true, the value is updated, and the third value of the tuple is returned.
        /// Note that <paramref name="updateIfTrue"/> may be called many times so it should be idempotent.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        /// <returns>The third value from the tuple return by <paramref name="updateIfTrue"/>.</returns>
        public static TReturn ConditionallySwap<T, TReturn>(ref T reference, Func<T, Tuple<bool, T, TReturn>> updateIfTrue) where T : class
        {
            var spinWait = new SpinWait();
            while (true)
            {
                var current = reference;
                var t = updateIfTrue(current);
                if (!t.Item1) return t.Item3;
                if (CompareExchange(ref reference, current, t.Item2)) return t.Item3;
                spinWait.SpinOnce();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CompareExchange<T>(ref T reference, T expectedValue, T newValue) where T : class
        {
            return Interlocked.CompareExchange(ref reference, newValue, expectedValue) == expectedValue;
        }

    }
}

