using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Configuration;

namespace Akka.Cluster
{
    static class Utils
    {
        //TODO: Tests
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

        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector)
        {
            return source.MaxBy(selector, Comparer<TKey>.Default);
        }

        //TODO: Test
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

        public static TimeSpan? GetMillisDurationWithOffSwitch(this Config @this, string key)
        {
            TimeSpan? ret = null;
            if (@this.GetString(key).ToLower() != "off") ret = @this.GetMillisDuration(key);
            return ret;
        }

        public static Tuple<ImmutableSortedSet<T>, ImmutableSortedSet<T>> Partition<T>(this ImmutableSortedSet<T> @this,
            Func<T, bool> partitioner)
        {
            var @true = new List<T>();
            var @false = new List<T>();

            foreach (var item in @this)
            {
                (partitioner(item) ? @true : @false).Add(item);
            }

            return Tuple.Create(@true.ToImmutableSortedSet(), @false.ToImmutableSortedSet());
        }
    }
}
