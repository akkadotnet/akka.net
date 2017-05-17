//-----------------------------------------------------------------------
// <copyright file="ITestKitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit
{
// ReSharper disable once InconsistentNaming
    /// <summary>
    /// TBD
    /// </summary>
    public interface ITestKitAssertions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        void Fail(string format = "", params object[] args);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="condition">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        void AssertTrue(bool condition, string format = "", params object[] args);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="condition">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        void AssertFalse(bool condition, string format = "", params object[] args);
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="expected">TBD</param>
        /// <param name="actual">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        void AssertEqual<T>(T expected, T actual, string format = "", params object[] args);
        /// <summary>
        /// TBD
        /// </summary>
        ///<typeparam name="T">TBD</typeparam>
        /// <param name="expected">TBD</param>
        /// <param name="actual">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        void AssertEqual<T>(T expected, T actual, Func<T,T,bool> comparer, string format = "", params object[] args);
    }
}