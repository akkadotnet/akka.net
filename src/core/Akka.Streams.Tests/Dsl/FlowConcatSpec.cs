//-----------------------------------------------------------------------
// <copyright file="FlowConcatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InvokeAsExtensionMethod
namespace Akka.Streams.Tests.Dsl
{
    public class FlowConcatSpec : BaseTwoStreamsSetup<int>
    {
        public FlowConcatSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        protected override TestSubscriber.Probe<int> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            Source.FromPublisher(p1)
                .Concat(Source.FromPublisher(p2))
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);
            return subscriber;
        }

        [Fact]
        public void A_Concat_for_Flow_must_be_able_to_concat_Flow_with_Source()
        {
            var f1 = Flow.Create<int>().Select(x => x + "-s");
            var s1 = Source.From(new[] {1, 2, 3});
            var s2 = Source.From(new[] { 4,5,6 }).Select(x=> x + "-s");

            var subs = this.CreateManualSubscriberProbe<string>();
            var subSink = Sink.AsPublisher<string>(false);

            var res = f1.Concat(s2).RunWith(s1, subSink, Materializer).Item2;

            res.Subscribe(subs);
            var sub = subs.ExpectSubscription();
            sub.Request(9);
            Enumerable.Range(1, 6).ForEach(e=>subs.ExpectNext(e + "-s"));
            subs.ExpectComplete();
        }

        [Fact]
        public void A_Concat_for_Flow_must_be_able_to_prepend_a_Source_to_a_Flow()
        {
            var s1 = Source.From(new[] { 1, 2, 3 }).Select(x => x + "-s");
            var s2 = Source.From(new[] { 4, 5, 6 });
            var f2 = Flow.Create<int>().Select(x => x + "-s");

            var subs = this.CreateManualSubscriberProbe<string>();
            var subSink = Sink.AsPublisher<string>(false);

            var res = f2.Prepend(s1).RunWith(s2, subSink, Materializer).Item2;

            res.Subscribe(subs);
            var sub = subs.ExpectSubscription();
            sub.Request(9);
            Enumerable.Range(1, 6).ForEach(e => subs.ExpectNext(e + "-s"));
            subs.ExpectComplete();
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();
                subscription1.Request(5);
                Enumerable.Range(1, 4).ForEach(x => subscriber1.ExpectNext(x));
                subscriber1.ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();
                subscription2.Request(5);
                Enumerable.Range(1, 4).ForEach(x => subscriber2.ExpectNext(x));
                subscriber2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_one_nonempty_and_one_immediately_failed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
                subscriber.ExpectSubscription().Request(5);

                var errorSignalled = Enumerable.Range(1, 4)
                    .Aggregate(false, (b, e) => b || subscriber.ExpectNextOrError() is TestException);
                if (!errorSignalled)
                    subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_one_nonempty_and_one_delayed_failed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
                subscriber.ExpectSubscription().Request(5);

                var errorSignalled = Enumerable.Range(1, 4)
                    .Aggregate(false, (b, e) => b || subscriber.ExpectNextOrError() is TestException);
                if (!errorSignalled)
                    subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_correctly_handle_async_errors_in_secondary_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var promise = new TaskCompletionSource<int>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 3))
                    .Concat(Source.FromTask(promise.Task))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = subscriber.ExpectSubscription();
                subscription.Request(4);
                Enumerable.Range(1, 3).ForEach(x => subscriber.ExpectNext(x));
                promise.SetException(TestException());
                subscriber.ExpectError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_Source_DSL()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testSource =
                    Source.From(Enumerable.Range(1, 5))
                        .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)
                        .Grouped(1000);
                var task = testSource.RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1,10));

                var runnable = testSource.ToMaterialized(Sink.Ignore<IEnumerable<int>>(), Keep.Left);
                var t = runnable.Run(Materializer);
                t.Item1.Should().BeOfType<NotUsed>();
                t.Item2.Should().BeOfType<NotUsed>();

                runnable.MapMaterializedValue(_ => "boo").Run(Materializer).Should().Be("boo");
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_work_with_Flow_DSL()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testFlow = Flow.Create<int>()
                    .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)
                    .Grouped(1000);
                var task = Source.From(Enumerable.Range(1, 5))
                    .ViaMaterialized(testFlow, Keep.Both)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));

                var runnable =
                    Source.From(Enumerable.Range(1, 5))
                        .ViaMaterialized(testFlow, Keep.Both)
                        .To(Sink.Ignore<IEnumerable<int>>());
                runnable.Invoking(r => r.Run(Materializer)).ShouldNotThrow();

                runnable.MapMaterializedValue(_ => "boo").Run(Materializer).Should().Be("boo");
            }, Materializer);
        }

        [Fact(Skip = "ConcatMaterialized type conflict")]
        public void A_Concat_for_Flow_must_work_with_Flow_DSL2()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testFlow = Flow.Create<int>()
                    .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)
                    .Grouped(1000);
                var task = Source.From(Enumerable.Range(1, 5))
                    .ViaMaterialized(testFlow, Keep.Both)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));

                //var sink = testFlow.ConcatMaterialized(Source.From(Enumerable.Range(1, 5)), Keep.Both)
                //    .To(Sink.Ignore<IEnumerable<int>>())
                //    .MapMaterializedValue(
                //        x =>
                //        {
                //            x.Item1.Item1.Should().BeOfType<NotUsed>();
                //            x.Item1.Item2.Should().BeOfType<NotUsed>();
                //            x.Item2.Should().BeOfType<NotUsed>();
                //            return "boo";
                //        });

                //Source.From(Enumerable.Range(10, 6)).RunWith(sink, Materializer).Should().Be("boo");
            }, Materializer);
        }

        [Fact]
        public void A_Concat_for_Flow_must_subscribe_at_one_to_initial_source_and_to_one_that_it_is_concat_to()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher1 = this.CreatePublisherProbe<int>();
                var publisher2 = this.CreatePublisherProbe<int>();
                var probeSink =
                    Source.FromPublisher(publisher1)
                        .Concat(Source.FromPublisher(publisher2))
                        .RunWith(this.SinkProbe<int>(), Materializer);

                var sub1 = publisher1.ExpectSubscription();
                var sub2 = publisher2.ExpectSubscription();
                var subSink = probeSink.ExpectSubscription();

                sub1.SendNext(1);
                subSink.Request(1);
                probeSink.ExpectNext(1);
                sub1.SendComplete();

                sub2.SendNext(2);
                subSink.Request(1);
                probeSink.ExpectNext(2);
                sub2.SendComplete();

                probeSink.ExpectComplete();
            }, Materializer);
        }
    }
}
