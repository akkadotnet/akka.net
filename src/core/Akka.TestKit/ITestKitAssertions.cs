﻿//-----------------------------------------------------------------------
// <copyright file="ITestKitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit
{
// ReSharper disable once InconsistentNaming
    public interface ITestKitAssertions
    {
        void Fail(string format = "", params object[] args);
        void AssertTrue(bool condition, string format = "", params object[] args);
        void AssertFalse(bool condition, string format = "", params object[] args);
        void AssertEqual<T>(T expected, T actual, string format = "", params object[] args);
        void AssertEqual<T>(T expected, T actual, Func<T,T,bool> comparer, string format = "", params object[] args);
    }
}

