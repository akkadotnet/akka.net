//-----------------------------------------------------------------------
// <copyright file="ImmutableAvlTreeMapTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.TestKit;
using Akka.Util.Internal.Collections;
using Xunit;

namespace Akka.Tests.Util.Internal.Collections
{
    /*TODO: this class is not used*/public class ImmutableAvlTreeMapTests
    {
        [Fact]
        public void AddingSamekeyTwiceAreNotAllowed()
        {
            var tree = ImmutableTreeMap<int, string>.Empty
                .Add(3, "c")
                .Add(2, "b");

            Assert.Throws<InvalidOperationException>(() => tree.Add(3, "XXXX"));
        }

        [Fact]
        public void Removing_last_item_returns_empty_map()
        {
            var map = ImmutableTreeMap<int, string>.Empty
                .Add(1, "a");

            ImmutableTreeMap<int, string> removedMap;
            map.TryRemove(1, out removedMap).ShouldBeTrue();
            removedMap.IsEmpty.ShouldBeTrue();

            ImmutableTreeMap<int, string> removedMap2;
            removedMap.TryRemove(1, out removedMap2).ShouldBeFalse();
            removedMap2.IsEmpty.ShouldBeTrue();
        }
    }
}

