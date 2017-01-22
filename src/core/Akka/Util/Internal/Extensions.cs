﻿//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            return self.Skip(count).ToList();
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
        /// Like selectMany, but alternates between two selectors (starting with even for item 0)
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="self">The input sequence</param>
        /// <param name="evenSelector">The selector to use for items 0, 2, 4 etc.</param>
        /// <param name="oddSelector">The selector to use for items 1, 3, 5 etc.</param>
        /// <returns>TBD</returns>
        public static IEnumerable<TOut> AlternateSelectMany<TIn, TOut>(this IEnumerable<TIn> self,
            Func<TIn, IEnumerable<TOut>> evenSelector, Func<TIn, IEnumerable<TOut>> oddSelector)
        {
            return self.SelectMany((val, i) => i%2 == 0 ? evenSelector(val) : oddSelector(val));
        }

        /// <summary>
        /// Splits a 'dotted path' in its elements, honouring quotes (not splitting by dots between quotes)
        /// </summary>
        /// <param name="path">The input path</param>
        /// <returns>The path elements</returns>
        public static IEnumerable<string> SplitDottedPathHonouringQuotes(this string path)
        {
            return path.Split('\"')
                .AlternateSelectMany(
                    outsideQuote => outsideQuote.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries),
                    insideQuote => new[] { insideQuote });
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
            if (hash.ContainsKey(key)) return hash[key];
            return elseValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="obj">TBD</param>
        /// <param name="elseValue">TBD</param>
        /// <returns>TBD</returns>
        public static T GetOrElse<T>(this T obj, T elseValue)
        {
            if (obj.Equals(default(T)))
                return elseValue;
            return obj;
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
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumerable">TBD</param>
        /// <param name="action">TBD</param>
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
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

