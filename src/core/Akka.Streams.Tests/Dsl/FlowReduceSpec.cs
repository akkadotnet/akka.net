//-----------------------------------------------------------------------
// <copyright file="FlowReduceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowReduceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowReduceSpec(ITestOutputHelper helper):base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static IEnumerable<int> Input => Enumerable.Range(1, 100);
        private static int Expected => Input.Sum();
        private static Source<int, Unit> InputSource => Source.From(Input).Filter(_ => true).Map(x => x);

        private static Source<int, Unit> ReduceSource
            => InputSource.Reduce((i, i1) => i + i1).Filter(_ => true).Map(x => x);

        private static Flow<int, int, Unit> ReduceFlow
            => Flow.Create<int>().Filter(_ => true).Map(x => x).Reduce((i, i1) => i + i1).Filter(_ => true).Map(x => x);

        private static Sink<int, Task<int>> ReduceSink => Sink.Reduce<int>((i, i1) => i + i1);

        [Fact]
        public void A_Reduce_must_work_when_using_Source_RunReduce()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.RunReduce((i, i1) => i + i1, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Reduce_must_work_when_using_Source_Reduce()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = ReduceSource.RunWith(Sink.First<int>(), Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }
        [Fact]
        public void A_Reduce_must_work_when_using_Sink_Reduce()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.RunWith(ReduceSink, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);

            }, Materializer);
        }

        [Fact]
        public void A_Reduce_must_work_when_using_Flow_Reduce()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.Via(ReduceFlow).RunWith(Sink.First<int>(), Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Reduce_must_work_when_using_Source_Reduce_and_Flow_Reduce_and_Sink_Reduce()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = ReduceSource.Via(ReduceFlow).RunWith(ReduceSink, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);

            }, Materializer);
        }

        [Fact]
        public void A_Reduce_must_propagate_an_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("test");
                var task = InputSource.Map(x =>
                {
                    if (x > 50)
                        throw error;
                    return x;
                }).RunReduce((i, i1) => 0, Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<TestException>().WithMessage("test");
            }, Materializer);
        }

        [Fact]
        public void A_Reduce_must_complete_future_with_failure_when_reducing_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("test");
                var task = InputSource.RunReduce((x, y) =>
                {
                    if (x > 50)
                        throw error;
                    return x + y;
                }, Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<TestException>().WithMessage("test");
            }, Materializer);
        }
    }
}
