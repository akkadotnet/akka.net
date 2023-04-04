//-----------------------------------------------------------------------
// <copyright file="FlowConcatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task A_Concat_for_Flow_must_be_able_to_concat_Flow_with_Source()
        {
            var f1 = Flow.Create<int>().Select(x => x + "-s");
            var s1 = Source.From(new[] {1, 2, 3});
            var s2 = Source.From(new[] { 4,5,6 }).Select(x=> x + "-s");

            var subs = this.CreateManualSubscriberProbe<string>();
            var subSink = Sink.AsPublisher<string>(false);

            var res = f1.Concat(s2).RunWith(s1, subSink, Materializer).Item2;

            res.Subscribe(subs);
            var sub = await subs.ExpectSubscriptionAsync();
            sub.Request(9);

            foreach (var e in Enumerable.Range(1, 6))
                await subs.ExpectNextAsync(e + "-s");

            await subs.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_be_able_to_prepend_a_Source_to_a_Flow()
        {
            var s1 = Source.From(new[] { 1, 2, 3 }).Select(x => x + "-s");
            var s2 = Source.From(new[] { 4, 5, 6 });
            var f2 = Flow.Create<int>().Select(x => x + "-s");

            var subs = this.CreateManualSubscriberProbe<string>();
            var subSink = Sink.AsPublisher<string>(false);

            var res = f2.Prepend(s1).RunWith(s2, subSink, Materializer).Item2;

            res.Subscribe(subs);
            var sub = await subs.ExpectSubscriptionAsync();
            sub.Request(9);

            foreach (var e in Enumerable.Range(1, 6))
                await subs.ExpectNextAsync(e + "-s");

            await subs.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = await subscriber1.ExpectSubscriptionAsync();
                subscription1.Request(5);

                foreach (var x in Enumerable.Range(1, 4))
                    await subscriber1.ExpectNextAsync(x);

                await subscriber1.ExpectCompleteAsync();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = await subscriber2.ExpectSubscriptionAsync();
                subscription2.Request(5);

                foreach (var x in Enumerable.Range(1, 4))
                    await subscriber2.ExpectNextAsync(x);

                await subscriber2.ExpectCompleteAsync();

            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscriber = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_one_nonempty_and_one_immediately_failed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
                (await subscriber.ExpectSubscriptionAsync()).Request(5);

                var errorSignalled = Enumerable.Range(1, 4)
                    .Aggregate(false, (b, e) => b || subscriber.ExpectNextOrError() is TestException);
                if (!errorSignalled)
                    subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscriber = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_one_nonempty_and_one_delayed_failed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
                (await subscriber.ExpectSubscriptionAsync()).Request(5);

                var errorSignalled = Enumerable.Range(1, 4)
                    .Aggregate(false, (b, e) => b || subscriber.ExpectNextOrError() is TestException);
                if (!errorSignalled)
                    subscriber.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_correctly_handle_async_errors_in_secondary_upstream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var promise = new TaskCompletionSource<int>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 3))
                    .Concat(Source.FromTask(promise.Task))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var subscription = await subscriber.ExpectSubscriptionAsync();
                subscription.Request(4);

                foreach (var x in Enumerable.Range(1, 3))
                    await subscriber.ExpectNextAsync(x);

                promise.SetException(TestException());
                subscriber.ExpectError().Should().BeOfType<TestException>();

            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_Source_DSL()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var testSource =                                                                             
                Source.From(Enumerable.Range(1, 5))                                                                                 
                .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)                                                                                 
                .Grouped(1000);
                var task = testSource.RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));

                var runnable = testSource.ToMaterialized(Sink.Ignore<IEnumerable<int>>(), Keep.Left);
                var t = runnable.Run(Materializer);
                t.Item1.Should().BeOfType<NotUsed>();
                t.Item2.Should().BeOfType<NotUsed>();

                runnable.MapMaterializedValue(_ => "boo").Run(Materializer).Should().Be("boo");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_work_with_Flow_DSL()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var testFlow = Flow.Create<int>()                                                                             
                .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)                                                                             
                .Grouped(1000);
                var task = Source.From(Enumerable.Range(1, 5))
                    .ViaMaterialized(testFlow, Keep.Both)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));

                var runnable =
                    Source.From(Enumerable.Range(1, 5))
                        .ViaMaterialized(testFlow, Keep.Both)
                        .To(Sink.Ignore<IEnumerable<int>>());
                runnable.Invoking(r => r.Run(Materializer)).Should().NotThrow();

                runnable.MapMaterializedValue(_ => "boo").Run(Materializer).Should().Be("boo");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact(Skip = "ConcatMaterialized type conflict")]
        public async Task A_Concat_for_Flow_must_work_with_Flow_DSL2()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var testFlow = Flow.Create<int>()                                                                             
                .ConcatMaterialized(Source.From(Enumerable.Range(6, 5)), Keep.Both)                                                                             
                .Grouped(1000);
                var task = Source.From(Enumerable.Range(1, 5))
                    .ViaMaterialized(testFlow, Keep.Both)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Concat_for_Flow_must_subscribe_at_one_to_initial_source_and_to_one_that_it_is_concat_to()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var publisher1 = this.CreatePublisherProbe<int>();
                var publisher2 = this.CreatePublisherProbe<int>();
                var probeSink =
                    Source.FromPublisher(publisher1)
                        .Concat(Source.FromPublisher(publisher2))
                        .RunWith(this.SinkProbe<int>(), Materializer);

                var sub1 = await publisher1.ExpectSubscriptionAsync();
                var sub2 = await publisher2.ExpectSubscriptionAsync();
                var subSink = await probeSink.ExpectSubscriptionAsync();

                sub1.SendNext(1);
                subSink.Request(1);
                await probeSink.ExpectNextAsync(1);
                sub1.SendComplete();

                sub2.SendNext(2);
                subSink.Request(1);
                await probeSink.ExpectNextAsync(2);
                sub2.SendComplete();

                await probeSink.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
