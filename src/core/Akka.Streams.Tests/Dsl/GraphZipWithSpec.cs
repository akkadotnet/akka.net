//-----------------------------------------------------------------------
// <copyright file="GraphZipWithSpec.cs" company="Akka.NET Project">
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
// ReSharper disable InvokeAsExtensionMethod
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable MemberCanBePrivate.Local

namespace Akka.Streams.Tests.Dsl
{
    public class GraphZipWithSpec : TwoStreamsSetup<int>
    {
        public GraphZipWithSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder) => new ZipWithFixture(builder);

        private sealed class ZipWithFixture : Fixture
        {
            public ZipWithFixture(GraphDsl.Builder<NotUsed> builder) : base(builder)
            {
                var zipWith = builder.Add(new ZipWith<int, int, int>((i, i1) => i + i1));

                Left = zipWith.In0;
                Right = zipWith.In1;
                Out = zipWith.Out;
            }

            public override Inlet<int> Left { get; }

            public override Inlet<int> Right { get; }

            public override Outlet<int> Out { get; }
        }



        [Fact]
        public void ZipWith_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipWith_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void ZipWith_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
            subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
        }

        [Fact]
        public void ZipWith_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
            subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
        }

        [Fact]
        public void ZipWith_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zipWith = b.Add(new ZipWith<int, int, int>((i, i1) => i+i1));
                    var source1 = Source.From(Enumerable.Range(1, 4));
                    var source2 = Source.From(new[] {10, 20, 30, 40});

                    b.From(source1).To(zipWith.In0);
                    b.From(source2).To(zipWith.In1);
                    b.From(zipWith.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                subscription.Request(2);
                probe.ExpectNext(11, 22);

                subscription.Request(1);
                probe.ExpectNext(33);

                subscription.Request(1);
                probe.ExpectNext(44);

                probe.ExpectComplete();
            }, Materializer);
        }


        [Fact]
        public void ZipWith_must_work_in_the_sad_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zipWith = b.Add(new ZipWith<int, int, int>((i, i1) => i / i1));
                    var source1 = Source.From(Enumerable.Range(1, 4));
                    var source2 = Source.From(Enumerable.Range(-2, 5));

                    b.From(source1).To(zipWith.In0);
                    b.From(source2).To(zipWith.In1);
                    b.From(zipWith.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                subscription.Request(2);
                probe.ExpectNext(1/-2, 2/-1);
                EventFilter.Exception<DivideByZeroException>().ExpectOne(() => subscription.Request(2));
                probe.ExpectError().Should().BeOfType<DivideByZeroException>();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }

        [Fact]
        public void ZipWith_must_ZipWith_expanded_Person_unapply_3_outputs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<Person>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zipWith =
                        b.Add(
                            new ZipWith<string, string, int, Person>(
                                (name, surname, age) => new Person(name, surname, age)));

                    b.From(Source.Single("Caplin")).To(zipWith.In0);
                    b.From(Source.Single("Capybara")).To(zipWith.In1);
                    b.From(Source.Single(55)).To(zipWith.In2);
                    b.From(zipWith.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                subscription.Request(5);
                probe.ExpectNext().ShouldBeEquivalentTo(new Person("Caplin", "Capybara", 55));

                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void ZipWith_must_work_with_up_to_9_inputs()
        {
            // the jvm version uses 19 inputs but we have only 9

            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    Func<int, string, int, string, int, string, int, string, int, string> sum9 =
                        (i1, s1, i2, s2, i3, s3, i4, s4, i5) => i1 + s1 + i2 + s2 + i3 + s3 + i4 + s4 + i5;

                    // odd input ports will be Int, even input ports will be String
                    var zipWith = b.Add(ZipWith.Apply(sum9));

                    b.From(Source.Single(1)).To(zipWith.In0);
                    b.From(Source.Single("2")).To(zipWith.In1);
                    b.From(Source.Single(3)).To(zipWith.In2);
                    b.From(Source.Single("4")).To(zipWith.In3);
                    b.From(Source.Single(5)).To(zipWith.In4);
                    b.From(Source.Single("6")).To(zipWith.In5);
                    b.From(Source.Single(7)).To(zipWith.In6);
                    b.From(Source.Single("8")).To(zipWith.In7);
                    b.From(Source.Single(9)).To(zipWith.In8);
                    b.From(zipWith.Out).To(Sink.FromSubscriber(probe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription = probe.ExpectSubscription();

                subscription.Request(1);
                probe.ExpectNext(Enumerable.Range(1, 9).Aggregate("", (s, i) => s + i));
                probe.ExpectComplete();
            }, Materializer);
        }

        private sealed class Person
        {
            public Person(string name, string surname, int age)
            {
                Name = name;
                Surname = surname;
                Age = age;
            }

            public string Name { get; }

            public string Surname { get; }

            public int Age { get; }
        }
    }
}
