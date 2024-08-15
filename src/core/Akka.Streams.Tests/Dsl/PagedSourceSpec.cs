﻿//-----------------------------------------------------------------------
// <copyright file="PagedSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Util;
using Akka.TestKit.Extensions;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class PagedSourceSpec
    {
        public class MultiplesOfTwo : Akka.TestKit.Xunit2.TestKit
        {
            private class MultiplesOfTwoPage
            {
                private readonly int? _size;
                private const int _itemsPerPage = 2;

                public MultiplesOfTwoPage(int? size = null)
                {
                    _size = size;
                }

                public Task<PagedSource.Page<int, int>> Page(int key)
                {
                    var indices = Enumerable.Range(key * _itemsPerPage, _itemsPerPage);
                    var filteredIndices = _size.HasValue ? indices.Where(x => x < _size.Value) : indices;

                    return Task.FromResult(new PagedSource.Page<int, int>(filteredIndices.Select(x => x * 2), Option<int>.Create(key + 1)));
                }
            }

            [Fact]
            public async Task PagedSource_should_return_the_items_in_the_proper_order()
            {
                var source = PagedSource.Create(0, new MultiplesOfTwoPage().Page);
                var t = source.Take(3).RunWith(Sink.Seq<int>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { 0, 2, 4 }, o => o.WithStrictOrdering());
            }

            [Fact]
            public async Task PagedSource_should_return_not_more_items_then_available()
            {
                var source = PagedSource.Create(0, new MultiplesOfTwoPage(4).Page);
                var t = source.Take(10).RunWith(Sink.Seq<int>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().HaveCount(4);
            }
        }

        public class IndexedStringPages : Akka.TestKit.Xunit2.TestKit
        {
            private readonly Source<string, NotUsed> _source = PagedSource.Create
            (
                1,
                i => Task.FromResult(new PagedSource.Page<string, int>(Page(i), Option<int>.Create(i + 1)))
            );

            private static IEnumerable<string> Page(int key)
            {
                if (key == 1)
                    return new[] { "a", "b", "c" };

                if (key == 2)
                    return new[] { "d", "e" };

                return null;
            }

            [Fact]
            public async Task PagedSource_should_return_the_items_in_the_proper_order()
            {
                var t = _source.Take(4).RunWith(Sink.Seq<string>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { "a", "b", "c", "d" }, o => o.WithStrictOrdering());
            }

            [Fact]
            public async Task PagedSource_should_close_stream_when_received_empty_page()
            {
                var t = _source.RunWith(Sink.Seq<string>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { "a", "b", "c", "d", "e" }, o => o.WithStrictOrdering());
            }
        }

        public class LinkedIntPages : Akka.TestKit.Xunit2.TestKit
        {
            private readonly Source<int, NotUsed> _source = PagedSource.Create
            (
                "first",
                key =>
                {
                    var t = Page(key);
                    var items = t.Item1;
                    var next = t.Item2;

                    return Task.FromResult(new PagedSource.Page<int, string>(items, next == "" ? Option<string>.None : Option<string>.Create(next)));
                }
            );

            private static (int[], string) Page(string key)
            {
                if (key == "first")
                    return (new[] { 1, 2 }, "second");

                if (key == "second")
                    return (new[] { 3, 4, 5 }, "");

                return (new[] { 6 }, "");
            }

            [Fact]
            public async Task PagedSource_should_return_the_items_in_the_proper_order()
            {
                var t = _source.Take(4).RunWith(Sink.Seq<int>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 }, o => o.WithStrictOrdering());
            }

            [Fact]
            public async Task PagedSource_should_close_stream_when_received_empty_link()
            {
                var t = _source.RunWith(Sink.Seq<int>(), Sys.Materializer());

                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 }, o => o.WithStrictOrdering());
            }
        }
    }
}
