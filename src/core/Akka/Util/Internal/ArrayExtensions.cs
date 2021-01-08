//-----------------------------------------------------------------------
// <copyright file="ArrayExtensions.cs" company="Akka.NET Project">
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
    /// Provides extension utilities to arrays.
    /// </summary>
    internal static class ArrayExtensions
    {
        /// <summary>
        /// Determines if an array is null or empty.
        /// </summary>
        /// <param name="obj">The array to check.</param>
        /// <returns>True if null or empty, false otherwise.</returns>
        public static bool IsNullOrEmpty(this Array obj)
        {
            return ((obj == null) || (obj.Length == 0));
        }

        /// <summary>
        /// Determines if an array is not null or empty.
        /// </summary>
        /// <param name="obj">The array to check.</param>
        /// <returns>True if not null or empty, false otherwise.</returns>
        public static bool NonEmpty(this Array obj)
        {
            return obj != null && obj.Length > 0;
        }

        /// <summary>
        /// Shuffles an array of objects.
        /// </summary>
        /// <typeparam name="T">The type of the array to sort.</typeparam>
        /// <param name="array">The array to sort.</param>
        public static void Shuffle<T>(this T[] array)
        {
            var length = array.Length;
            var random = new Random();

            while (length > 1)
            {
                int randomNumber = random.Next(length--);
                T obj = array[length];
                array[length] = array[randomNumber];
                array[randomNumber] = obj;
            }
        }

        /// <summary>
        /// Implementation of Scala's ZipWithIndex method.
        /// 
        /// Folds a collection into a Dictionary where the original value (of type T) acts as the key
        /// and the index of the item in the array acts as the value.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="collection">TBD</param>
        /// <returns>TBD</returns>
        public static Dictionary<T, int> ZipWithIndex<T>(this IEnumerable<T> collection)
        {
            var i = 0;
            var dict = new Dictionary<T, int>();
            foreach (var item in collection)
            {
                dict.Add(item, i);
                i++;
            }
            return dict;
        }

        /// <summary>
        /// Grabs a subset of an IEnumerable based on a starting index and position
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="items">The array of items to slice</param>
        /// <param name="startIndex">The starting position to begin the slice</param>
        /// <param name="count">The number of items to take</param>
        /// <returns>A slice of size <paramref name="count"/> beginning from position <sparamref name="startIndex"/> in <paramref name="items"/>.</returns>
        internal static IEnumerable<T> Slice<T>(this IEnumerable<T> items, int startIndex, int count)
        {
            return items.Skip(startIndex).Take(count);
        }

        /// <summary>
        /// Select all the items in this array beginning with <paramref name="startingItem"/> and up until the end of the array.
        /// 
        /// <note>
        /// If <paramref name="startingItem"/> is not found in the array, From will return an empty set.
        /// If <paramref name="startingItem"/> is found at the end of the array, From will return the entire original array.
        /// </note>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="items">TBD</param>
        /// <param name="startingItem">TBD</param>
        /// <returns>TBD</returns>
        internal static IEnumerable<T> From<T>(this IEnumerable<T> items, T startingItem)
        {
            var itemsAsList = items.ToList();
            var indexOf = itemsAsList.IndexOf(startingItem);
            if (indexOf == -1) return new List<T>();
            if (indexOf == 0) return itemsAsList;
            var itemCount = (itemsAsList.Count - indexOf);
            return itemsAsList.Slice(indexOf, itemCount);
        }

        /// <summary>
        /// Select all the items in this array from the beginning until (but not including) <paramref name="startingItem"/>
        /// <note>
        /// If <paramref name="startingItem"/> is not found in the array, Until will select all items.
        /// If <paramref name="startingItem"/> is the first item in the array, an empty array will be returned.
        /// </note>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="items">TBD</param>
        /// <param name="startingItem">TBD</param>
        /// <returns>TBD</returns>
        internal static IEnumerable<T> Until<T>(this IEnumerable<T> items, T startingItem)
        {
            var enumerator = items.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                if (Equals(current, startingItem))
                    yield break;
                yield return current;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="items">TBD</param>
        /// <returns>TBD</returns>
        internal static IEnumerable<T> Tail<T>(this IEnumerable<T> items)
        {
            return items.Skip(1);
        }
    }
}

