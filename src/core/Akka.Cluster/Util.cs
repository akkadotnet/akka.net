//-----------------------------------------------------------------------
// <copyright file="Util.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Configuration;

namespace Akka.Cluster
{
    /// <summary>
    /// TBD
    /// </summary>
    static class Utils
    {
        //TODO: Tests
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TSource">TBD</typeparam>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <param name="selector">TBD</param>
        /// <returns>TBD</returns>
        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector)
        {
            return source.MaxBy(selector, Comparer<TKey>.Default);
        }

        //TODO: Test
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TSource">TBD</typeparam>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <param name="selector">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="this">TBD</param>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="this">TBD</param>
        /// <param name="partitioner">TBD</param>
        /// <returns>TBD</returns>
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

