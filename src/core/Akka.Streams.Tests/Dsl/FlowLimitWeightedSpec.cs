//-----------------------------------------------------------------------
// <copyright file="FlowLimitWeightedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowLimitWeightedSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowLimitWeightedSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2,16);
            Materializer = ActorMaterializer.Create(Sys,settings);
        }

        [Fact]
        public void Limit_must_produce_empty_sequence_regardless_of_cost_when_source_is_empty_and_n_equals_0()
        {
            var input = new List<int>();
            var n = input.Count;
            Func<int, long> costFunction = e => 999999L; // set to an arbitrarily big value
            var future = Source.From(input)
                .LimitWeighted(n, costFunction)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.Should().BeNull();
        }

        [Fact]
        public void Limit_must_always_exhaust_a_source_regardless_of_n_as_long_as_n_is_greater_than_0_if_cost_is_0()
        {
            var input = Enumerable.Range(1, 15).ToList();
            var n = 1; // must not matter since costFn always evaluates to 0
            Func<int, long> costFunction = e => 0L;
            var future = Source.From(input)
                .LimitWeighted(n, costFunction)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);

        }

        [Fact]
        public void Limit_must_exhaust_source_if_n_equals_to_input_length_and_cost_is_1()
        {
            var input = Enumerable.Range(1, 16).ToList();
            var n = input.Count;
            Func<int, long> costFunction = e => 1L;
            var future = Source.From(input)
                .LimitWeighted(n, costFunction)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void Limit_must_exhaust_a_source_if_n_greater_or_equal_accumulated_cost()
        {
            var input = new[] {"this", "is", "some", "string"};
            var n = input.Length;
            Func<string, long> costFunction = e => 1L;
            var future = Source.From(input)
                .LimitWeighted(n, costFunction)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<string>>(), Materializer);

            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void Limit_must_throw_a_StreamLimitReachedException_when_n_lower_than_accumulated_cost()
        {
            var input = new[] { "this", "is", "some", "string" };
            var n = input.Aggregate((s, s1) => s + s1).Length - 1;
            Func<string, long> costFunction = e => e.Length;
            var future = Source.From(input)
                .LimitWeighted(n, costFunction)
                .Grouped(1000)
                .RunWith(Sink.FirstOrDefault<IEnumerable<string>>(), Materializer);

            future.Invoking(f => f.Wait(RemainingOrDefault)).ShouldThrow<StreamLimitReachedException>();
        }
    }
}
