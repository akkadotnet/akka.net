//-----------------------------------------------------------------------
// <copyright file="LastElementSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Util;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class LastElementSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void A_stream_via_LastElement_should_materialize_to_the_last_element_emitted_by_a_finite_nonempty_successful_source()
        {
            var t = Source.From(new[] { 1, 2, 3 })
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var probe = t.Item2;

            probe.Request(3)
                .ExpectNext(1, 2, 3)
                .ExpectComplete();

            lastElement.AwaitResult(TimeSpan.FromSeconds(1)).Should().Be(new Option<int>(3));
        }

        [Fact]
        public void A_stream_via_LastElement_should_materialize_to_materialize_to_None_for_an_empty_successful_source()
        {
            var t = Source.From(Enumerable.Empty<int>())
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var probe = t.Item2;

            probe.Request(3)
                .ExpectComplete();

            lastElement.AwaitResult(TimeSpan.FromSeconds(1)).Should().Be(Option<int>.None);
        }

        [Fact]
        public void A_stream_via_LastElement_should_materialize_to_the_last_element_emitted_by_a_source_before_it_failed()
        {
            var t = Source.UnfoldInfinite(1, n => n >= 3 ? throw new Exception() : (n + 1, n + 1))
                .ViaMaterialized(new LastElement<int>(), Keep.Right)
                .ToMaterialized(Sink.Aggregate<int, Option<int>>(Option<int>.None, (_, o) => new Option<int>(o)), Keep.Both)
                .Run(Sys.Materializer());

            var lastElement = t.Item1;
            var lastEmitted = t.Item2;

            lastElement.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            lastEmitted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            lastElement.Result.Should().Be(lastEmitted.Result);
        }
    }
}
