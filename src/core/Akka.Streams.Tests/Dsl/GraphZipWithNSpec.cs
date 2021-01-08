//-----------------------------------------------------------------------
// <copyright file="GraphZipWithNSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphZipWithNSpec : TwoStreamsSetup<int>
    {
        public GraphZipWithNSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new ZipWithNFixture(builder);

        private sealed class ZipWithNFixture : Fixture
        {
            public ZipWithNFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var zip = builder.Add(new ZipWithN<int, int>(ints => ints.Sum(), 2));
                Left = zip.In(0);
                Right = zip.In(1);
                Out = zip.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }

        [Fact]
        public void ZipWithN_must_work_in_the_happy_case()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new ZipWithN<int, int>(ints => ints.Sum(), 2));

                b.From(Source.From(Enumerable.Range(1, 4))).To(zip.In(0));
                b.From(Source.From(new[] {10, 20, 30, 40})).To(zip.In(1));

                b.From(zip.Out).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(2);
            probe.ExpectNext(11);
            probe.ExpectNext(22);

            subscription.Request(1);
            probe.ExpectNext(33);
            subscription.Request(1);
            probe.ExpectNext(44);

            probe.ExpectComplete();
        }

        [Fact]
        public void ZipWithN_must_work_in_the_sad_case()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new ZipWithN<int, int>(ints => ints.Aggregate(1, (i, i1) => i / i1), 2));

                b.From(Source.From(Enumerable.Range(1, 4))).To(zip.In(0));
                b.From(Source.From(Enumerable.Range(-2, 5))).To(zip.In(1));

                b.From(zip.Out).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(2);
            probe.ExpectNext(1 / 1 / -2);
            probe.ExpectNext(1 / 2 / -1);

            EventFilter.Exception<DivideByZeroException>().ExpectOne(() => subscription.Request(2));

            probe.ExpectError().Should().BeOfType<DivideByZeroException>();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void ZipWithN_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();
        
            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipWithN_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipWithN_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
            subscriber2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void ZipWithN_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
            subscriber2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void ZipWithN_must_work_with_3_inputs()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new ZipWithN<int, int>(ints => ints.Sum(), 3));

                b.From(Source.Single(1)).To(zip.In(0));
                b.From(Source.Single(2)).To(zip.In(1));
                b.From(Source.Single(3)).To(zip.In(2));

                b.From(zip.Out).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(5);
            probe.ExpectNext(6);

            probe.ExpectComplete();
        }

        [Fact]
        public void ZipWithN_must_work_with_30_inputs()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new ZipWithN<int, int>(ints => ints.Sum(), 30));

                foreach (var i in Enumerable.Range(0, 30))
                    b.From(Source.Single(i)).To(zip.In(i));

                b.From(zip.Out).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(1);
            probe.ExpectNext(Enumerable.Range(0, 30).Sum());

            probe.ExpectComplete();
        }
    }
}
