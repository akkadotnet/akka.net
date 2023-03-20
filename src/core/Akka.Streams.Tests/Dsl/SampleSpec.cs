//-----------------------------------------------------------------------
// <copyright file="SampleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Akka.TestKit.Extensions;
using Xunit;
using System.Threading.Tasks;
using FluentAssertions.Extensions;

namespace Akka.Streams.Tests.Dsl
{
    public class SampleSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public async Task Sample_Stage_should_return_every_Nth_element_in_stream()
        {
            var list = Enumerable.Range(1, 1000);
            var source = Source.From(list);
            for (var n = 1; n <= 100; n++)
            {
                var future = source
                    .Via(new Sample<int>(n))
                    .RunWith(Sink.Seq<int>(), Sys.Materializer());

                var expected = list.Where(x => x % n == 0);

                var complete = await future.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
            }
        }

        [Fact]
        public async Task Sample_Stage_should_return_elements_using_next_function()
        {
            var num = 0;
            int next() => ++num;

            var future = Source.From(Enumerable.Range(1, 10))
                .Via(new Sample<int>(next))
                .RunWith(Sink.Seq<int>(), Sys.Materializer());

            var complete = await future.ShouldCompleteWithin(3.Seconds());
            complete.Should().BeEquivalentTo(new[] { 1, 3, 6, 10 }, o => o.WithStrictOrdering());
        }

        [Fact]
        public void Sample_Stage_should_throw_exception_when_next_step_less_or_equal_to_0()
        {
            Action aсtion = () =>
            {
                Source.Empty<int>()
                .Via(new Sample<int>(() => 0))
                .RunWith(Sink.Seq<int>(), Sys.Materializer());
            };

            aсtion.Should().Throw<ArgumentException>();

            aсtion = () =>
            {
                Source.Empty<int>()
                .Via(new Sample<int>(() => -1))
                .RunWith(Sink.Seq<int>(), Sys.Materializer());
            };

            aсtion.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Sample_Stage_should_throw_exception_when_max_random_step_less_or_equal_to_0()
        {
            Action aсtion = () =>
            {
                Source.Empty<int>()
                .Via(Sample<int>.Random(0))
                .RunWith(Sink.Seq<int>(), Sys.Materializer());
            };

            aсtion.Should().Throw<ArgumentException>();
        }
    }
}
