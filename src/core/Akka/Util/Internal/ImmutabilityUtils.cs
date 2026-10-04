//-----------------------------------------------------------------------
// <copyright file="ImmutabilityUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Creates a new hash set containing the existing elements and the supplied item.
        /// </summary>
        /// <typeparam name="T">The type of elements in the set.</typeparam>
        /// <param name="set">The source set to copy.</param>
        /// <param name="item">The item to add to the new set.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="set"/> is undefined.
        /// </exception>
        /// <returns>A new set containing the source elements and <paramref name="item"/>. The elements themselves are not cloned.</returns>
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
        /// Creates a new hash set containing the source elements except for the supplied item.
        /// </summary>
        /// <typeparam name="T">The type of elements in the set.</typeparam>
        /// <param name="set">The source set to copy.</param>
        /// <param name="item">The item to remove from the new set, if present.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="set"/> is undefined.
        /// </exception>
        /// <returns>A new set containing the source elements except for <paramref name="item"/>. The elements themselves are not cloned.</returns>
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
        /// Creates a new sorted dictionary containing the source entries and the supplied entries.
        /// </summary>
        /// <typeparam name="TKey">The type of dictionary keys.</typeparam>
        /// <typeparam name="TValue">The type of dictionary values.</typeparam>
        /// <param name="dict">The source dictionary to copy.</param>
        /// <param name="values">The entries to add to the new dictionary.</param>
        /// <returns>A new sorted dictionary containing both input sequences. Keys and values are reused, not cloned.</returns>
        public static SortedDictionary<TKey, TValue> CopyAndAdd<TKey, TValue>(this SortedDictionary<TKey, TValue> dict,
            IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            var newDict = new SortedDictionary<TKey, TValue>();
            foreach(var item in dict.Concat(values))
                newDict.Add(item.Key, item.Value);
            return newDict;
        }

        /// <summary>
        /// Creates a new sorted dictionary containing source entries not present in the supplied entries.
        /// </summary>
        /// <typeparam name="TKey">The type of dictionary keys.</typeparam>
        /// <typeparam name="TValue">The type of dictionary values.</typeparam>
        /// <param name="dict">The source dictionary to copy.</param>
        /// <param name="values">The entries to exclude from the new dictionary.</param>
        /// <returns>A new sorted dictionary containing the remaining entries. Keys and values are reused, not cloned.</returns>
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
