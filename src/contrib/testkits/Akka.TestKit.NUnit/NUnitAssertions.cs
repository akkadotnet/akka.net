//-----------------------------------------------------------------------
// <copyright file="XunitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using NUnit.Framework;

namespace Akka.TestKit.NUnit
{
    /// <summary>
    /// Assertions for NUnit
    /// </summary>
    public class NUnitAssertions  : ITestKitAssertions
    {
        
        public void Fail(string format = "", params object[] args)
        {
            Assert.Fail(format, args);
        }

        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            Assert.IsTrue(condition, format, args);
        }

        public void AssertFalse(bool condition, string format = "", params object[] args)
        {
            Assert.IsFalse(condition, format, args);
        }

        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            Assert.AreEqual(expected, actual, format, args);
        }

        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {
            if (!comparer(expected, actual))
                throw new AssertionException(string.Format("Assert.AreEqual failed. Expected [{0}]. Actual [{1}]. {2}", FormatValue(expected), FormatValue(actual), string.Format(format, args)));
        }

        private static string FormatValue<T>(T expected)
        {
            return ReferenceEquals(expected, null) ? "null" : expected.ToString();
        }
    }
}