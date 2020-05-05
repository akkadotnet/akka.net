//-----------------------------------------------------------------------
// <copyright file="GraphMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMergeSpec  : TwoStreamsSetup<int>
    {
        public GraphMergeSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new MergeFixture(builder);

        private sealed class MergeFixture : Fixture
        {
            public MergeFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var merge = builder.Add(new Merge<int, int>(2));
                Left = merge.In(0);
                Right = merge.In(1);
                Out = merge.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void A_Merge_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                // Different input sizes(4 and 6)
                var source1 = Source.From(Enumerable.Range(0, 4));
                var source2 = Source.From(Enumerable.Range(4, 6));
                var source3 = Source.From(new List<int>());
                var probe = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var m1 = b.Add(new Merge<int>(2));
                    var m2 = b.Add(new Merge<int>(2));
                    var sink = Sink.FromSubscriber(probe);

                    b.From(source1).To(m1.In(0));
                    b.From(m1.Out).Via(Flow.Create<int>().Select(x => x*2)).To(m2.In(0));
                    b.From(m2.Out).Via(Flow.Create<int>().Select(x => x / 2).Select(x=>x+1)).To(sink);
                    b.From(source2).To(m1.In(1));
                    b.From(source3).To(m2.In(1));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();
                var collected = new List<int>();
                for (var i = 1; i <= 10; i++)
                {
                    subscription.Request(1);
                    collected.Add(probe.ExpectNext());
                }

                collected.Where(i => i <= 4).ShouldOnlyContainInOrder(1, 2, 3, 4);
                collected.Where(i => i >= 5).ShouldOnlyContainInOrder(5, 6, 7, 8, 9, 10);

                collected.ShouldBeEquivalentTo(Enumerable.Range(1, 10).ToArray());
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Merge_must_work_with_one_way_merge()
        {
            var task = Source.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<int>(1));
                var source = b.Add(Source.From(Enumerable.Range(1, 3)));

                b.From(source).To(merge.In(0));

                return new SourceShape<int>(merge.Out);
            })).RunAggregate(new List<int>(), (list, i) =>
            {
                list.Add(i);
                return list;
            }, Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 3));
        }

        [Fact]
        public void A_Merge_must_work_with_n_way_merge()
        {
            var source1 = Source.Single(1);
            var source2 = Source.Single(2);
            var source3 = Source.Single(3);
            var source4 = Source.Single(4);
            var source5 = Source.Single(5);
            var source6 = Source.Empty<int>();

            var probe = this.CreateManualSubscriberProbe<int>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<int>(6));

                b.From(source1).To(merge.In(0));
                b.From(source2).To(merge.In(1));
                b.From(source3).To(merge.In(2));
                b.From(source4).To(merge.In(3));
                b.From(source5).To(merge.In(4));
                b.From(source6).To(merge.In(5));
                b.From(merge.Out).To(Sink.FromSubscriber(probe));

                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();

            var collected = new List<int>();
            for (var i = 1; i <= 5; i++)
            {
                subscription.Request(1);
                collected.Add(probe.ExpectNext());
            }

            collected.ShouldAllBeEquivalentTo(Enumerable.Range(1, 5));
            probe.ExpectComplete();
        }

        [Fact]
        public void A_Merge_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();
                subscription1.Request(4);
                subscriber1.ExpectNext(1, 2, 3, 4).ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();
                subscription2.Request(4);
                subscriber2.ExpectNext(1, 2, 3, 4).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Merge_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();
                subscription1.Request(4);
                subscriber1.ExpectNext(1, 2, 3, 4).ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();
                subscription2.Request(4);
                subscriber2.ExpectNext(1, 2, 3, 4).ExpectComplete();
            }, Materializer);
        }

        [Fact(Skip = "This is nondeterministic, multiple scenarios can happen")]
        public void A_Merge_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {

            }, Materializer);
        }

        [Fact(Skip = "This is nondeterministic, multiple scenarios can happen")]
        public void A_Merge_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {

            }, Materializer);
        }

        [Fact]
        public void A_Merge_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up1 = this.CreateManualPublisherProbe<int>();
                var up2 = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<int>();

                var src1 = Source.AsSubscriber<int>();
                var src2 = Source.AsSubscriber<int>();

                var t = RunnableGraph.FromGraph(GraphDsl.Create(src1, src2, ValueTuple.Create, (b, s1, s2) =>
                {
                    var merge = b.Add(new Merge<int>(2));
                    var sink = Sink.FromSubscriber(down)
                        .MapMaterializedValue<(ISubscriber<int>, ISubscriber<int>)?>(_ => null);

                    b.From(s1.Outlet).To(merge.In(0));
                    b.From(s2.Outlet).To(merge.In(1));
                    b.From(merge.Out).To(sink);
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up1.Subscribe(t.Item1);
                up2.Subscribe(t.Item2);
                var upSub1 = up1.ExpectSubscription();
                upSub1.ExpectCancellation();
                var upSub2 = up2.ExpectSubscription();
                upSub2.ExpectCancellation();
            }, Materializer);
        }
    }
}
