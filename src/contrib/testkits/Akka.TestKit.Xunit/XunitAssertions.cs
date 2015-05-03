﻿//-----------------------------------------------------------------------
// <copyright file="XunitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.TestKit.Xunit.Internals;
using Xunit;

namespace Akka.TestKit.Xunit
{
    /// <summary>
    /// Assertions for xUnit
    /// </summary>
    public class XunitAssertions : ITestKitAssertions
    {
        public void Fail(string format = "", params object[] args)
        {
            Assert.True(false, string.Format(format, args));
        }

        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            Assert.True(condition, string.Format(format, args));
        }

        public void AssertFalse(bool condition, string format = "", params object[] args)
        {

            Assert.False(condition, string.Format(format, args));
        }

        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            var comparer = new AkkaAssertEqualityComparer<T>();
            if(!comparer.Equals(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }

        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {
            if(!comparer(expected, actual))
                throw new AkkaEqualException(expected, actual, format, args);
        }

    }
}

