//-----------------------------------------------------------------------
// <copyright file="MSpecAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Machine.Specifications;

namespace Akka.TestKit.MSpec
{
    /// <summary>
    /// Assertions for MSpec
    /// </summary>
    public class MSpecAssertions : ITestKitAssertions
    {
        
        public void Fail(string format = "", params object[] args)
        {
            throw new SpecificationException(string.Format(format, args));
        }

        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            condition.ShouldBeTrue();
        }

        public void AssertFalse(bool condition, string format = "", params object[] args)
        {
            condition.ShouldBeFalse();
        }

        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            actual.ShouldEqual(expected);
        }

        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {
            if (!comparer(expected, actual))
                throw new SpecificationException(string.Format("Assert.AreEqual failed. Expected [{0}]. Actual [{1}]. {2}", FormatValue(expected), FormatValue(actual), string.Format(format, args)));
        }

        private static string FormatValue<T>(T expected)
        {
            return ReferenceEquals(expected, null) ? "null" : expected.ToString();
        }
    }
}