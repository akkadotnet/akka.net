//-----------------------------------------------------------------------
// <copyright file="CollectionExtensionsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Util
{
    public class CollectionExtensionsSpec
    {
        [Fact]
        public void Slice_should_return_correct_array_subset()
        {
            var items = new List<int>() { 1, 2, 3, 4, 5, 6 };
            var slice = items.Slice(2, 4);
            Assert.True(slice.SequenceEqual(new[] { 3, 4, 5, 6 }));
        }

        [Fact]
        public void From_should_return_empty_set_when_item_not_found()
        {
            var items = new List<int>() {1, 2, 3, 4, 5, 6};
            var from7 = items.From(7).Take(2);
            from7.Count().ShouldBe(0);
        }

        [Fact]
        public void From_should_return_correct_subset_from_array()
        {
            var items = new List<string>() {"meat", "cheese", "beer", "bread"};
            var from = items.From("beer");
            Assert.True(from.SequenceEqual(new[]{ "beer", "bread" }));
        }

        [Fact]
        public void From_should_return_entire_array_if_item_is_at_front_of_array()
        {
            var items = new List<string>() { "meat", "cheese", "beer", "bread" };
            var from = items.From("meat");
            Assert.True(from.SequenceEqual(items));
        }

        [Fact]
        public void Until_should_return_empty_set_when_item_is_at_front_of_array()
        {
            var items = new List<string>() { "meat", "cheese", "beer", "bread" };
            var until = items.Until("meat");
            until.Count().ShouldBe(0);
        }

        [Fact]
        public void Until_should_return_entire_array_when_item_is_not_found_in_array()
        {
            var items = new List<string>() { "meat", "cheese", "beer", "bread" };
            var until = items.Until("wine");
            Assert.True(until.SequenceEqual(items));
        }

        [Fact]
        public void Until_should_return_correct_subset_from_array()
        {
            var items = new List<string>() { "meat", "cheese", "beer", "bread" };
            var until = items.Until("bread");
            Assert.True(until.SequenceEqual(new[]{ "meat", "cheese", "beer" }));
        }
    }
}

