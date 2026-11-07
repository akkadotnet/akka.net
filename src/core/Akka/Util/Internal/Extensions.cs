//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
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
    /// Extension methods used by Akka.NET's internal collection, configuration, and time utilities.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Casts the object reference to the requested type.
        /// </summary>
        /// <typeparam name="T">The type to which the object is cast.</typeparam>
        /// <param name="self">The object to cast.</param>
        /// <returns>The same reference converted to <typeparamref name="T"/>.</returns>
        public static T AsInstanceOf<T>(this object self)
        {
            return (T) self;
        }

        /// <summary>
        /// Scala alias for Skip
        /// </summary>
        /// <typeparam name="T">The type of elements in the sequence.</typeparam>
        /// <param name="self">The source sequence.</param>
        /// <param name="count">The number of leading elements to skip.</param>
        /// <returns>The source sequence without its first <paramref name="count"/> elements.</returns>
        public static IEnumerable<T> Drop<T>(this IEnumerable<T> self, int count)
        {
            return self.Skip(count);
        }

        /// <summary>
        /// Scala alias for FirstOrDefault
        /// </summary>
        /// <typeparam name="T">The type of elements in the sequence.</typeparam>
        /// <param name="self">The sequence whose first element is returned.</param>
        /// <returns>The first element, or the default value of <typeparamref name="T"/> if the sequence is empty.</returns>
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
        /// Joins the strings in a sequence using the specified separator.
        /// </summary>
        /// <param name="self">The strings to join.</param>
        /// <param name="separator">The string placed between adjacent values.</param>
        /// <returns>The joined string.</returns>
        public static string Join(this IEnumerable<string> self, string separator)
        {
            return string.Join(separator, self);
        }

        /// <summary>
        /// Encloses a string in double-quote characters without escaping its contents.
        /// </summary>
        /// <param name="self">The string to enclose.</param>
        /// <returns>The input string surrounded by double quotes.</returns>
        public static string BetweenDoubleQuotes(this string self)
        {
            return @"""" + self + @"""";
        }

        /// <summary>
        /// Returns a dictionary value when the key exists, or the supplied fallback value otherwise.
        /// </summary>
        /// <typeparam name="TKey">The type of dictionary keys.</typeparam>
        /// <typeparam name="TValue">The type of dictionary values.</typeparam>
        /// <param name="hash">The dictionary to query.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="elseValue">The value returned when <paramref name="key"/> is absent.</param>
        /// <returns>The value associated with <paramref name="key"/>, or <paramref name="elseValue"/> if no value is found.</returns>
        public static TValue GetOrElse<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue elseValue)
        {
            if (hash.TryGetValue(key, out var value))
                return value;
            return elseValue;
        }

        /// <summary>
        /// Sets a dictionary entry and returns the same dictionary.
        /// </summary>
        /// <typeparam name="TKey">The type of dictionary keys.</typeparam>
        /// <typeparam name="TValue">The type of dictionary values.</typeparam>
        /// <param name="hash">The dictionary to update.</param>
        /// <param name="key">The key to add or replace.</param>
        /// <param name="value">The value to associate with <paramref name="key"/>.</param>
        /// <returns>The same dictionary after the entry has been set.</returns>
        public static IDictionary<TKey, TValue> AddAndReturn<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue value)
        {
            hash[key] = value;
            return hash;
        }

        /// <summary>
        /// Returns the greater of two time spans.
        /// </summary>
        /// <param name="this">The first time span to compare.</param>
        /// <param name="other">The second time span to compare.</param>
        /// <returns><paramref name="this"/> when it is greater; otherwise, <paramref name="other"/>.</returns>
        public static TimeSpan Max(this TimeSpan @this, TimeSpan other)
        {
            return @this > other ? @this : other;
        }

        /// <summary>
        /// Returns the lesser of two time spans.
        /// </summary>
        /// <param name="this">The first time span to compare.</param>
        /// <param name="other">The second time span to compare.</param>
        /// <returns><paramref name="this"/> when it is less; otherwise, <paramref name="other"/>.</returns>
        public static TimeSpan Min(this TimeSpan @this, TimeSpan other)
        {
            return @this < other ? @this : other;
        }

        /// <summary>
        /// Returns a sequence with the specified item appended to an optional source sequence.
        /// </summary>
        /// <typeparam name="T">The type of elements in the sequence.</typeparam>
        /// <param name="enumerable">The optional source sequence.</param>
        /// <param name="item">The item appended after the source elements.</param>
        /// <returns>The source elements followed by <paramref name="item"/>; when the source is <c>null</c>, a sequence containing only that item.</returns>
        #nullable enable
        public static IEnumerable<T> Concat<T>(this IEnumerable<T>? enumerable, T item)
        {
            var itemInArray = new[] {item};
            if (enumerable == null)
                return itemInArray;
            return enumerable.Concat(itemInArray);
        }
        #nullable restore

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
        /// <typeparam name="T">The type of elements in the sequence.</typeparam>
        /// <param name="self">The source sequence.</param>
        /// <param name="n">The maximum number of elements to return from the end.</param>
        /// <returns>The last <paramref name="n"/> elements, or the entire sequence when it contains fewer than <paramref name="n"/> elements.</returns>
        public static IEnumerable<T> TakeRight<T>(this IEnumerable<T> self, int n)
        {
            var enumerable = self as T[] ?? self.ToArray();
            return enumerable.Skip(Math.Max(0, enumerable.Length - n));
        }
    }
}
