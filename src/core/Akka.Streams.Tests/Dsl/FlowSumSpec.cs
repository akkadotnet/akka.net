//-----------------------------------------------------------------------
// <copyright file="FlowSumSpec.cs" company="Akka.NET Project">
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
    public class FlowSumSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSumSpec(ITestOutputHelper helper):base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static IEnumerable<int> Input => Enumerable.Range(1, 100);
        private static int Expected => Input.Sum();
        private static Source<int, NotUsed> InputSource => Source.From(Input).Where(_ => true).Select(x => x);

        private static Source<int, NotUsed> SumSource
            => InputSource.Sum((i, i1) => i + i1).Where(_ => true).Select(x => x);

        private static Flow<int, int, NotUsed> SumFlow
            => Flow.Create<int>().Where(_ => true).Select(x => x).Sum((i, i1) => i + i1).Where(_ => true).Select(x => x);

        private static Sink<int, Task<int>> SumSink => Sink.Sum<int>((i, i1) => i + i1);

        [Fact]
        public void A_Sum_must_work_when_using_Source_RunSum()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.RunSum((i, i1) => i + i1, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Sum_must_work_when_using_Source_Sum()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = SumSource.RunWith(Sink.First<int>(), Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }
        [Fact]
        public void A_Sum_must_work_when_using_Sink_Sum()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.RunWith(SumSink, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);

            }, Materializer);
        }

        [Fact]
        public void A_Sum_must_work_when_using_Flow_Sum()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = InputSource.Via(SumFlow).RunWith(Sink.First<int>(), Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Sum_must_work_when_using_Source_Sum_and_Flow_Sum_and_Sink_Sum()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = SumSource.Via(SumFlow).RunWith(SumSink, Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be(Expected);

            }, Materializer);
        }

        [Fact]
        public void A_Sum_must_propagate_an_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("test");
                var task = InputSource.Select(x =>
                {
                    if (x > 50)
                        throw error;
                    return x;
                }).RunSum((i, i1) => 0, Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<TestException>().WithMessage("test");
            }, Materializer);
        }

        [Fact]
        public void A_Sum_must_complete_task_with_failure_when_reducing_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("test");
                var task = InputSource.RunSum((x, y) =>
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
