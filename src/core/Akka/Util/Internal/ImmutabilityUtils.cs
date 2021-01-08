//-----------------------------------------------------------------------
// <copyright file="ImmutabilityUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal
{
    /// <summary>
    /// Utility class for adding some basic immutable behaviors
    /// to specific types of collections without having to reference
    /// the entire BCL.Immutability NuGet package.
    /// 
    /// INTERNAL API
    /// </summary>
    internal static class ImmutabilityUtils
    {
        #region HashSet<T>

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="set">TBD</param>
        /// <param name="item">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="set"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public static HashSet<T> CopyAndAdd<T>(this HashSet<T> set, T item)
        {
            if (set == null) throw new ArgumentNullException(nameof(set), "CopyAndAdd cause exception cannot be null");
            // ReSharper disable once PossibleNullReferenceException
            var copy = new T[set.Count + 1];
            set.CopyTo(copy);
            copy[set.Count] = item;
            return new HashSet<T>(copy);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="set">TBD</param>
        /// <param name="item">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="set"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public static HashSet<T> CopyAndRemove<T>(this HashSet<T> set, T item)
        {
            if (set == null) throw new ArgumentNullException(nameof(set), "CopyAndRemove cause exception cannot be null");
            // ReSharper disable once PossibleNullReferenceException
            var copy = new T[set.Count];
            set.CopyTo(copy);
            var copyList = copy.ToList();
            copyList.Remove(item);
            return new HashSet<T>(copyList);
        }

        #endregion

        #region IDictionary<T>

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="dict">TBD</param>
        /// <param name="values">TBD</param>
        /// <returns>TBD</returns>
        public static SortedDictionary<TKey, TValue> CopyAndAdd<TKey, TValue>(this SortedDictionary<TKey, TValue> dict,
            IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            var newDict = new SortedDictionary<TKey, TValue>();
            foreach(var item in dict.Concat(values))
                newDict.Add(item.Key, item.Value);
            return newDict;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="dict">TBD</param>
        /// <param name="values">TBD</param>
        /// <returns>TBD</returns>
        public static SortedDictionary<TKey, TValue> CopyAndRemove<TKey, TValue>(this SortedDictionary<TKey, TValue> dict,
            IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            var newDict = new SortedDictionary<TKey, TValue>();
            foreach (var item in dict.Except(values))
                newDict.Add(item.Key, item.Value);
            return newDict;
        }

        #endregion
    }
}

