//-----------------------------------------------------------------------
// <copyright file="GraphZipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task Zip_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = this.CreateManualSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.From(Enumerable.Range(1, 4));
                    var source2 = Source.From(new[] { "A", "B", "C", "D", "E", "F" });

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = await probe.ExpectSubscriptionAsync();

                subscription.Request(2);
                await probe.ExpectNextAsync((1, "A"));
                await probe.ExpectNextAsync((2, "B"));
                subscription.Request(1);
                await probe.ExpectNextAsync((3, "C"));
                subscription.Request(1);
                await probe.ExpectNextAsync((4, "D"));
                await probe.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_complete_if_one_side_is_available_but_other_already_completed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<string>();

                var completed = RunnableGraph.FromGraph(GraphDsl.Create(Sink.Ignore<(int, string)>(), (b, sink) =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var source1 = Source.FromPublisher(upstream1).MapMaterializedValue(_ => Task.FromResult(Done.Instance));
                    var source2 = Source.FromPublisher(upstream2).MapMaterializedValue(_ => Task.FromResult(Done.Instance));

                    b.From(source1).To(zip.In0);
                    b.From(source2).To(zip.In1);
                    b.From(zip.Out).To(sink);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                await upstream1.SendNextAsync(1);
                await upstream1.SendNextAsync(2);
                await upstream2.SendNextAsync("A");
                await upstream2.SendCompleteAsync();

                completed.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                await upstream1.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_complete_even_if_no_pending_demand()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                await upstream1.SendNextAsync(1);
                await upstream2.SendNextAsync("A");
                await downstream.ExpectNextAsync((1, "A"));

                await upstream2.SendCompleteAsync();
                await downstream.ExpectCompleteAsync();
                await upstream1.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_complete_if_both_sides_complete_before_requested_with_elements_pending_2()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                await upstream1.SendNextAsync(1);
                await upstream2.SendNextAsync("A");

                await upstream1.SendCompleteAsync();
                await upstream2.SendCompleteAsync();

                await downstream.RequestNextAsync((1, "A"));
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_complete_if_one_side_complete_before_requested_with_elements_pending()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                await upstream1.SendNextAsync(1);
                await upstream1.SendNextAsync(2);
                await upstream2.SendNextAsync("A");

                await upstream1.SendCompleteAsync();
                await upstream2.SendCompleteAsync();

                await downstream.RequestNextAsync((1, "A"));
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_complete_if_one_side_complete_before_requested_with_elements_pending_2()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                await downstream.EnsureSubscriptionAsync();

                await upstream1.SendNextAsync(1);
                await upstream1.SendCompleteAsync();
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                await upstream2.SendNextAsync("A");
                await upstream2.SendCompleteAsync();

                await downstream.RequestNextAsync((1, "A"));
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                await subscriber1.ExpectSubscriptionAndCompleteAsync();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                await subscriber2.ExpectSubscriptionAndCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                await subscriber1.ExpectSubscriptionAndCompleteAsync();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                await subscriber2.ExpectSubscriptionAndCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
                subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task Zip_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
                subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
