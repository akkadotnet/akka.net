//-----------------------------------------------------------------------
// <copyright file="AkkaSpecExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.TestKit.Xunit2.Internals;
using Akka.Util.Internal;
using Xunit;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    /// <summary>
    /// Assertion helpers shared by Akka.NET test specifications.
    /// </summary>
    public static class AkkaSpecExtensions
    {
        /// <summary>
        /// Asserts that this value satisfies the supplied predicate.
        /// </summary>
        /// <typeparam name="T">The type of value being checked.</typeparam>
        /// <param name="self">The value to check.</param>
        /// <param name="isValid">The predicate that must return true for the value.</param>
        /// <param name="message">The message to report if the predicate rejects the value, or null to use a value-based default.</param>
        public static void Should<T>(this T self, Func<T, bool> isValid, string message)
        {
            Assert.True(isValid(self), message ?? "Value did not meet criteria. Value: " + self);
        }

        /// <summary>
        /// Asserts that this collection contains the expected number of items.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="self">The collection whose count is checked.</param>
        /// <param name="expectedCount">The required item count.</param>
        public static void ShouldHaveCount<T>(this IReadOnlyCollection<T> self, int expectedCount)
        {
            Assert.Equal(expectedCount, self.Count);
        }

        /// <summary>
        /// Asserts that two sequences contain equal elements in the same order.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="self">The actual sequence.</param>
        /// <param name="other">The expected sequence.</param>
        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            var otherList = other.ToList();
            var selfList = self.ToList();
            var expected = string.Join(",", otherList.Select(i => $"'{i}'"));
            var actual = string.Join(",", selfList.Select(i => $"'{i}'"));

            Assert.True(selfList.SequenceEqual(otherList), "Expected " + expected + " got " + actual);
        }

        public static async Task ShouldBeAsync<T>(this IAsyncEnumerable<T> self, IEnumerable<T> other)
        {
            if (self is null)
                throw new ArgumentNullException(nameof(self));
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            
            var l1 = new List<string>();
            var l2 = new List<string>();
            var index = 0;

            await using var e1 = self.GetAsyncEnumerator();
            using var e2 = other.GetEnumerator();
            
            var comparer = EqualityComparer<T>.Default;
            while (await e1.MoveNextAsync())
            {
                l1.Add($"'{e1.Current}'");
                if (!e2.MoveNext())
                    throw AkkaEqualException.ForMismatchedValues(
                        l2, l1, $"Input has more elements than expected, differ at index {index}");
                
                l2.Add($"'{e2.Current}'");
                if(!comparer.Equals(e1.Current, e2.Current))
                    throw AkkaEqualException.ForMismatchedValues(
                        l2, l1, $"Input is not equal to expected, differ at index {index}");
                
                index++;
            }

            if (e2.MoveNext())
            {
                l2.Add($"'{e2.Current}'");
                throw AkkaEqualException.ForMismatchedValues(
                    l2, l1, $"Input has less elements than expected, differ at index {index}");
            }
        }

        /// <summary>
        /// Asserts that this value equals the expected value.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="self">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static void ShouldBe<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        /// <summary>
        /// Awaits a value task and asserts that its result equals the expected value.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="self">The value task to await.</param>
        /// <param name="expected">The expected result.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static async Task ShouldBeAsync<T>(this ValueTask<T> self, T expected, string message = null)
        {
            Assert.Equal(expected, await self);
        }

        /// <summary>
        /// Asserts that this value does not equal the specified value.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="self">The actual value.</param>
        /// <param name="expected">The value that must not compare equal to <paramref name="self"/>.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static void ShouldNotBe<T>(this T self, T expected, string message = null)
        {
            Assert.NotEqual(expected, self);
        }

        /// <summary>
        /// Asserts that this value equals the expected value using xUnit value equality.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="self">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static void ShouldBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        /// <summary>
        /// Asserts that this value does not equal the expected value using xUnit value equality.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="self">The actual value.</param>
        /// <param name="expected">The value that must not compare equal to <paramref name="self"/>.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static void ShouldNotBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.NotEqual(expected, self);
        }

        /// <summary>
        /// Asserts that this boolean value is true.
        /// </summary>
        /// <param name="b">The boolean value to check.</param>
        /// <param name="message">The message to report if the value is false.</param>
        public static void ShouldBeTrue(this bool b, string message = null)
        {
            Assert.True(b, message);
        }

        /// <summary>
        /// Asserts that this boolean value is false.
        /// </summary>
        /// <param name="b">The boolean value to check.</param>
        /// <param name="message">The message to report if the value is true.</param>
        public static void ShouldBeFalse(this bool b, string message = null)
        {
            Assert.False(b, message);
        }

        /// <summary>
        /// Asserts that the actual value compares less than the specified value.
        /// </summary>
        /// <typeparam name="T">The comparable value type.</typeparam>
        /// <param name="actual">The value being checked.</param>
        /// <param name="value">The upper bound, which the actual value must be less than.</param>
        /// <param name="message">An optional assertion message.</param>
        public static void ShouldBeLessThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult < 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// Asserts that the actual value compares less than or equal to the specified value.
        /// </summary>
        /// <typeparam name="T">The comparable value type.</typeparam>
        /// <param name="actual">The value being checked.</param>
        /// <param name="value">The upper bound, which the actual value must not exceed.</param>
        /// <param name="message">An optional assertion message.</param>
        public static void ShouldBeLessOrEqualTo<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult <= 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// Asserts that the actual value compares greater than the specified value.
        /// </summary>
        /// <typeparam name="T">The comparable value type.</typeparam>
        /// <param name="actual">The value being checked.</param>
        /// <param name="value">The lower bound, which the actual value must exceed.</param>
        /// <param name="message">An optional assertion message.</param>
        public static void ShouldBeGreaterThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult > 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// Asserts that the actual value compares greater than or equal to the specified value.
        /// </summary>
        /// <typeparam name="T">The comparable value type.</typeparam>
        /// <param name="actual">The value being checked.</param>
        /// <param name="value">The lower bound, which the actual value must meet or exceed.</param>
        /// <param name="message">An optional assertion message.</param>
        public static void ShouldBeGreaterOrEqual<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult >= 0, message ?? "Expected Actual: " + actual + " to be less than " + value);
        }

        /// <summary>
        /// Asserts that this string begins with the specified prefix.
        /// </summary>
        /// <param name="s">The string to inspect.</param>
        /// <param name="start">The required prefix.</param>
        /// <param name="message">An optional assertion message; the current implementation does not use this parameter.</param>
        public static void ShouldStartWith(this string s, string start, string message = null)
        {
            Assert.Equal(s.Substring(0, Math.Min(s.Length, start.Length)), start);
        }

        /// <summary>
        /// Asserts that the sequence contains exactly the expected elements in order.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="actual">The sequence being checked.</param>
        /// <param name="expected">The expected elements in order.</param>
        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            ShouldBe(actual, expected);
        }

        /// <summary>
        /// Asserts asynchronously that the sequence contains exactly the expected elements in order.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="actual">The asynchronous sequence being checked.</param>
        /// <param name="expected">The expected elements in order.</param>
        public static async Task ShouldOnlyContainInOrderAsync<T>(this IAsyncEnumerable<T> actual, params T[] expected)
            => await ShouldBeAsync(actual, expected).ConfigureAwait(false);
        
        /// <summary>
        /// Asserts that the sequence contains exactly the expected elements in order.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="actual">The sequence being checked.</param>
        /// <param name="expected">The expected sequence.</param>
        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, IEnumerable<T> expected)
        {
            ShouldBe(actual, expected);
        }

        /// <summary>
        /// Asserts that an asynchronous function throws an exception of exactly the specified type.
        /// </summary>
        /// <typeparam name="TException">The exact exception type expected.</typeparam>
        /// <param name="func">The asynchronous function expected to throw.</param>
        public static async Task ThrowsAsync<TException>(Func<Task> func)
        {
            var expected = typeof(TException);
            Type actual = null;
            try
            {
                await func();
            }
            catch (Exception e)
            {
                actual = e.GetType();
            }

            Assert.Equal(expected, actual);
        }
    }
}
