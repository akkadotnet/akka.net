//-----------------------------------------------------------------------
// <copyright file="GraphZipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphZipSpec : TwoStreamsSetup<(int, int)>
    {
        public GraphZipSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new ZipFixture(builder);

        private sealed class ZipFixture : Fixture
        {
            public ZipFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var zip = builder.Add(new Zip<int, int>());
                Left = zip.In0;
                Right = zip.In1;
                Out = zip.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<(int, int)> Out { get; }
        }
        
        [Fact]
        public void Zip_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.From(Enumerable.Range(1, 4));
                    var source2 = Source.From(new[] {"A", "B", "C", "D", "E", "F"});

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                subscription.Request(2);
                probe.ExpectNext((1, "A"));
                probe.ExpectNext((2, "B"));
                subscription.Request(1);
                probe.ExpectNext((3, "C"));
                subscription.Request(1);
                probe.ExpectNext((4, "D"));
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_complete_if_one_side_is_available_but_other_already_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();

                var completed = RunnableGraph.FromGraph(GraphDsl.Create(Sink.Ignore<(int, string)>(), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1).MapMaterializedValue<Task>(_ => null);
                    var source2 = Source.FromPublisher(upstream2).MapMaterializedValue<Task>(_ => null);

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                upstream1.SendNext(1);
                upstream1.SendNext(2);
                upstream2.SendNext("A");
                upstream2.SendComplete();

                completed.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                upstream1.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_complete_even_if_no_pending_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();
                var downstream = this.CreateSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1);
                    var source2 = Source.FromPublisher(upstream2);

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                downstream.Request(1);

                upstream1.SendNext(1);
                upstream2.SendNext("A");
                downstream.ExpectNext((1, "A"));

                upstream2.SendComplete();
                downstream.ExpectComplete();
                upstream1.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_complete_if_both_sides_complete_before_requested_with_elements_pending_2()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();
                var downstream = this.CreateSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1);
                    var source2 = Source.FromPublisher(upstream2);

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                upstream1.SendNext(1);
                upstream2.SendNext("A");

                upstream1.SendComplete();
                upstream2.SendComplete();

                downstream.RequestNext((1, "A"));
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_complete_if_one_side_complete_before_requested_with_elements_pending()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();
                var downstream = this.CreateSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1);
                    var source2 = Source.FromPublisher(upstream2);

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                upstream1.SendNext(1);
                upstream1.SendNext(2);
                upstream2.SendNext("A");

                upstream1.SendComplete();
                upstream2.SendComplete();

                downstream.RequestNext((1, "A"));
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_complete_if_one_side_complete_before_requested_with_elements_pending_2()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();
                var downstream = this.CreateSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(Sink.FromSubscriber(downstream), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1);
                    var source2 = Source.FromPublisher(upstream2);

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                downstream.EnsureSubscription();

                upstream1.SendNext(1);
                upstream1.SendComplete();
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

                upstream2.SendNext("A");
                upstream2.SendComplete();

                downstream.RequestNext((1, "A"));
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                subscriber2.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                subscriber2.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void Zip_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
                subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Zip_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
                subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }
    }
}
