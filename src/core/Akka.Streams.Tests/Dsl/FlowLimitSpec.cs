//-----------------------------------------------------------------------
// <copyright file="FlowLimitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowLimitSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowLimitSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Limit_must_produce_empty_sequence_when_source_is_empty_and_n_is_equal_to_zero()
        {
            var input = Enumerable.Empty<int>().ToList();
            var n = input.Count;
            var future = Source.Empty<int>()
                .Limit(n)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.Should().BeNull();
        }

        [Fact]
        public void A_Limit_must_produce_output_that_is_identical_to_the_input_when_n_is_equal_to_input_length()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var n = input.Count;
            var future = Source.From(input)
                .Limit(n)
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void A_Limit_must_produce_output_that_is_identical_to_the_input_when_n_greater_than_input_length()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var n = input.Count + 2; // n > input.Count
            var future = Source.From(input)
                .Limit(n)
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void A_Limit_must_produce_n_messages_before_throwing_a_StreamLimitReachedException_when_n_lower_than_input_size()
        {
            //TODO: check if it actually produces n messages
            var input = Enumerable.Range(1, 6).ToList();
            var n = input.Count - 2; // n < input.Count
            var future = Source.From(input)
                .Limit(n)
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Invoking(f => f.Wait(RemainingOrDefault)).ShouldThrow<StreamLimitReachedException>();
        }

        [Fact]
        public void A_Limit_must_throw_a_StreamLimitReachedException_when_n_lower_than_0()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var n = -1; // n < input.Count
            var future = Source.From(input)
                .Limit(n)
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Invoking(f => f.Wait(RemainingOrDefault)).ShouldThrow<StreamLimitReachedException>();
        }
    }
}
