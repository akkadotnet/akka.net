//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
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
    /// TBD
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static T AsInstanceOf<T>(this object self)
        {
            return (T) self;
        }

        /// <summary>
        /// Scala alias for Skip
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="count">TBD</param>
        /// <returns>TBD</returns>
        public static IEnumerable<T> Drop<T>(this IEnumerable<T> self, int count)
        {
            return self.Skip(count);
        }

        /// <summary>
        /// Scala alias for FirstOrDefault
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static T Head<T>(this IEnumerable<T> self)
        {
            return self.FirstOrDefault();
        }

        /// <summary>
        /// Splits a 'dotted path' in its elements, honouring quotes (not splitting by dots between quotes)
        /// </summary>
        /// <param name="path">The input path</param>
        /// <returns>The path elements</returns>
        public static IEnumerable<string> SplitDottedPathHonouringQuotes(this string path)
        {
            var i = 0;
            var j = 0;
            while (true)
            {
                if (j >= path.Length) yield break;
                else if (path[j] == '\"')
                {
                    i = path.IndexOf('\"', j + 1);
                    yield return path.Substring(j + 1, i - j - 1);
                    j = i + 2;
                }
                else
                {
                    i = path.IndexOf('.', j);
                    if (i == -1)
                    {
                        yield return path.Substring(j);
                        yield break;
                    }
                    yield return path.Substring(j, i - j);
                    j = i + 1;
                }
            }
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="separator">TBD</param>
        /// <returns>TBD</returns>
        public static string Join(this IEnumerable<string> self, string separator)
        {
            return string.Join(separator, self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static string BetweenDoubleQuotes(this string self)
        {
            return @"""" + self + @"""";
        }

        /// <summary>
        /// Dictionary helper that allows for idempotent updates. You don't need to care whether or not
        /// this item is already in the collection in order to update it.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="hash">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        public static void AddOrSet<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue value)
        {
            if (hash.ContainsKey(key))
                hash[key] = value;
            else
                hash.Add(key,value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="hash">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="elseValue">TBD</param>
        /// <returns>TBD</returns>
        public static TValue GetOrElse<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue elseValue)
        {
            if (hash.TryGetValue(key, out var value))
                return value;
            return elseValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="hash">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static IDictionary<TKey, TValue> AddAndReturn<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue value)
        {
            hash.AddOrSet(key, value);
            return hash;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="this">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static TimeSpan Max(this TimeSpan @this, TimeSpan other)
        {
            return @this > other ? @this : other;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="this">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static TimeSpan Min(this TimeSpan @this, TimeSpan other)
        {
            return @this < other ? @this : other;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumerable">TBD</param>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> enumerable, T item)
        {
            var itemInArray = new[] {item};
            if (enumerable == null)
                return itemInArray;
            return enumerable.Concat(itemInArray);
        }

        /// <summary>
        /// Applies a delegate <paramref name="action" /> to all elements of this enumerable.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{T}" /> to iterate.</param>
        /// <param name="action">The function that is applied for its side-effect to every element. The result of function <paramref name="action" /> is discarded.</param>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (var item in source)
                action(item);
        }

        /// <summary>
        /// Selects last n elements.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static IEnumerable<T> TakeRight<T>(this IEnumerable<T> self, int n)
        {
            var enumerable = self as T[] ?? self.ToArray();
            return enumerable.Skip(Math.Max(0, enumerable.Length - n));
        }
    }
}

