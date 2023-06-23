//-----------------------------------------------------------------------
// <copyright file="GraphUnzipWithSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task UnzipWith_must_work_with_immediately_completed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscribers = Setup(TestPublisher.Empty<int>());
                ValidateSubscriptionAndComplete(subscribers);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_work_with_delayed_completed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscribers = Setup(TestPublisher.LazyEmpty<int>());
                ValidateSubscriptionAndComplete(subscribers);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_work_with_two_immediately_failed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscribers = Setup(TestPublisher.Error<int>(TestException));
                ValidateSubscriptionAndError(subscribers);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_work_with_two_delayed_failed_publisher()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var subscribers = Setup(TestPublisher.LazyError<int>(TestException));
                ValidateSubscriptionAndError(subscribers);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                var leftSubscription = await leftProbe.ExpectSubscriptionAsync();
                var rightSubscription = await rightProbe.ExpectSubscriptionAsync();

                leftSubscription.Request(2);
                rightSubscription.Request(1);

                leftProbe.ExpectNext(2, 4);
                await leftProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                await rightProbe.ExpectNextAsync("1+1");
                await rightProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                leftSubscription.Request(1);
                rightSubscription.Request(2);

                leftProbe.ExpectNext(6);
                await leftProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                rightProbe.ExpectNext("2+2", "3+3");
                await rightProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                leftSubscription.Request(1);
                rightSubscription.Request(1);

                await leftProbe.ExpectNextAsync(8);
                await rightProbe.ExpectNextAsync("4+4");

                await leftProbe.ExpectCompleteAsync();
                await rightProbe.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task UnzipWith_must_work_in_the_sad_case()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var leftProbe = this.CreateManualSubscriberProbe<int>();
                var rightProbe = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnzipWith<int, int, string>(i => (1 / i, 1 + "/" + i)));
                    var source = Source.From(Enumerable.Range(-2, 5));

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(leftProbe));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(rightProbe));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var leftSubscription = await leftProbe.ExpectSubscriptionAsync();
                var rightSubscription = await rightProbe.ExpectSubscriptionAsync();

                Action requestFromBoth = () =>
                {
                    leftSubscription.Request(1);
                    rightSubscription.Request(1);
                };

                requestFromBoth();
                await leftProbe.ExpectNextAsync(1 / -2);
                await rightProbe.ExpectNextAsync("1/-2");

                requestFromBoth();
                await leftProbe.ExpectNextAsync(1 / -1);
                await rightProbe.ExpectNextAsync("1/-1");

                await EventFilter.Exception<DivideByZeroException>().ExpectOneAsync(requestFromBoth);

                leftProbe.ExpectError().Should().BeOfType<DivideByZeroException>();
                rightProbe.ExpectError().Should().BeOfType<DivideByZeroException>();

                await leftProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await rightProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_propagate_last_downstream_cancellation_cause_once_all_downstream_have_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = CreateTestProbe();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var source = Source
                        .Maybe<int>()
                        .WatchTermination(Keep.Right)
                        .MapMaterializedValue(t =>
                        {
                            // side effecting our way out of this
                            probe.Ref.Tell(t, Nobody.Instance);
                            return NotUsed.Instance;
                        });

                    var unzip = b.Add(new UnzipWith<int, int, string>(i => (1 / i, $"1 / {i}")));

                    b.From(source).To(unzip.In);

                    Flow<T, T, NotUsed> KillSwitchFlow<T>()
                        => Flow.Create<T, NotUsed>()
                            .ViaMaterialized(KillSwitches.Single<T>(), Keep.Right)
                            .MapMaterializedValue(killSwitch =>
                            {
                                probe.Ref.Tell(killSwitch);
                                return NotUsed.Instance;
                            });

                    b.From(unzip.Out0).Via(KillSwitchFlow<int>()).To(Sink.Ignore<int>());
                    b.From(unzip.Out1).Via(KillSwitchFlow<string>()).To(Sink.Ignore<string>());

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var termination = probe.ExpectMsg<Task<Done>>();
                var killSwitch1 = probe.ExpectMsg<UniqueKillSwitch>();
                var killSwitch2 = probe.ExpectMsg<UniqueKillSwitch>();
                var boom = new TestException("Boom");
                killSwitch1.Abort(boom);
                killSwitch2.Abort(boom);
                termination.ContinueWith(t =>
                {
                    t.Exception.Should().NotBeNull();
                    t.Exception.InnerException.Should().Be(boom);
                });
                return Task.CompletedTask;
            }, Materializer);
        }
        
        [Fact]
        public async Task UnzipWith_must_unzipWith_expanded_Person_unapply_3_outputs()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
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

                var subscription0 = await probe0.ExpectSubscriptionAsync();
                var subscription1 = await probe1.ExpectSubscriptionAsync();
                var subscription2 = await probe2.ExpectSubscriptionAsync();

                subscription0.Request(1);
                subscription1.Request(1);
                subscription2.Request(1);

                await probe0.ExpectNextAsync("Caplin");
                await probe1.ExpectNextAsync("Capybara");
                await probe2.ExpectNextAsync(55);

                await probe0.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();
                await probe2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task UnzipWith_must_work_with_up_to_6_outputs()
        {
            // the jvm version uses 20 outputs but we have only 7 so changed this spec a little bit

            await this.AssertAllStagesStoppedAsync(async() => {
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

                    var source = Source.Single(Enumerable.Range(1, 3).ToList());

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(probe0));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(probe1));
                    b.From(unzip.Out2).To(Sink.FromSubscriber(probe2));
                    b.From(unzip.Out3).To(Sink.FromSubscriber(probe3));
                    b.From(unzip.Out4).To(Sink.FromSubscriber(probe4));
                    b.From(unzip.Out5).To(Sink.FromSubscriber(probe5));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                (await probe0.ExpectSubscriptionAsync()).Request(1);
                (await probe1.ExpectSubscriptionAsync()).Request(1);
                (await probe2.ExpectSubscriptionAsync()).Request(1);
                (await probe3.ExpectSubscriptionAsync()).Request(1);
                (await probe4.ExpectSubscriptionAsync()).Request(1);
                (await probe5.ExpectSubscriptionAsync()).Request(1);

                await probe0.ExpectNextAsync(1);
                await probe1.ExpectNextAsync("1");
                await probe2.ExpectNextAsync(2);
                await probe3.ExpectNextAsync("2");
                await probe4.ExpectNextAsync(3);
                await probe5.ExpectNextAsync("3");
                
                await probe0.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();
                await probe2.ExpectCompleteAsync();
                await probe3.ExpectCompleteAsync();
                await probe4.ExpectCompleteAsync();
                await probe5.ExpectCompleteAsync();
            }, Materializer);
        }

        #region Test classes and helper 
        
        private static readonly TestException TestException = new("test");

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
