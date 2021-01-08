//-----------------------------------------------------------------------
// <copyright file="XunitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.TestKit.Xunit2.Internals;
using Xunit;

namespace Akka.TestKit.Xunit2
{
    /// <summary>
    /// This class contains several common assert patterns used throughout this testkit.
    /// </summary>
    public class XunitAssertions : ITestKitAssertions
    {
        /// <summary>
        /// Fails an assertion without checking any conditions.
        /// </summary>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public void Fail(string format = "", params object[] args)
        {
            Assert.True(false, string.Format(format, args));
        }

        /// <summary>
        /// Verifies that a specified <paramref name="condition"/> is true.
        /// </summary>
        /// <param name="condition">The condition that is being verified.</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            Assert.True(condition, string.Format(format, args));
        }

        /// <summary>
        /// Verifies that a specified <paramref name="condition"/> is false.
        /// </summary>
        /// <param name="condition">The condition that is being verified.</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public void AssertFalse(bool condition, string format = "", params object[] args)
        {
            Assert.False(condition, string.Format(format, args));
        }

        /// <summary>
        /// Verifies that the two specified values (<paramref name="expected"/> and <paramref name="actual"/>
        /// are equal using the built-in comparison function <see cref="AkkaAssertEqualityComparer{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type that is being compared.</typeparam>
        /// <param name="expected">The expected value of the object</param>
        /// <param name="actual">The actual value of the object</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        /// <exception cref="AkkaEqualException">
        /// This exception is thrown when the two specified values are not equal.
        /// </exception>
        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            var comparer = new AkkaAssertEqualityComparer<T>();
            if(!comparer.Equals(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }

        /// <summary>
        /// Verifies that the two specified values (<paramref name="expected"/> and <paramref name="actual"/>
        /// are equal using a specified comparison function <paramref name="comparer"/>.
        /// </summary>
        /// <typeparam name="T">The type that is being compared.</typeparam>
        /// <param name="expected">The expected value of the object</param>
        /// <param name="actual">The actual value of the object</param>
        /// <param name="comparer">The function used to compare the two specified values.</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        /// <exception cref="AkkaEqualException">
        /// This exception is thrown when the two specified values are not equal.
        /// </exception>
        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {
            if(!comparer(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }
    }
}
