//-----------------------------------------------------------------------
// <copyright file="FlowAggregateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
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
        public void A_Aggregate_must_complete_task_with_failure_when_Aggregateing_functions_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("buh");
                var future = InputSource.RunAggregate(0, (x,y) =>
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
    }
}
