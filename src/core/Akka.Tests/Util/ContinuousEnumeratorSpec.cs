//-----------------------------------------------------------------------
// <copyright file="ContinuousEnumeratorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// Tests to ensure that <see cref="ContinuousEnumerator{T}"/> works as expected.
    /// </summary>
    public class ContinuousEnumeratorSpec
    {
        [Fact]
        public void ContinuousEnumerator_should_not_enumerate_empty_set()
        {
            var testList = new List<int>();
            var continuousEnumerator = testList.GetContinuousEnumerator();
            Assert.False(continuousEnumerator.MoveNext());
        }

        [Fact]
        public void ContinuousEnumerator_should_iterate_list_overlapping()
        {
            var testList = Enumerable.Range(0, 10).ToList();
            var continuousEnumerator = testList.GetContinuousEnumerator();

            //move the iterator around the array length
            for (var i = 0; i < testList.Count && continuousEnumerator.MoveNext(); i++)
            {
                Assert.Equal(testList[i], continuousEnumerator.Current);
            }

            //move the iterator, twice past the array length for good measure
            Assert.True(continuousEnumerator.MoveNext());
            Assert.Equal(testList[0], continuousEnumerator.Current);

            Assert.True(continuousEnumerator.MoveNext());
            Assert.Equal(testList[1], continuousEnumerator.Current);
        }
    }
}

