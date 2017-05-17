//-----------------------------------------------------------------------
// <copyright file="XunitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.TestKit.Xunit2.Internals;
using Xunit;

namespace Akka.TestKit.Xunit2
{
    /// <summary>
    /// Assertions for xUnit
    /// </summary>
    public class XunitAssertions : ITestKitAssertions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        public void Fail(string format = "", params object[] args)
        {
            Assert.True(false, string.Format(format, args));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="condition">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            Assert.True(condition, string.Format(format, args));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="condition">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        public void AssertFalse(bool condition, string format = "", params object[] args)
        {
            Assert.False(condition, string.Format(format, args));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expected">TBD</param>
        /// <param name="actual">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        /// <exception cref="AkkaEqualException">TBD</exception>
        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            var comparer = new AkkaAssertEqualityComparer<T>();
            if(!comparer.Equals(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expected">TBD</param>
        /// <param name="actual">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        /// <exception cref="AkkaEqualException">TBD</exception>
        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {
            if(!comparer(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }
    }
}