//-----------------------------------------------------------------------
// <copyright file="DictionaryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Util.Internal
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="dict">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        public static void Put<TKey, TVal>(this IDictionary<TKey, TVal> dict, TKey key, TVal value)
        {
            if (dict.ContainsKey(key))
                dict[key] = value;
            else
                dict.Add(key, value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="dict">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static bool TryAdd<TKey, TVal>(this IDictionary<TKey, TVal> dict, TKey key, TVal value)
        {
            if (dict.ContainsKey(key))
                return false;

            dict.Add(key, value);
            return true;
        }
    }
}
