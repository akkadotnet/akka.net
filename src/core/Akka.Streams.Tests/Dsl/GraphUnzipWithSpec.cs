//-----------------------------------------------------------------------
// <copyright file="GraphUnzipWithSpec.cs" company="Akka.NET Project">
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
    public class GraphUnzipWithSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphUnzipWithSpec(ITestOutputHelper helper)  : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void UnzipWith_must_work_with_immediately_completed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscribers = Setup(TestPublisher.Empty<int>());
                ValidateSubscriptionAndComplete(subscribers);
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_work_with_delayed_completed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscribers = Setup(TestPublisher.LazyEmpty<int>());
                ValidateSubscriptionAndComplete(subscribers);
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_work_with_two_immediately_failed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscribers = Setup(TestPublisher.Error<int>(TestException));
                ValidateSubscriptionAndError(subscribers);
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_work_with_two_delayed_failed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscribers = Setup(TestPublisher.LazyError<int>(TestException));
                ValidateSubscriptionAndError(subscribers);
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var leftProbe = this.CreateManualSubscriberProbe<int>();
                var rightProbe = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnzipWith<int, int, string>(Zipper));
                    var source = Source.From(Enumerable.Range(1, 4));

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0)
                        .Via(Flow.Create<int>().Buffer(4, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(leftProbe));
                    b.From(unzip.Out1)
                        .Via(Flow.Create<string>().Buffer(4, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(rightProbe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var leftSubscription = leftProbe.ExpectSubscription();
                var rightSubscription = rightProbe.ExpectSubscription();

                leftSubscription.Request(2);
                rightSubscription.Request(1);

                leftProbe.ExpectNext(2, 4);
                leftProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                rightProbe.ExpectNext("1+1");
                rightProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                
                leftSubscription.Request(1);
                rightSubscription.Request(2);

                leftProbe.ExpectNext(6);
                leftProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                rightProbe.ExpectNext("2+2", "3+3");
                rightProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                leftSubscription.Request(1);
                rightSubscription.Request(1);

                leftProbe.ExpectNext(8);
                rightProbe.ExpectNext("4+4");

                leftProbe.ExpectComplete();
                rightProbe.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void UnzipWith_must_work_in_the_sad_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var leftProbe = this.CreateManualSubscriberProbe<int>();
                var rightProbe = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnzipWith<int, int, string>(i => (1/i, 1 + "/" + i)));
                    var source = Source.From(Enumerable.Range(-2, 5));

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(leftProbe));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(rightProbe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var leftSubscription = leftProbe.ExpectSubscription();
                var rightSubscription = rightProbe.ExpectSubscription();

                Action requestFromBoth = () =>
                {
                    leftSubscription.Request(1);
                    rightSubscription.Request(1);
                };

                requestFromBoth();
                leftProbe.ExpectNext(1/-2);
                rightProbe.ExpectNext("1/-2");

                requestFromBoth();
                leftProbe.ExpectNext(1 / -1);
                rightProbe.ExpectNext("1/-1");

                EventFilter.Exception<DivideByZeroException>().ExpectOne(requestFromBoth);

                leftProbe.ExpectError().Should().BeOfType<DivideByZeroException>();
                rightProbe.ExpectError().Should().BeOfType<DivideByZeroException>();

                leftProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                rightProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_unzipWith_expanded_Person_unapply_3_outputs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe0 = this.CreateManualSubscriberProbe<string>();
                var probe1 = this.CreateManualSubscriberProbe<string>();
                var probe2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnzipWith<Person, string, string, int>(p => (p.Name, p.Surname, p.Age)));
                    var source = Source.Single(new Person("Caplin", "Capybara", 55));

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(probe0));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(probe1));
                    b.From(unzip.Out2).To(Sink.FromSubscriber(probe2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var subscription0 = probe0.ExpectSubscription();
                var subscription1 = probe1.ExpectSubscription();
                var subscription2 = probe2.ExpectSubscription();

                subscription0.Request(1);
                subscription1.Request(1);
                subscription2.Request(1);

                probe0.ExpectNext("Caplin");
                probe1.ExpectNext("Capybara");
                probe2.ExpectNext(55);

                probe0.ExpectComplete();
                probe1.ExpectComplete();
                probe2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void UnzipWith_must_work_with_up_to_6_outputs()
        {
            // the jvm version uses 20 outputs but we have only 7 so changed this spec a little bit

            this.AssertAllStagesStopped(() =>
            {
                var probe0 = this.CreateManualSubscriberProbe<int>();
                var probe1 = this.CreateManualSubscriberProbe<string>();
                var probe2 = this.CreateManualSubscriberProbe<int>();
                var probe3 = this.CreateManualSubscriberProbe<string>();
                var probe4 = this.CreateManualSubscriberProbe<int>();
                var probe5 = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    // odd input ports will be Int, even input ports will be String
                    var unzip =
                        b.Add(
                            new UnzipWith<List<int>, int, string, int, string, int, string>(
                                ints =>
                                    (ints[0], ints[0].ToString(), ints[1], ints[1].ToString(), ints[2],
                                        ints[2].ToString())));

                    var source = Source.Single(Enumerable.Range(1,3).ToList());
                    
                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(probe0));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(probe1));
                    b.From(unzip.Out2).To(Sink.FromSubscriber(probe2));
                    b.From(unzip.Out3).To(Sink.FromSubscriber(probe3));
                    b.From(unzip.Out4).To(Sink.FromSubscriber(probe4));
                    b.From(unzip.Out5).To(Sink.FromSubscriber(probe5));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                probe0.ExpectSubscription().Request(1);
                probe1.ExpectSubscription().Request(1);
                probe2.ExpectSubscription().Request(1);
                probe3.ExpectSubscription().Request(1);
                probe4.ExpectSubscription().Request(1);
                probe5.ExpectSubscription().Request(1);

                probe0.ExpectNext(1);
                probe1.ExpectNext("1");
                probe2.ExpectNext(2);
                probe3.ExpectNext("2");
                probe4.ExpectNext(3);
                probe5.ExpectNext("3");

                probe0.ExpectComplete();
                probe1.ExpectComplete();
                probe2.ExpectComplete();
                probe3.ExpectComplete();
                probe4.ExpectComplete();
                probe5.ExpectComplete();
            }, Materializer);
        }

        #region Test classes and helper 
        
        private static readonly TestException TestException = new TestException("test");

        private static readonly Func<int, (int, string)> Zipper = i => (i + i, i + "+" + i);

        private sealed class UnzipWithFixture
        {
            public UnzipWithFixture(GraphDsl.Builder<NotUsed> builder)
            {
                var unzip = builder.Add(new UnzipWith<int, int, string>(i => (i + i, i + "+" + i)));
                In = unzip.In;
                Left = unzip.Out0;
                Right = unzip.Out1;
            }

            public Inlet<int> In { get; }
            public Outlet<int> Left { get; }
            public Outlet<string> Right { get; }
        }

        private (TestSubscriber.ManualProbe<int>, TestSubscriber.ManualProbe<string>) Setup(IPublisher<int> p)
        {
            var leftSubscriber = this.CreateManualSubscriberProbe<int>();
            var rightSubscriber = this.CreateManualSubscriberProbe<string>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var f = new UnzipWithFixture(b);
                b.From(Source.FromPublisher(p)).To(f.In);
                b.From(f.Left).To(Sink.FromSubscriber(leftSubscriber));
                b.From(f.Right).To(Sink.FromSubscriber(rightSubscriber));

                return ClosedShape.Instance;
            })).Run(Materializer);

            return (leftSubscriber, rightSubscriber);
        }

        private static void ValidateSubscriptionAndComplete((TestSubscriber.ManualProbe<int>, TestSubscriber.ManualProbe<string>) subscribers)
        {
            subscribers.Item1.ExpectSubscriptionAndComplete();
            subscribers.Item2.ExpectSubscriptionAndComplete();
        }

        private static void ValidateSubscriptionAndError((TestSubscriber.ManualProbe<int>, TestSubscriber.ManualProbe<string>) subscribers)
        {
            subscribers.Item1.ExpectSubscriptionAndError().Should().Be(TestException);
            subscribers.Item2.ExpectSubscriptionAndError().Should().Be(TestException);
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

        #endregion
    }
}
