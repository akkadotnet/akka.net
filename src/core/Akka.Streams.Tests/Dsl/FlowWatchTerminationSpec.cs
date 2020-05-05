//-----------------------------------------------------------------------
// <copyright file="FlowWatchTerminationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowWatchTerminationSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowWatchTerminationSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_WatchTermination_must_complete_the_future_when_stream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    Source.From(Enumerable.Range(1, 4))
                        .WatchTermination(Keep.Right)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(Materializer);
                var future = t.Item1;
                var p = t.Item2;

                p.Request(4).ExpectNext(1, 2, 3, 4);
                future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                p.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_WatchTermination_must_complete_the_future_when_stream_is_cancelled_from_downstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    Source.From(Enumerable.Range(1, 4))
                        .WatchTermination(Keep.Right)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(Materializer);
                var future = t.Item1;
                var p = t.Item2;

                p.Request(3).ExpectNext(1, 2, 3);
                p.Cancel();
                future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public void A_WatchTermination_must_fail_the_future_when_stream_is_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var ex = new Exception("Stream failed.");
                var t = this.SourceProbe<int>().WatchTermination(Keep.Both).To(Sink.Ignore<int>()).Run(Materializer);
                var p = t.Item1;
                var future = t.Item2;
                p.SendNext(1);
                p.SendError(ex);
                future.Invoking(f => f.Wait()).ShouldThrow<Exception>().WithMessage("Stream failed.");
            }, Materializer);
        }

        [Fact]
        public void A_WatchTermination_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    Source.Empty<int>()
                        .WatchTermination(Keep.Right)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(Materializer);
                var future = t.Item1;
                var p = t.Item2;
                p.Request(1);
                future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            }, Materializer);
        }

        [Fact(Skip = "We need a way to combine multiple sources with different materializer types")]
        public void A_WatchTermination_must_complete_the_future_for_graph()
        {
            this.AssertAllStagesStopped(() =>
            {
                //var first = this.SourceProbe<int>().WatchTermination(Keep.Both);
                //var second = Source.From(Enumerable.Range(2, 4)).MapMaterializedValue(new Func<NotUsed, (TestPublisher.Probe<int>, Task)>(_ => null));
                
                //var t = Source.FromGraph(
                //    GraphDsl.Create<SourceShape<int>, (TestPublisher.Probe<int>, Task)>(b =>
                //    {
                //        var c = b.Add(new Merge<int, int>(2));
                //        b.From(first).To(c.In(0));
                //        b.From(second).To(c.In(1));

                //        return new SourceShape<int>(c.Out);
                //    }))
                //    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                //    .Run(Materializer);

                //var sourceProbe = t.Item1.Item1;
                //var future = t.Item1.Item2;
                //var sinkProbe = t.Item2;

                //sinkProbe.Request(5);
                //sourceProbe.SendNext(1);
                //sinkProbe.ExpectNext(1);
                //ExpectNoMsg(TimeSpan.FromMilliseconds(300));

                //sourceProbe.SendComplete();
                //sinkProbe.ExpectNextN(new[] {2, 3, 4, 5}).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_WatchTermination_must_fail_task_when_abruptly_terminated()
        {
            var materializer = ActorMaterializer.Create(Sys);

            var t = this.SourceProbe<int>().WatchTermination(Keep.Both).To(Sink.Ignore<int>()).Run(materializer);
            var task = t.Item2;

            materializer.Shutdown();

            Action a = () => task.Wait(TimeSpan.FromSeconds(3));
            a.ShouldThrow<AbruptTerminationException>();
        }
    }
}
