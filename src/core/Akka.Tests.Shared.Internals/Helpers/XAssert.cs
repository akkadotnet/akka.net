//-----------------------------------------------------------------------
// <copyright file="XAssert.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit.Xunit2;
using Xunit;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    public static class XAssert
    {
        private static readonly XunitAssertions _assertions = new XunitAssertions();
        /// <summary>
        /// Fails the test with the specified reason.
        /// </summary>
        public static void Fail(string reason)
        {
            Assert.True(false, reason);
        }

        /// <summary>
        /// Asserts that both arguments are the same reference.
        /// </summary>
        public static void Same<T>(T expected, T actual, string message)
        {
            Assert.True(ReferenceEquals(expected,actual),message);
        }

        public static void Equal<T>(T expected, T actual, string format, params object[] args)
        {
            _assertions.AssertEqual(expected,actual,format,args);
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            Exception exception = null;
            try
            {
                action();
            }
            catch(AggregateException ex) //need to flatten AggregateExceptions
            {
                var any = ex.Flatten().InnerExceptions.FirstOrDefault(x => x is T);
                if(any!=null) return (T) any;
                exception = ex;
            }
            catch(Exception ex)
            {
                if(ex is T)
                {
                    return (T) ex;
                }
                exception = ex;
            }
            if(exception != null)
                Fail("Expected exception of type " + typeof(T).FullName + ". Received " + exception);
            else
                Fail("Expected exception of type " + typeof(T).Name + " but no exceptions was thrown.");
            return null;    //We'll never reach this line, since calling Fail will throw an exception.
        }


        /// <summary>
        /// Assert passes if two sequences are equal, regardless of the ordering of the items.
        /// 
        /// Equivalent of http://msdn.microsoft.com/en-us/library/microsoft.visualstudio.testtools.unittesting.collectionassert.areequivalent.aspx
        /// </summary>
        public static void Equivalent<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            Assert.True(expected.All(x => actual.Contains(x)) && actual.All(y => expected.Contains(y)));
        }
    }
}

