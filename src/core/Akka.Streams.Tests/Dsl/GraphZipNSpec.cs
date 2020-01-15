//-----------------------------------------------------------------------
// <copyright file="GraphZipNSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphZipNSpec : TwoStreamsSetup<IImmutableList<int>>
    {
        public GraphZipNSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new ZipNFixture(builder);

        private sealed class ZipNFixture : Fixture
        {
            public ZipNFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var zip = builder.Add(new ZipN<int>(2));
                Left = zip.In(0);
                Right = zip.In(1);
                Out = zip.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<IImmutableList<int>> Out { get; }
        }

        [Fact]
        public void ZipN_must_work_in_the_happy_case()
        {
            var probe = this.CreateManualSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.From(Enumerable.Range(1, 4))).To(zip.In(0));
                b.From(Source.From(new[] {2, 3, 4, 5})).To(zip.In(1));

                b.From(zip.Out).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(2);
            probe.ExpectNext().ShouldAllBeEquivalentTo(new[] {1, 2});
            probe.ExpectNext().ShouldAllBeEquivalentTo(new[] {2, 3});

            subscription.Request(1);
            probe.ExpectNext().ShouldAllBeEquivalentTo(new[] {3, 4});

            subscription.Request(1);
            probe.ExpectNext().ShouldAllBeEquivalentTo(new[] {4, 5});

            probe.ExpectComplete();
        }

        [Fact]
        public void ZipN_must_complete_if_one_side_is_available_but_other_already_completed()
        {
            var upstream1 = this.CreatePublisherProbe<int>();
            var upstream2 = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.FromPublisher(upstream1)).To(zip);
                b.From(Source.FromPublisher(upstream2)).To(zip);
                b.From(zip).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);

            upstream1.SendNext(1);
            upstream1.SendNext(2);
            upstream2.SendNext(2);
            upstream2.SendComplete();

            downstream.Request(1);
            downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] {1, 2});
            downstream.ExpectComplete();
            upstream1.ExpectCancellation();
        }

        [Fact]
        public void ZipN_must_complete_even_if_no_pending_demand()
        {
            var upstream1 = this.CreatePublisherProbe<int>();
            var upstream2 = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.FromPublisher(upstream1)).To(zip);
                b.From(Source.FromPublisher(upstream2)).To(zip);
                b.From(zip).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);

            downstream.Request(1);

            upstream1.SendNext(1);
            upstream2.SendNext(2);
            downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] { 1, 2 });

            upstream2.SendComplete();
            downstream.ExpectComplete();
            upstream1.ExpectCancellation();
        }

        [Fact]
        public void ZipN_must_complete_if_both_sides_complete_before_requested_with_elements_pending()
        {
            var upstream1 = this.CreatePublisherProbe<int>();
            var upstream2 = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.FromPublisher(upstream1)).To(zip);
                b.From(Source.FromPublisher(upstream2)).To(zip);
                b.From(zip).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);

            upstream1.SendNext(1);
            upstream2.SendNext(2);
            upstream1.SendComplete();
            upstream2.SendComplete();

            downstream.Request(1);
            downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] { 1, 2 });
            downstream.ExpectComplete();
        }

        [Fact]
        public void ZipN_must_complete_if_one_side_complete_before_requested_with_elements_pending()
        {
            var upstream1 = this.CreatePublisherProbe<int>();
            var upstream2 = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.FromPublisher(upstream1)).To(zip);
                b.From(Source.FromPublisher(upstream2)).To(zip);
                b.From(zip).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);

            upstream1.SendNext(1);
            upstream1.SendNext(2);
            upstream2.SendNext(2);
            upstream1.SendComplete();
            upstream2.SendComplete();

            downstream.Request(1);
            downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] { 1, 2 });
            downstream.ExpectComplete();
        }

        [Fact]
        public void ZipN_must_complete_if_one_side_complete_before_requested_with_elements_pending_2()
        {
            var upstream1 = this.CreatePublisherProbe<int>();
            var upstream2 = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<IImmutableList<int>>();

            RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
            {
                var zip = b.Add(new ZipN<int>(2));

                b.From(Source.FromPublisher(upstream1)).To(zip);
                b.From(Source.FromPublisher(upstream2)).To(zip);
                b.From(zip).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);

            downstream.EnsureSubscription();

            upstream1.SendNext(1);
            upstream1.SendComplete();
            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            upstream2.SendNext(2);
            upstream2.SendComplete();
            downstream.Request(1);
            downstream.ExpectNext().ShouldAllBeEquivalentTo(new[] { 1, 2 });
            downstream.ExpectComplete();
        }

        [Fact]
        public void ZipN_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();
        
            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipN_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipN_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
            subscriber2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void ZipN_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
            subscriber2.ExpectSubscriptionAndError();
        }
    }
}
