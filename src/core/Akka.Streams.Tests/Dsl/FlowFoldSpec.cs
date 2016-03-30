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
    public class FlowFoldSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowFoldSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static IEnumerable<int> Input => Enumerable.Range(1, 100);
        private static int Expected => Input.Sum();
        private static Source<int, Unit> InputSource => Source.From(Input).Filter(_ => true).Map(x => x);

        private static Source<int, Unit> FoldSource =>
            InputSource.Fold(0, (sum, i) => sum + i).Filter(_ => true).Map(x => x);

        private static Flow<int, int, Unit> FoldFlow =>
            Flow.Create<int>().Filter(_ => true).Map(x => x).Fold(0, (sum, i) => sum + i).Filter(_ => true).Map(x => x);

        private static Sink<int, Task<int>> FoldSink => Sink.Fold<int, int>(0, (sum, i) => sum + i);

        [Fact]
        public void A_Fold_must_work_when_using_Source_RunFold()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.RunFold(0, (sum, i) => sum + i, Materializer);
                task.Wait(TimeSpan.FromSeconds(3));
                task.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Fold_must_work_when_using_Source_Fold()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = FoldSource.RunWith(Sink.First<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3));
                task.Result.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public void A_Fold_must_work_when_using_Sink_Fold()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.RunWith(FoldSink, Materializer);
                task.Wait(TimeSpan.FromSeconds(3));
                task.Result.Should().Be(Expected);
            }, Materializer);

        }

        [Fact]
        public void A_Fold_must_work_when_using_Flow_Fold()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = InputSource.Via(FoldFlow).RunWith(Sink.First<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3));
                task.Result.Should().Be(Expected);
            }, Materializer);

        }

        [Fact]
        public void A_Fold_must_work_when_using__Source_Fold_and_Flow_Fold_and_Sink_Fold()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = FoldSource.Via(FoldFlow).RunWith(FoldSink, Materializer);
                task.Wait(TimeSpan.FromSeconds(3));
                task.Result.Should().Be(Expected);
            }, Materializer);

        }

        [Fact]
        public void A_Fold_must_propagate_an_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("buh");
                var future = InputSource.Map(x =>
                {
                    if (x > 50)
                        throw error;
                    return x;
                }).RunFold(Unit.Instance, Keep.None, Materializer);

                future.Invoking(f => f.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>()
                    .And.Should()
                    .Be(error);
            }, Materializer);

        }

        [Fact]
        public void A_Fold_must_complete_future_with_failure_when_folding_functions_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("buh");
                var future = InputSource.RunFold(0, (x,y) =>
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
