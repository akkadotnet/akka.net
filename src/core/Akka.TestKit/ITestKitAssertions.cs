//-----------------------------------------------------------------------
// <copyright file="ITestKitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit
{
    /// <summary>
    /// This interface defines several common assert patterns used in the various testkits.
    /// </summary>
    public interface ITestKitAssertions
    {
        /// <summary>
        /// Fails an assertion without checking any conditions.
        /// </summary>
        /// <param name="format">A template string to display if the assertion fails</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        void Fail(string format = "", params object[] args);
        /// <summary>
        /// Verifies that a specified <paramref name="condition"/> is true.
        /// </summary>
        /// <param name="condition">The condition that is being verified</param>
        /// <param name="format">A template string to display if the assertion fails</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        void AssertTrue(bool condition, string format = "", params object[] args);
        /// <summary>
        /// Verifies that a specified <paramref name="condition"/> is false.
        /// </summary>
        /// <param name="condition">The condition that is being verified.</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        void AssertFalse(bool condition, string format = "", params object[] args);
        /// <summary>
        /// Verifies that the two specified values (<paramref name="expected"/> and <paramref name="actual"/> are equal.
        /// </summary>
        /// <typeparam name="T">The type that is being compared.</typeparam>
        /// <param name="expected">The expected value of the object</param>
        /// <param name="actual">The actual value of the object</param>
        /// <param name="format">A template string to display if the assertion fails.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        void AssertEqual<T>(T expected, T actual, string format = "", params object[] args);
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
        void AssertEqual<T>(T expected, T actual, Func<T,T,bool> comparer, string format = "", params object[] args);
    }
}
