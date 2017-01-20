//-----------------------------------------------------------------------
// <copyright file="FlowFlattenMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
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
        public void A_FlattenMerge_must_propage_late_failure_from_main_stream()
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
    }
}
