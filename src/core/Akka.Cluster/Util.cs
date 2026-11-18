//-----------------------------------------------------------------------
// <copyright file="Util.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Configuration;

namespace Akka.Cluster
{
    /// <summary>
    /// Internal helpers for cluster collection and configuration operations.
    /// </summary>
    static class Utils
    {
        //TODO: Tests
        /// <summary>
        /// Returns the smallest element according to the supplied comparer.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The sequence to inspect.</param>
        /// <param name="comparer">The comparer used to order elements.</param>
        /// <returns>The first element with the smallest compared value.</returns>
        public static T Min<T>(this IEnumerable<T> source,
            IComparer<T> comparer)
        {
            using (var sourceIterator = source.GetEnumerator())
            {
                if (!sourceIterator.MoveNext())
                {
                    throw new InvalidOperationException("Sequence was empty");
                }
                var min = sourceIterator.Current;
                while (sourceIterator.MoveNext())
                {
                    var candidate = sourceIterator.Current;
                    if (comparer.Compare(candidate, min) < 0)
                    {
                        min = candidate;
                    }
                }
                return min;
            }
        }

        /// <summary>
        /// Returns the source element whose selected key compares greatest using the default key comparer.
        /// </summary>
        /// <typeparam name="TSource">The source element type.</typeparam>
        /// <typeparam name="TKey">The key type produced by the selector.</typeparam>
        /// <param name="source">The sequence to inspect.</param>
        /// <param name="selector">A function that selects a key from each element.</param>
        /// <returns>The first source element whose selected key is greatest.</returns>
        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector)
        {
            return source.MaxBy(selector, Comparer<TKey>.Default);
        }

        //TODO: Test
        /// <summary>
        /// Returns the source element whose selected key compares greatest using the supplied comparer.
        /// </summary>
        /// <typeparam name="TSource">The source element type.</typeparam>
        /// <typeparam name="TKey">The key type produced by the selector.</typeparam>
        /// <param name="source">The sequence to inspect.</param>
        /// <param name="selector">A function that selects a key from each element.</param>
        /// <param name="comparer">The comparer used to order selected keys.</param>
        /// <returns>The first source element whose selected key is greatest.</returns>
        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector, IComparer<TKey> comparer)
        {
            //TODO:
            //source.ThrowIfNull("source");
            //selector.ThrowIfNull("selector");
            //comparer.ThrowIfNull("comparer");
            using (var sourceIterator = source.GetEnumerator())
            {
                if (!sourceIterator.MoveNext())
                {
                    throw new InvalidOperationException("Sequence was empty");
                }
                var max = sourceIterator.Current;
                var maxKey = selector(max);
                while (sourceIterator.MoveNext())
                {
                    var candidate = sourceIterator.Current;
                    var candidateProjected = selector(candidate);
                    if (comparer.Compare(candidateProjected, maxKey) > 0)
                    {
                        max = candidate;
                        maxKey = candidateProjected;
                    }
                }
                return max;
            }
        }

        /// <summary>
        /// Reads an optional time span, treating the configured values <c>off</c>, <c>false</c>, and <c>no</c> as disabled.
        /// </summary>
        /// <param name="this">The configuration containing the setting.</param>
        /// <param name="key">The path of the time-span setting.</param>
        /// <returns><c>null</c> when the value is disabled or absent; otherwise, the parsed time span.</returns>
        public static TimeSpan? GetTimeSpanWithOffSwitch(this Config @this, string key)
        {
            TimeSpan? ret = null;
            var useTimeSpanOffSwitch = @this.GetString(key, "");
            if (useTimeSpanOffSwitch.ToLower() != "off" &&
                useTimeSpanOffSwitch.ToLower() != "false" &&
                useTimeSpanOffSwitch.ToLower() != "no")
                ret = @this.GetTimeSpan(key, null);
            return ret;
        }

        /// <summary>
        /// Splits a sorted set into elements for which the predicate returns true and false.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="this">The sorted set to partition.</param>
        /// <param name="partitioner">The predicate that selects the first result set.</param>
        /// <returns>A pair containing matching elements first and nonmatching elements second.</returns>
        public static (ImmutableSortedSet<T>, ImmutableSortedSet<T>) Partition<T>(this ImmutableSortedSet<T> @this,
            Func<T, bool> partitioner)
        {
            var @true = new List<T>();
            var @false = new List<T>();

            foreach (var item in @this)
            {
                (partitioner(item) ? @true : @false).Add(item);
            }

            return (@true.ToImmutableSortedSet(), @false.ToImmutableSortedSet());
        }
    }
}
