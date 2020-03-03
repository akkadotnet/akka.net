//-----------------------------------------------------------------------
// <copyright file="FlowAggregateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowAggregateSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowAggregateSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static IEnumerable<int> Input => Enumerable.Range(1, 100);
        private static int Expected => Input.Sum();
        private static Source<int, NotUsed> InputSource => Source.From(Input);
        private static Source<int, NotUsed> AggregateSource => InputSource.Aggregate(0, (sum, i) => sum + i);
        private static Flow<int, int, NotUsed> AggregateFlow => Flow.Create<int>().Aggregate(0, (sum, i) => sum + i);
        private static Sink<int, Task<int>> AggregateSink => Sink.Aggregate<int, int>(0, (sum, i) => sum + i);

        [Fact]
        public void A_Aggregate_must_work_when_using_Source_RunAggregate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.RunAggregate(0, (sum, i) => sum + i, Materializer);
                task.AwaitResult().Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_work_when_using_Source_Aggregate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = AggregateSource.RunWith(Sink.First<int>(), Materializer);
                task.AwaitResult().Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_work_when_using_Sink_Aggregate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.RunWith(AggregateSink, Materializer);
                task.AwaitResult().Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_work_when_using_Flow_Aggregate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.Via(AggregateFlow).RunWith(Sink.First<int>(), Materializer);
                task.AwaitResult().Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_work_when_using_Source_Aggregate_and_Flow_Aggregate_and_Sink_Aggregate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = AggregateSource.Via(AggregateFlow).RunWith(AggregateSink, Materializer);
                task.AwaitResult().Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_propagate_an_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("buh");
                var future = InputSource.Select(x =>
                {
                    if (x > 50)
                        throw error;
                    return x;
                }).RunAggregate(NotUsed.Instance, Keep.None, Materializer);

                future.Invoking(f => f.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>()
                    .And.Should()
                    .Be(error);
            }, Materializer);
        }

        [Fact]
        public void
            A_Aggregate_must_complete_task_with_failure_when_the_aggregateing_function_throws_and_the_supervisor_strategy_decides_to_stop()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("buh");
                var future = InputSource.RunAggregate(0, (x, y) =>
                {
                    if (x > 50)
                        throw error;
                    return x + y;
                }, Materializer);

                future.Invoking(f => f.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>()
                    .And.Should()
                    .Be(error);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_resume_with_the_accumulated_state_when_the_aggregating_funtion_throws_and_the_supervisor_strategy_decides_to_resume()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new Exception("boom");
                var aggregate = Sink.Aggregate(0, (int x, int y) =>
                {
                    if (y == 50)
                        throw error;

                    return x + y;
                });
                var task = InputSource.RunWith(
                    aggregate.WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)),
                    Materializer);
                task.AwaitResult().Should().Be(Expected - 50);
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_resume_and_reset_the_state_when_the_aggregating_funtion_throws_and_the_supervisor_strategy_decides_to_restart()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new Exception("boom");
                var aggregate = Sink.Aggregate(0, (int x, int y) =>
                {
                    if (y == 50)
                        throw error;

                    return x + y;
                });
                var task = InputSource.RunWith(
                    aggregate.WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider)),
                    Materializer);
                task.AwaitResult().Should().Be(Enumerable.Range(51, 50).Sum());
            }, Materializer);
        }

        [Fact]
        public void A_Aggregate_must_complete_task_and_return_zero_given_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Empty<int>())
                    .RunAggregate(0, (acc, element) => acc + element, Materializer);
                task.AwaitResult().ShouldBe(0);
            }, Materializer);
        }
    }
}
