//-----------------------------------------------------------------------
// <copyright file="AkkaSpecExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Util.Internal;
using Xunit;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    public static class AkkaSpecExtensions
    {
        public static void Should<T>(this T self, Func<T, bool> isValid, string message)
        {
            Assert.True(isValid(self), message ?? "Value did not meet criteria. Value: " + self);
        }

        public static void ShouldHaveCount<T>(this IReadOnlyCollection<T> self, int expectedCount)
        {
            Assert.Equal(expectedCount, self.Count);
        }

        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            Assert.True(self.SequenceEqual(other), "Expected " + other.Select(i => string.Format("'{0}'", i)).Join(",") + " got " + self.Select(i => string.Format("'{0}'", i)).Join(","));
        }

        public static void ShouldBe<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        public static void ShouldNotBe<T>(this T self, T expected, string message = null)
        {
            Assert.NotEqual(expected, self);
        }

        public static void ShouldBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.Same(expected, self);
        }

        public static void ShouldNotBeSame<T>(this T self, T expected, string message = null)
        {
            Assert.NotSame(expected, self);
        }

        public static void ShouldBeTrue(this bool b, string message = null)
        {
            Assert.True(b);
        }

        public static void ShouldBeFalse(this bool b, string message = null)
        {
            Assert.False(b);
        }

        public static void ShouldBeLessThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult < 0, "Expected Actual: " + actual + " to be less than " + value);
        }

        public static void ShouldBeLessOrEqualTo<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult <= 0, "Expected Actual: " + actual + " to be less than " + value);
        }

        public static void ShouldBeGreaterThan<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult > 0, "Expected Actual: " + actual + " to be less than " + value);
        }

        public static void ShouldBeGreaterOrEqual<T>(this T actual, T value, string message = null) where T : IComparable<T>
        {
            var comparisonResult = actual.CompareTo(value);
            Assert.True(comparisonResult >= 0, "Expected Actual: " + actual + " to be less than " + value);
        }

        public static void ShouldStartWith(this string s, string start, string message = null)
        {
            Assert.Equal(s.Substring(0, Math.Min(s.Length, start.Length)), start);
        }

        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            ShouldBe(actual, expected);
        }

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

