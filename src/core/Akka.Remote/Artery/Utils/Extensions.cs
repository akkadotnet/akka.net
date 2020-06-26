using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.Remote.Artery.Utils
{
    internal static class Extensions
    {
        /// <summary>
        /// Get a value for a given key, or if it does not exist then default the value via a
        /// <see cref="Func{TKey, TResult}"/> and put it in the Dictionary.
        /// </summary>
        /// <typeparam name="TKey">key type of Dictionary</typeparam>
        /// <typeparam name="TValue">value type of Dictionary</typeparam>
        /// <param name="dict">the <see cref="Dictionary{TKey,TValue}"/> to search on.</param>
        /// <param name="key">to search on.</param>
        /// <param name="mappingFunction">to provide a value if <see cref="Dictionary{TKey,TValue}.TryGetValue"/> returns false.</param>
        /// <returns>the value if found otherwise the default.</returns>
        public static TValue ComputeIfAbsent<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, 
            TKey key,
            Func<TKey, TValue> mappingFunction)
        {
            if (dict.TryGetValue(key, out var value))
                return value;

            value = mappingFunction(key);
            if(!value.Equals(default(TValue)))
                dict.Add(key, value);

            return value;
        }

        /// <summary>
        /// Bitwise rotate left an unsigned integer
        /// </summary>
        /// <param name="i"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateLeft(this uint i, int distance)
            => (i << distance) | (i >> -distance);

        /// <summary>
        /// Bitwise rotate right an unsigned integer
        /// </summary>
        /// <param name="i"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight(this uint i, int distance)
            => (i >> distance) | (i << -distance);
    }
}
