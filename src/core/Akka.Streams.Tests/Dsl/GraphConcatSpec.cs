//-----------------------------------------------------------------------
// <copyright file="GraphConcatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
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
    public class GraphConcatSpec : TwoStreamsSetup<int>
    {
        public GraphConcatSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new ConcatFixture(builder);

        private class ConcatFixture : Fixture
        {
            public ConcatFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
               var concat =  builder.Add(new Concat<int, int>());
                Left = concat.In(0);
                Right = concat.In(1);
                Out = concat.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void Concat_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var concat1 = b.Add(new Concat<int, int>());
                    var concat2 = b.Add(new Concat<int, int>());
                    b.From(Source.From(new List<int>())).To(concat1.In(0));
                    b.From(Source.From(Enumerable.Range(1, 4))).To(concat1.In(1));

                    b.From(concat1.Out).To(concat2.In(0));
                    b.From(Source.From(Enumerable.Range(5, 6))).To(concat2.In(1));

                    b.From(concat2.Out).To(Sink.FromSubscriber(probe));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                for (var i = 1; i <= 10; i++)
                {
                    subscription.Request(1);
                    probe.ExpectNext(i);
                }

                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Concat_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();

                subscription1.Request(5);
                subscriber1.ExpectNext(1, 2, 3, 4).ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();

                subscription2.Request(5);
                subscriber2.ExpectNext(1, 2, 3, 4).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Concat_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();

                subscription1.Request(5);
                subscriber1.ExpectNext(1, 2, 3, 4).ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();

                subscription2.Request(5);
                subscriber2.ExpectNext(1, 2, 3, 4).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Concat_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
                subscriber2.ExpectSubscription().Request(5);
                
                foreach (var i in Enumerable.Range(1,4))
                {
                    var result = subscriber2.ExpectNextOrError();
                    if(result is int && (int)result == i)
                        continue;
                    if (result.Equals(TestException()))
                        return;
                }

                subscriber2.ExpectError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Concat_must_work_with_one_nonempty_publisher_and_one_delayed_failed_and()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
                subscriber.ExpectSubscription().Request(5);

                foreach (var i in Enumerable.Range(1, 4))
                {
                    var result = subscriber.ExpectNextOrError();
                    if (result is int && (int)result == i)
                        continue;
                    if (result.Equals(TestException()))
                        return;
                }

                subscriber.ExpectError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Concat_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Concat_must_correctly_handle_async_errors_in_secondary_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var promise = new TaskCompletionSource<int>();
                var subscriber = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var concat = b.Add(new Concat<int, int>());
                    var source = Source.From(Enumerable.Range(1, 3));

                    b.From(source).To(concat.In(0));
                    b.From(Source.FromTask(promise.Task)).To(concat.In(1));
                    b.From(concat.Out).To(Sink.FromSubscriber(subscriber));

                    return ClosedShape.Instance;
                })).Run(Materializer);


                var subscription = subscriber.ExpectSubscription();
                subscription.Request(4);
                subscriber.ExpectNext(1, 2, 3);
                promise.SetException(TestException());
                subscriber.ExpectError().Should().Be(TestException());
            }, Materializer);
        }
    }
}
