//-----------------------------------------------------------------------
// <copyright file="FlowFlattenMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowFlattenMergeSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowFlattenMergeSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static Source<int, NotUsed> Src10(int i) => Source.From(Enumerable.Range(i, 10));

        private static Source<int, NotUsed> Blocked => Source.FromTask(new TaskCompletionSource<int>().Task);

        private static Sink<int, Task<IEnumerable<int>>> ToSeq
            => Flow.Create<int>().Grouped(1000).ToMaterialized(Sink.First<IEnumerable<int>>(), Keep.Right);

        private static Sink<int, Task<ImmutableHashSet<int>>> ToSet =>
                Flow.Create<int>()
                    .Grouped(1000)
                    .Select(x => x.ToImmutableHashSet())
                    .ToMaterialized(Sink.First<ImmutableHashSet<int>>(), Keep.Right);

        [Fact]
        public async Task A_FlattenMerge_must_work_in_the_nominal_case()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(new[] {Src10(0), Src10(10), Src10(20), Src10(30)})
                    .MergeMany(4, s => s)
                    .RunWith(ToSet, Materializer);
                await task.ShouldCompleteWithin(1.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(0, 40));
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_not_be_held_back_by_one_slow_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(new[] { Src10(0), Src10(10), Blocked, Src10(20), Src10(30) })
                    .MergeMany(3, s => s)
                    .Take(40)
                    .RunWith(ToSet, Materializer);
                await task.ShouldCompleteWithin(1.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(0, 40));
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_respect_breadth()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(new[] { Src10(0), Src10(10), Src10(20), Blocked, Blocked, Src10(30) })
                    .MergeMany(3, s => s)
                    .Take(40)
                    .RunWith(ToSeq, Materializer);

                await task.ShouldCompleteWithin(1.Seconds());

                task.Result.Take(30).Should().BeEquivalentTo(Enumerable.Range(0, 30));
                task.Result.Drop(30).Should().BeEquivalentTo(Enumerable.Range(30, 10));
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_propagate_early_failure_from_main_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var ex = new TestException("buh");
                var future = Source.Failed<Source<int, NotUsed>>(ex)
                    .MergeMany(1, x => x)
                    .RunWith(Sink.First<int>(), Materializer);

                (await Awaiting(() => future.ShouldCompleteWithin(1.Seconds()))
                    .Should().ThrowAsync<TestException>()).And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_propagate_late_failure_from_main_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var ex = new TestException("buh");

                var future = Source.Combine(Source.From(new[] {Blocked, Blocked}), Source.Failed<Source<int, NotUsed>>(ex),
                        i => new Merge<Source<int, NotUsed>>(i))
                    .MergeMany(10, x => x)
                    .RunWith(Sink.First<int>(), Materializer);

                (await Awaiting(() => future.ShouldCompleteWithin(1.Seconds()))
                    .Should().ThrowAsync<TestException>()).And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_propagate_failure_from_map_function()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                (await Awaiting(() => future.ShouldCompleteWithin(1.Seconds()))
                    .Should().ThrowAsync<TestException>()).And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_bubble_up_subStream_exceptions()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var ex = new TestException("buh");
                var future = Source.From(new[] { Blocked, Blocked, Source.Failed<int>(ex) })
                    .MergeMany(10, x => x)
                    .RunWith(Sink.First<int>(), Materializer);

                (await Awaiting(() => future.ShouldCompleteWithin(1.Seconds()))
                    .Should().ThrowAsync<TestException>()).And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_cancel_subStreams_when_failing_from_main_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p1 = this.CreatePublisherProbe<int>();
                var p2 = this.CreatePublisherProbe<int>();
                var ex = new TestException("buh");
                var p = new TaskCompletionSource<Source<int, NotUsed>>();

                // task is intentionally not awaited
                var task = Source.Combine(
                        Source.From(new[] {Source.FromPublisher(p1), Source.FromPublisher(p2)}),
                        Source.FromTask(p.Task), i => new Merge<Source<int, NotUsed>>(i))
                    .MergeMany(5, x => x)
                    .RunWith(Sink.First<int>(), Materializer);

                await p1.ExpectRequestAsync();
                await p2.ExpectRequestAsync();
                p.SetException(ex);
                await p1.ExpectCancellationAsync();
                await p2.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_cancel_subStreams_when_failing_from_subStream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p1 = this.CreatePublisherProbe<int>();
                var p2 = this.CreatePublisherProbe<int>();
                var ex = new TestException("buh");
                var p = new TaskCompletionSource<int>();

                // task is intentionally not awaited
                var task = Source.From(new[]
                    {
                        Source.FromPublisher(p1), 
                        Source.FromPublisher(p2), 
                        Source.FromTask(p.Task)
                    })
                    .MergeMany(5, x => x)
                    .RunWith(Sink.First<int>(), Materializer);

                await p1.ExpectRequestAsync();
                await p2.ExpectRequestAsync();
                p.SetException(ex);
                await p1.ExpectCancellationAsync();
                await p2.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_cancel_subStreams_when_failing_map_function()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var settings = ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithInputBuffer(1, 1);
                var materializer = ActorMaterializer.Create(Sys, settings);
                var p = this.CreatePublisherProbe<int>();
                var ex = new TestException("buh");
                var latch = new TestLatch();

                // task is intentionally not awaited
                var task = Source.From(Enumerable.Range(1, 3)).MergeMany(10, i =>
                {
                    if (i == 1)
                        return Source.FromPublisher(p);

                    latch.Ready(TimeSpan.FromSeconds(3));
                    throw ex;
                }).RunWith(Sink.First<int>(), materializer);
                
                await p.ExpectRequestAsync();
                latch.CountDown();
                await p.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_cancel_subStreams_when_being_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p1 = this.CreatePublisherProbe<int>();
                var p2 = this.CreatePublisherProbe<int>();

                var sink = Source.From(new[] {Source.FromPublisher(p1), Source.FromPublisher(p2)})
                    .MergeMany(5, x => x)
                    .RunWith(this.SinkProbe<int>(), Materializer);

                sink.Request(1);
                await p1.ExpectRequestAsync();
                await p2.ExpectRequestAsync();
                sink.Cancel();
                await p1.ExpectCancellationAsync();
                await p2.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_work_with_many_concurrently_queued_events()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                const int noOfSources = 100;
                var p = Source.From(Enumerable.Range(0, noOfSources).Select(i => Src10(10*i)))
                    .MergeMany(int.MaxValue, x => x)
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await p.EnsureSubscriptionAsync();
                await p.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

                var elems = new List<int>();
                foreach (var _ in Enumerable.Range(0, noOfSources * 10))
                {
                    elems.Add(await p.RequestNextAsync());
                }
                
                await p.ExpectCompleteAsync();
                elems.Should().BeEquivalentTo(Enumerable.Range(0, noOfSources * 10));
            }, Materializer);
        }

        private sealed class AttributesSourceStage : GraphStage<SourceShape<Attributes>>
        {
            #region Logic

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly AttributesSourceStage _stage;
                private readonly Attributes _inheritedAttributes;

                public Logic(AttributesSourceStage stage, Attributes inheritedAttributes) : base(stage.Shape)
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


            public AttributesSourceStage()
            {
                Shape = new SourceShape<Attributes>(Out);
            }

            private Outlet<Attributes> Out { get; } = new Outlet<Attributes>("AttributesSource.out");

            public override SourceShape<Attributes> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
        }

        [Fact]
        public async Task A_FlattenMerge_must_propagate_attributes_to_inner_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var attributesSource = Source.FromGraph(new AttributesSourceStage());
                
                var task = Source.Single(attributesSource.AddAttributes(Attributes.CreateName("inner")))
                    .MergeMany(1, x => x)
                    .AddAttributes(Attributes.CreateName("outer"))
                    .RunWith(Sink.First<Attributes>(), Materializer);

                var attributes = (await task.ShouldCompleteWithin(3.Seconds())).AttributeList.ToList();
                var innerName = new Attributes.Name("inner");
                var outerName = new Attributes.Name("outer");

                attributes.Should().Contain(innerName);
                attributes.Should().Contain(outerName);
                attributes.IndexOf(outerName).Should().BeLessThan(attributes.IndexOf(innerName));
            }, Materializer);
        }

        [Fact]
        public async Task A_FlattenMerge_must_bubble_up_subStream_materialization_exception()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var matFail = new TestException("fail!");

                var task = Source.Single("whatever")
                    .MergeMany(4, x => Source.FromGraph(new FailingInnerMat(matFail)))
                    .RunWith(Sink.Ignore<string>(), Materializer);

                await task.ShouldThrowWithin(matFail, 1.Seconds());
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
