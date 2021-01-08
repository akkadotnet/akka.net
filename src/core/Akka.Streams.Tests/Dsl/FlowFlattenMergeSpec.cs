//-----------------------------------------------------------------------
// <copyright file="FlowFlattenMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowFlattenMergeSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowFlattenMergeSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private Source<int, NotUsed> Src10(int i) => Source.From(Enumerable.Range(i, 10));

        private Source<int, NotUsed> Blocked => Source.FromTask(new TaskCompletionSource<int>().Task);

        private Sink<int, Task<IEnumerable<int>>> ToSeq
            => Flow.Create<int>().Grouped(1000).ToMaterialized(Sink.First<IEnumerable<int>>(), Keep.Right);

        private Sink<int, Task<ImmutableHashSet<int>>> ToSet =>
                Flow.Create<int>()
                    .Grouped(1000)
                    .Select(x => x.ToImmutableHashSet())
                    .ToMaterialized(Sink.First<ImmutableHashSet<int>>(), Keep.Right);

        [Fact]
        public void A_FlattenMerge_must_work_in_the_nominal_case()
        {
            var task = Source.From(new[] {Src10(0), Src10(10), Src10(20), Src10(30)})
                .MergeMany(4, s => s)
                .RunWith(ToSet, Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 40));
        }

        [Fact]
        public void A_FlattenMerge_must_not_be_held_back_by_one_slow_stream()
        {
            var task = Source.From(new[] { Src10(0), Src10(10), Blocked, Src10(20), Src10(30) })
                .MergeMany(3, s => s)
                .Take(40)
                .RunWith(ToSet, Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 40));
        }

        [Fact]
        public void A_FlattenMerge_must_respect_breadth()
        {
            var task = Source.From(new[] { Src10(0), Src10(10), Src10(20), Blocked, Blocked, Src10(30) })
                .MergeMany(3, s => s)
                .Take(40)
                .RunWith(ToSeq, Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

            task.Result.Take(30).ShouldAllBeEquivalentTo(Enumerable.Range(0, 30));
            task.Result.Drop(30).ShouldAllBeEquivalentTo(Enumerable.Range(30, 10));
        }

        [Fact]
        public void A_FlattenMerge_must_propagate_early_failure_from_main_stream()
        {
            var ex = new TestException("buh");
            var future = Source.Failed<Source<int, NotUsed>>(ex)
                .MergeMany(1, x => x)
                .RunWith(Sink.First<int>(), Materializer);

            future.Invoking(f => f.Wait(TimeSpan.FromSeconds(1))).ShouldThrow<TestException>().And.Should().Be(ex);
        }

        [Fact]
        public void A_FlattenMerge_must_propagate_late_failure_from_main_stream()
        {
            var ex = new TestException("buh");

            var future = Source.Combine(Source.From(new[] {Blocked, Blocked}), Source.Failed<Source<int, NotUsed>>(ex),
                i => new Merge<Source<int, NotUsed>>(i))
                .MergeMany(10, x => x)
                .RunWith(Sink.First<int>(), Materializer);

            future.Invoking(f => f.Wait(TimeSpan.FromSeconds(1))).ShouldThrow<TestException>().And.Should().Be(ex);
        }

        [Fact]
        public void A_FlattenMerge_must_propagate_failure_from_map_function()
        {
            var ex = new TestException("buh");
            var future = Source.From(Enumerable.Range(1, 3))
                .MergeMany(10, x =>
                {
                    if (x == 3)
                        throw ex;
                    return Blocked;
                })
                .RunWith(Sink.First<int>(), Materializer);

            future.Invoking(f => f.Wait(TimeSpan.FromSeconds(1))).ShouldThrow<TestException>().And.Should().Be(ex);
        }

        [Fact]
        public void A_FlattenMerge_must_bubble_up_substream_exceptions()
        {
            var ex = new TestException("buh");
            var future = Source.From(new[] { Blocked, Blocked, Source.Failed<int>(ex) })
                .MergeMany(10, x => x)
                .RunWith(Sink.First<int>(), Materializer);

            future.Invoking(f => f.Wait(TimeSpan.FromSeconds(1))).ShouldThrow<TestException>().And.Should().Be(ex);
        }

        [Fact]
        public void A_FlattenMerge_must_cancel_substreams_when_failing_from_main_stream()
        {
            var p1 = this.CreatePublisherProbe<int>();
            var p2 = this.CreatePublisherProbe<int>();
            var ex = new TestException("buh");
            var p = new TaskCompletionSource<Source<int, NotUsed>>();

            Source.Combine(
                Source.From(new[] {Source.FromPublisher(p1), Source.FromPublisher(p2)}),
                Source.FromTask(p.Task), i => new Merge<Source<int, NotUsed>>(i))
                .MergeMany(5, x => x)
                .RunWith(Sink.First<int>(), Materializer);

            p1.ExpectRequest();
            p2.ExpectRequest();
            p.SetException(ex);
            p1.ExpectCancellation();
            p2.ExpectCancellation();
        }

        [Fact]
        public void A_FlattenMerge_must_cancel_substreams_when_failing_from_substream()
        {
            var p1 = this.CreatePublisherProbe<int>();
            var p2 = this.CreatePublisherProbe<int>();
            var ex = new TestException("buh");
            var p = new TaskCompletionSource<int>();


            Source.From(new[]
            {Source.FromPublisher(p1), Source.FromPublisher(p2), Source.FromTask(p.Task)})
                .MergeMany(5, x => x)
                .RunWith(Sink.First<int>(), Materializer);

            p1.ExpectRequest();
            p2.ExpectRequest();
            p.SetException(ex);
            p1.ExpectCancellation();
            p2.ExpectCancellation();
        }

        [Fact]
        public void A_FlattenMerge_must_cancel_substreams_when_failing_map_function()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithInputBuffer(1, 1);
            var materializer = ActorMaterializer.Create(Sys, settings);
            var p = this.CreatePublisherProbe<int>();
            var ex = new TestException("buh");
            var latch = new TestLatch();

            Source.From(Enumerable.Range(1, 3)).MergeMany(10, i =>
            {
                if (i == 1)
                    return Source.FromPublisher(p);

                latch.Ready(TimeSpan.FromSeconds(3));
                throw ex;
            }).RunWith(Sink.First<int>(), materializer);
            p.ExpectRequest();
            latch.CountDown();
            p.ExpectCancellation();
        }

        [Fact]
        public void A_FlattenMerge_must_cancel_substreams_when_being_cancelled()
        {
            var p1 = this.CreatePublisherProbe<int>();
            var p2 = this.CreatePublisherProbe<int>();

            var sink = Source.From(new[] {Source.FromPublisher(p1), Source.FromPublisher(p2)})
                .MergeMany(5, x => x)
                .RunWith(this.SinkProbe<int>(), Materializer);

            sink.Request(1);
            p1.ExpectRequest();
            p2.ExpectRequest();
            sink.Cancel();
            p1.ExpectCancellation();
            p2.ExpectCancellation();
        }

        [Fact]
        public void A_FlattenMerge_must_work_with_many_concurrently_queued_events()
        {
            const int noOfSources = 100;
            var p = Source.From(Enumerable.Range(0, noOfSources).Select(i => Src10(10*i)))
                .MergeMany(int.MaxValue, x => x)
                .RunWith(this.SinkProbe<int>(), Materializer);

            p.EnsureSubscription();
            p.ExpectNoMsg(TimeSpan.FromSeconds(1));

            var elems = p.Within(TimeSpan.FromSeconds(1), () => Enumerable.Range(1, noOfSources * 10).Select(_ => p.RequestNext()).ToArray());
            p.ExpectComplete();
            elems.ShouldAllBeEquivalentTo(Enumerable.Range(0, noOfSources * 10));
        }

        private sealed class AttibutesSourceStage : GraphStage<SourceShape<Attributes>>
        {
            #region Logic

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly AttibutesSourceStage _stage;
                private readonly Attributes _inheritedAttributes;

                public Logic(AttibutesSourceStage stage, Attributes inheritedAttributes) : base(stage.Shape)
                {
                    _stage = stage;
                    _inheritedAttributes = inheritedAttributes;

                    SetHandler(stage.Out, this);
                }

                public override void OnPull()
                {
                    Push(_stage.Out, _inheritedAttributes);
                    CompleteStage();
                }
            }

            #endregion


            public AttibutesSourceStage()
            {
                Shape = new SourceShape<Attributes>(Out);
            }

            private Outlet<Attributes> Out { get; } = new Outlet<Attributes>("AttributesSource.out");

            public override SourceShape<Attributes> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
        }

        [Fact]
        public void A_FlattenMerge_must_propagate_attributes_to_inner_stream()
        {
            var attributesSource = Source.FromGraph(new AttibutesSourceStage());

            this.AssertAllStagesStopped(() =>
            {
                var task = Source.Single(attributesSource.AddAttributes(Attributes.CreateName("inner")))
                    .MergeMany(1, x => x)
                    .AddAttributes(Attributes.CreateName("outer"))
                    .RunWith(Sink.First<Attributes>(), Materializer);

                var attributes = task.AwaitResult().AttributeList.ToList();
                var innerName = new Attributes.Name("inner");
                var outerName = new Attributes.Name("outer");

                attributes.Should().Contain(innerName);
                attributes.Should().Contain(outerName);
                attributes.IndexOf(outerName).Should().BeLessThan(attributes.IndexOf(innerName));

            }, Materializer);

        }

        [Fact]
        public void A_FlattenMerge_must_bubble_up_substream_materialization_exception()
        {
            this.AssertAllStagesStopped(() => {
                var matFail = new TestException("fail!");

                var task = Source.Single("whatever")
                    .MergeMany(4, x => Source.FromGraph(new FailingInnerMat(matFail)))
                    .RunWith(Sink.Ignore<string>(), Materializer);

                try
                {
                    task.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException) { }

                task.IsFaulted.ShouldBe(true);
                task.Exception.ShouldNotBe(null);
                task.Exception.InnerException.ShouldBeEquivalentTo(matFail);

            }, Materializer);
        }

        private sealed class FailingInnerMat : GraphStage<SourceShape<string>>
        {
            #region Logic
            private sealed class FailingLogic : GraphStageLogic
            {
                public FailingLogic(Shape shape, TestException ex) : base(shape)
                {
                    throw ex;
                }
            }
            #endregion

            public FailingInnerMat(TestException ex)
            {
                var outlet = new Outlet<string>("out");
                Shape = new SourceShape<string>(outlet);
                _ex = ex;
            }

            private readonly TestException _ex;

            public override SourceShape<string> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new FailingLogic(Shape, _ex);
            }
        }
    }
}
