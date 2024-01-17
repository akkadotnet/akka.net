﻿//-----------------------------------------------------------------------
// <copyright file="LastElementSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
    public class LastElementSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public async Task A_stream_via_LastElement_should_materialize_to_the_last_element_emitted_by_a_finite_nonempty_successful_source()
        {
            var t = Source.From(new[] { 1, 2, 3 })
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var probe = t.Item2;

            probe.Request(3)
                .ExpectNext( 1, 2, 3)
                .ExpectComplete();

            var complete = await lastElement.ShouldCompleteWithin(TimeSpan.FromSeconds(1));
            complete.Should().Be(Option<int>.Create(3));
        }

        [Fact]
        public async Task A_stream_via_LastElement_should_materialize_to_materialize_to_None_for_an_empty_successful_source()
        {
            var t = Source.From(Enumerable.Empty<int>())
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var probe = t.Item2;

            probe.Request(3)
                .ExpectComplete();

            var complete = await lastElement.ShouldCompleteWithin(1.Seconds());
            complete.Should().Be(Option<int>.None);
        }

        [Fact]
        public async Task A_stream_via_LastElement_should_materialize_to_the_last_element_emitted_by_a_source_before_it_failed()
        {
            var t = Source.UnfoldInfinite(1, n => n >= 3 ? throw new Exception() : (n + 1, n + 1))
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(Sink.Aggregate<int, Option<int>>(Option<int>.None, (_, o) => Option<int>.Create(o)), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var lastEmitted = t.Item2;

            var r1 = await lastElement.WaitAsync(1.Seconds());
            var r2 = await lastEmitted.WaitAsync(1.Seconds());
            r1.Should().Be(r2);
        }
    }
}
