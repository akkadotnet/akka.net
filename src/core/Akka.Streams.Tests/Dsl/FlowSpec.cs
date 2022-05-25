//-----------------------------------------------------------------------
// <copyright file="FlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Internal;
using Akka.TestKit.TestEvent;
using FluentAssertions;
using FluentAssertions.Extensions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSpec : AkkaSpec
    {
        private interface IFruit { };
        private sealed class Apple : IFruit { };
        private static readonly List<Apple> Apples = Enumerable.Range(1, 1000).Select(_ => new Apple()).ToList(); 

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
                akka.actor.debug.receive=off
                akka.loglevel=INFO
                ");

        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowSpec(ITestOutputHelper helper) : base(Config.WithFallback(StreamTestDefaultMailbox.DefaultConfig), helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Theory]
        [InlineData("identity", 1)]
        [InlineData("identity", 2)]
        [InlineData("identity", 4)]
        [InlineData("identity2", 1)]
        [InlineData("identity2", 2)]
        [InlineData("identity2", 4)]
        public async Task A_flow_must_request_initial_elements_from_upstream(string name, int n)
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                ChainSetup<int, int, NotUsed> setup;

                if (name.Equals("identity"))
                    setup = await new ChainSetup<int, int, NotUsed>(Identity, Settings.WithInputBuffer(n, n),
                            (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                        .InitializeAsync();
                else
                    setup = await new ChainSetup<int, int, NotUsed>(Identity2, Settings.WithInputBuffer(n, n),
                            (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                        .InitializeAsync();

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, setup.Settings.MaxInputBufferSize);
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_request_more_elements_from_upstream_when_downstream_requests_more_elements()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings,
                        (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                    .InitializeAsync();
            
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, Settings.MaxInputBufferSize);
                setup.DownstreamSubscription.Request(1);
                await setup.Upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                setup.DownstreamSubscription.Request(2);
                await setup.Upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                setup.UpstreamSubscription.SendNext("a");
                await setup.Downstream.ExpectNextAsync("a");
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                await setup.Upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                setup.UpstreamSubscription.SendNext("b");
                setup.UpstreamSubscription.SendNext("c");
                setup.UpstreamSubscription.SendNext("d");
                await setup.Downstream.ExpectNextAsync("b");
                await setup.Downstream.ExpectNextAsync("c");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_deliver_events_when_publisher_sends_elements_and_then_completes()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings,
                        (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                    .InitializeAsync();
            
                setup.DownstreamSubscription.Request(1);
                setup.UpstreamSubscription.SendNext("test");
                setup.UpstreamSubscription.SendComplete();
                await setup.Downstream.ExpectNextAsync("test");
                await setup.Downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_deliver_complete_signal_when_publisher_immediately_completes()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings,
                        (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                    .InitializeAsync();
                
                setup.UpstreamSubscription.SendComplete();
                await setup.Downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_deliver_error_signal_when_publisher_immediately_fails()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings,
                        (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                    .InitializeAsync();
                
                var weirdError = new Exception("weird test exception");
                setup.UpstreamSubscription.SendError(weirdError);
                (await setup.Downstream.ExpectErrorAsync()).Should().Be(weirdError);
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_cancel_upstream_when_single_subscriber_cancels_subscription_while_receiving_data()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this)
                    .InitializeAsync();
                setup.DownstreamSubscription.Request(5);
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("test");
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("test2");
                await setup.Downstream.ExpectNextAsync("test");
                await setup.Downstream.ExpectNextAsync("test2");
                setup.DownstreamSubscription.Cancel();

                // because of the "must cancel its upstream Subscription if its last downstream Subscription has been canceled" rule
                await setup.UpstreamSubscription.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_materialize_into_Publisher_Subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<string>();
                var (flowIn, flowOut) = MaterializeIntoSubscriberAndPublisher(flow, Materializer);

                var c1 = this.CreateManualSubscriberProbe<string>();
                flowOut.Subscribe(c1);

                var source = Source.From(new[] {"1", "2", "3"}).RunWith(Sink.AsPublisher<string>(false), Materializer);
                source.Subscribe(flowIn);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(3);
                
                await c1.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_materialize_into_Publisher_Subscriber_and_transformation_processor()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<int>().Select(i=>i.ToString());
                var (flowIn, flowOut) = MaterializeIntoSubscriberAndPublisher(flow, Materializer);

                var c1 = this.CreateManualSubscriberProbe<string>();
                flowOut.Subscribe(c1);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(3);
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                var source = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
                source.Subscribe(flowIn);
            
                await c1.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_materialize_into_Publisher_Subscriber_and_multiple_transformation_processor()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<int>().Select(i => i.ToString()).Select(s => "elem-" + s);
                var (flowIn, flowOut) = MaterializeIntoSubscriberAndPublisher(flow, Materializer);

                var c1 = this.CreateManualSubscriberProbe<string>();
                flowOut.Subscribe(c1);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(3);
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                var source = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
                source.Subscribe(flowIn);

                await c1.AsyncBuilder()
                    .ExpectNext("elem-1", "elem-2", "elem-3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_subscribe_Subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<string>();
                var c1 = this.CreateManualSubscriberProbe<string>();
                var sink = flow.To(Sink.FromSubscriber(c1));
                var publisher = Source.From(new[] { "1", "2", "3" }).RunWith(Sink.AsPublisher<string>(false), Materializer);
                Source.FromPublisher(publisher).To(sink).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(3);
                
                await c1.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_perform_transformation_operation()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<int>().Select(i =>
                {
                    TestActor.Tell(i.ToString());
                    return i.ToString();
                });
                var publisher = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
                Source.FromPublisher(publisher).Via(flow).To(Sink.Ignore<string>()).Run(Materializer);

                await ExpectMsgAsync("1");
                await ExpectMsgAsync("2");
                await ExpectMsgAsync("3");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_perform_transformation_operation_and_subscribe_Subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Flow.Create<int>().Select(i => i.ToString());
                var c1 = this.CreateManualSubscriberProbe<string>();
                var sink = flow.To(Sink.FromSubscriber(c1));
                var publisher = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
                Source.FromPublisher(publisher).To(sink).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(3);
                
                await c1.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_be_materializable_several_times_with_fanout_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var flow = Source.From(new[] {1, 2, 3}).Select(i => i.ToString());
                var p1 = flow.RunWith(Sink.AsPublisher<string>(true), Materializer);
                var p2 = flow.RunWith(Sink.AsPublisher<string>(true), Materializer);
                var s1 = this.CreateManualSubscriberProbe<string>();
                var s2 = this.CreateManualSubscriberProbe<string>();
                var s3 = this.CreateManualSubscriberProbe<string>();
                p1.Subscribe(s1);
                p2.Subscribe(s2);
                p2.Subscribe(s3);

                var sub1 = await s1.ExpectSubscriptionAsync();
                var sub2 = await s2.ExpectSubscriptionAsync();
                var sub3 = await s3.ExpectSubscriptionAsync();

                
                sub1.Request(3);
                await s1.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();

                sub2.Request(3);
                sub3.Request(3);
                
                await s2.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
                await s3.AsyncBuilder()
                    .ExpectNext("1", "2", "3")
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_be_covariant()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                // ReSharper disable SuggestVarOrType_Elsewhere
                // ReSharper disable UnusedVariable
                Source<IFruit, NotUsed> f1 = Source.From<IFruit>(Apples);
                IPublisher<IFruit> p1 = Source.From<IFruit>(Apples).RunWith(Sink.AsPublisher<IFruit>(false), Materializer);
                SubFlow<IFruit, NotUsed, IRunnableGraph<NotUsed>> f2 =
                    Source.From<IFruit>(Apples).SplitWhen(_ => true);
                SubFlow<IFruit, NotUsed, IRunnableGraph<NotUsed>> f3 =
                    Source.From<IFruit>(Apples).GroupBy(2, _ => true);
                Source<(IImmutableList<IFruit>, Source<IFruit, NotUsed>), NotUsed> f4 =
                    Source.From<IFruit>(Apples).PrefixAndTail(1);
                SubFlow<IFruit, NotUsed, Sink<string, NotUsed>> d1 =
                    Flow.Create<string>()
                        .Select<string, string, IFruit, NotUsed>(_ => new Apple())
                        .SplitWhen(_ => true);
                SubFlow<IFruit, NotUsed, Sink<string, NotUsed>> d2 =
                    Flow.Create<string>()
                        .Select<string, string, IFruit, NotUsed>(_ => new Apple())
                        .GroupBy(-1,_ => 2);
                Flow<string, (IImmutableList<IFruit>, Source<IFruit, NotUsed>), NotUsed> d3 =
                    Flow.Create<string>().Select<string, string, IFruit, NotUsed>(_ => new Apple()).PrefixAndTail(1);
                // ReSharper restore SuggestVarOrType_Elsewhere
                // ReSharper restore UnusedVariable

                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_must_be_possible_to_convert_to_a_processor_and_should_be_able_to_take_a_Processor()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var identity1 = Flow.Create<int>().ToProcessor();
                var identity2 = Flow.FromProcessor(() => identity1.Run(Materializer));
                var task = Source.From(Enumerable.Range(1, 10))
                    .Via(identity2)
                    .Limit(100)
                    .RunWith(Sink.Seq<int>(), Materializer);

                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1,10));

                // Reusable:
                task = Source.From(Enumerable.Range(1, 10))
                    .Via(identity2)
                    .Limit(100)
                    .RunWith(Sink.Seq<int>(), Materializer);

                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_adapt_speed_to_the_currently_slowest_subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 1), this)
                    .InitializeAsync();
                
                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                setup.DownstreamSubscription.Request(5);
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1); // because initialInputBufferSize=1

                setup.UpstreamSubscription.SendNext("firstElement");
                await setup.Downstream.ExpectNextAsync("firstElement");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("element2");

                await setup.Downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
                downstream2Subscription.Request(1);
                await downstream2.ExpectNextAsync("firstElement");

                await setup.Downstream.ExpectNextAsync("element2");

                downstream2Subscription.Request(1);
                await downstream2.ExpectNextAsync("element2");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_support_slow_subscriber_with_fan_out_2()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 2), this)
                    .InitializeAsync();
                
                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                setup.DownstreamSubscription.Request(5);
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1); // because initialInputBufferSize=1

                setup.UpstreamSubscription.SendNext("element1");
                await setup.Downstream.ExpectNextAsync("element1");

                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("element2");
                await setup.Downstream.ExpectNextAsync("element2");
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("element3");
                // downstream2 has not requested anything, fan-out buffer 2
                await setup.Downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                downstream2Subscription.Request(2);
                await setup.Downstream.ExpectNextAsync("element3");

                await downstream2.AsyncBuilder()
                    .ExpectNext("element1", "element2")
                    .ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(100)))
                    .ExecuteAsync();

                setup.UpstreamSubscription.Request(1);
                setup.UpstreamSubscription.SendNext("element4");
                await setup.Downstream.ExpectNextAsync("element4");

                downstream2Subscription.Request(2);
                await downstream2.AsyncBuilder()
                    .ExpectNext("element3", "element4")
                    .ExecuteAsync();

                setup.UpstreamSubscription.SendComplete();
                await setup.Downstream.ExpectCompleteAsync();
                await downstream2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_support_incoming_subscriber_while_elements_were_requested_before()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 1), this)
                    .InitializeAsync();

                setup.DownstreamSubscription.Request(5);
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a1");
                await setup.Downstream.ExpectNextAsync("a1");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a2");
                await setup.Downstream.ExpectNextAsync("a2");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);

                // link now while an upstream element is already requested
                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                // situation here:
                // downstream 1 now has 3 outstanding
                // downstream 2 has 0 outstanding

                setup.UpstreamSubscription.SendNext("a3");
                await setup.Downstream.ExpectNextAsync("a3");
                await downstream2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100)); // as nothing was requested yet, fanOutBox needs to cache element in this case

                downstream2Subscription.Request(1);
                await downstream2.ExpectNextAsync("a3");

                // d1 now has 2 outstanding
                // d2 now has 0 outstanding
                // buffer should be empty so we should be requesting one new element

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1); // because of buffer size 1
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_be_unblocked_when_blocking_subscriber_cancels_subscription()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 1), this)
                    .InitializeAsync();
                
                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                setup.DownstreamSubscription.Request(5);
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("firstElement");
                await setup.Downstream.ExpectNextAsync("firstElement");

                downstream2Subscription.Request(1);
                await downstream2.ExpectNextAsync("firstElement");
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("element2");

                await setup.Downstream.ExpectNextAsync("element2");
                
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.UpstreamSubscription.SendNext("element3");
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                
                await setup.Downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await setup.Upstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await downstream2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                // should unblock fanout box
                downstream2Subscription.Cancel();
                await setup.Downstream.ExpectNextAsync("element3");
                setup.UpstreamSubscription.SendNext("element4");
                await setup.Downstream.ExpectNextAsync("element4");

                setup.UpstreamSubscription.SendComplete();
                await setup.Downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_after_OnSubscribe_if_initial_upstream_was_completed()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 1), this)
                    .InitializeAsync();
                var downstream2 = this.CreateManualSubscriberProbe<string>();
                // don't link it just yet

                setup.DownstreamSubscription.Request(5);
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a1");
                await setup.Downstream.ExpectNextAsync("a1");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a2");
                await setup.Downstream.ExpectNextAsync("a2");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);

                // link now while an upstream element is already requested
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                setup.UpstreamSubscription.SendNext("a3");
                setup.UpstreamSubscription.SendComplete();
                await setup.Downstream.ExpectNextAsync("a3");
                await setup.Downstream.ExpectCompleteAsync();
            
                await downstream2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100)); // as nothing was requested yet, fanOutBox needs to cache element in this case
            
                downstream2Subscription.Request(1);
                await downstream2.ExpectNextAsync("a3");
                await downstream2.ExpectCompleteAsync();

                var downstream3 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream3);
                await downstream3.ExpectSubscriptionAsync();
                (await downstream3.ExpectErrorAsync()).Should().BeOfType<NormalShutdownException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_should_be_called_instead_of_OnSubscribed_after_initial_upstream_reported_an_error()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<int, string, NotUsed>(
                        flow => flow.Select<int,int,string,NotUsed>(_ => throw new TestException("test")), 
                        Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 1), this)
                    .InitializeAsync();

                setup.DownstreamSubscription.Request(1);
                await setup.UpstreamSubscription.ExpectRequestAsync(1);

                setup.UpstreamSubscription.SendNext(5);
                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                await setup.UpstreamSubscription.ExpectCancellationAsync();
                (await setup.Downstream.ExpectErrorAsync()).Should().BeOfType<TestException>();

                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                (await downstream2.ExpectSubscriptionAndErrorAsync()).Should().BeOfType<TestException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_when_all_subscriptions_were_cancelled ()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                        (settings, factory) => ActorMaterializer.Create(factory, settings),
                        (source, materializer) => ToFanoutPublisher(source, materializer, 16), this)
                    .InitializeAsync();

                // make sure stream is initialized before canceling downstream
                await Task.Delay(100);

                await setup.UpstreamSubscription.ExpectRequestAsync(1);
                setup.DownstreamSubscription.Cancel();
                await setup.UpstreamSubscription.ExpectCancellationAsync();

                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                // IllegalStateException shut down
                (await downstream2.ExpectSubscriptionAndErrorAsync()).Should().BeAssignableTo<IllegalStateException>();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_multiple_subscribers_FanOutBox_should_be_created_from_a_function_easily()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(0, 10))
                    .Via(Flow.FromFunction<int, int>(i => i + 1))
                    .RunWith(Sink.Seq<int>(), Materializer);
                
                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public async Task A_broken_Flow_must_cancel_upstream_and_call_onError_on_current_and_future_downstream_subscribers_if_an_internal_error_occurs()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var setup = await new ChainSetup<string, string, NotUsed>(FaultyFlow<string,string,string>, Settings.WithInputBuffer(1, 1),
                    (settings, factory) => ActorMaterializer.Create(factory, settings),
                    (source, materializer) => ToFanoutPublisher(source, materializer, 16), this)
                    .InitializeAsync();

                async Task CheckError(TestSubscriber.ManualProbe<string> probe)
                {
                    var error = await probe.ExpectErrorAsync();
                    error.Should().BeOfType<AbruptTerminationException>();
                    error.Message.Should().StartWith("Processor actor");
                }

                var downstream2 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream2);
                var downstream2Subscription = await downstream2.ExpectSubscriptionAsync();

                setup.DownstreamSubscription.Request(5);
                downstream2Subscription.Request(5);
                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a1");
                await setup.Downstream.ExpectNextAsync("a1");
                await downstream2.ExpectNextAsync("a1");

                await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a2");
                await setup.Downstream.ExpectNextAsync("a2");
                await downstream2.ExpectNextAsync("a2");

                var filters = new EventFilterBase[]
                {
                    new ErrorFilter(typeof(NullReferenceException)),
                    new ErrorFilter(typeof(IllegalStateException)),
                    new ErrorFilter(typeof(PostRestartException)),// This is thrown because we attach the dummy failing actor to toplevel
                };

                try
                {
                    Sys.EventStream.Publish(new Mute(filters));

                    await setup.Upstream.ExpectRequestAsync(setup.UpstreamSubscription, 1);
                    setup.UpstreamSubscription.SendNext("a3");
                    await setup.UpstreamSubscription.ExpectCancellationAsync();

                    // IllegalStateException terminated abruptly
                    await CheckError(setup.Downstream);
                    await CheckError(downstream2);

                    var downstream3 = this.CreateManualSubscriberProbe<string>();
                    setup.Publisher.Subscribe(downstream3);
                    await downstream3.ExpectSubscriptionAsync();
                    // IllegalStateException terminated abruptly
                    await CheckError(downstream3);
                }
                finally
                {
                    Sys.EventStream.Publish(new Unmute(filters));
                }
            }, Materializer);
        }

        [Fact]
        public async Task A_broken_Flow_must_suitably_override_attribute_handling_methods()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                var f = Flow.Create<int>().Select(x => x + 1).Async().AddAttributes(Attributes.None).Named("name");
                f.Module.Attributes.GetAttribute<Attributes.Name>().Value.Should().Be("name");
                f.Module.Attributes.GetAttribute<Attributes.AsyncBoundary>()
                    .Should()
                    .Be(Attributes.AsyncBoundary.Instance);
                
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_broken_Flow_must_work_without_fusing()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var settings = ActorMaterializerSettings.Create(Sys).WithAutoFusing(false).WithInputBuffer(1, 1);
                var noFusingMaterializer = ActorMaterializer.Create(Sys, settings);

                // The map followed by a filter is to ensure that it is a CompositeModule passed in to Flow[Int].via(...)
                var sink = Flow.Create<int>().Via(Flow.Create<int>().Select(i => i + 1).Where(_ => true))
                    .ToMaterialized(Sink.First<int>(), Keep.Right);
                
                var task = Source.Single(4711).RunWith(sink, noFusingMaterializer);
                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.Should().Be(4712);
            }, Materializer);
        }

        private static Flow<TIn, TOut, TMat> Identity<TIn, TOut, TMat>(Flow<TIn, TOut, TMat> flow) => flow.Select(e => e);
        private static Flow<TIn, TOut, TMat> Identity2<TIn, TOut, TMat>(Flow<TIn, TOut, TMat> flow) => Identity(flow);

        private sealed class BrokenActorInterpreter : ActorGraphInterpreter
        {
            private readonly object _brokenMessage;

            public BrokenActorInterpreter(GraphInterpreterShell shell, object brokenMessage) : base(shell)
            {
                _brokenMessage = brokenMessage;
            }

            protected internal override bool AroundReceive(Receive receive, object message)
            {
                if (message is OnNext { Id: 0 } next && next.Event == _brokenMessage)
                    throw new NullReferenceException($"I'm so broken {next.Event}");

                return base.AroundReceive(receive, message);
            }
        }

        private Flow<TIn, TOut2, NotUsed> FaultyFlow<TIn, TOut, TOut2>(Flow<TIn, TOut, NotUsed> flow) where TOut : TOut2
        {
            Flow<TOut, TOut2, NotUsed> CreateGraph()
            {
                var stage = new Select<TOut, TOut2>(x => x);
                var assembly = new GraphAssembly(
                    stages: new IGraphStageWithMaterializedValue<Shape, object>[] { stage }, 
                    originalAttributes: new[] { Attributes.None }, 
                    inlets: new Inlet[] { stage.Shape.Inlet, null }, 
                    inletOwners: new[] { 0, -1 }, 
                    outlets: new Outlet[] { null, stage.Shape.Outlet }, 
                    outletOwners: new[] { -1, 0 });

                var (connections, logics) = assembly.Materialize(
                    inheritedAttributes: Attributes.None, 
                    copiedModules: assembly.Stages.Select(s => s.Module).ToArray(), 
                    materializedValues: new Dictionary<IModule, object>(), register: _ => { });

                var shell = new GraphInterpreterShell(assembly, connections, logics, stage.Shape, 
                    Settings, (ActorMaterializerImpl)Materializer);

                var props = Props.Create(() => new BrokenActorInterpreter(shell, "a3"))
                    .WithDeploy(Deploy.Local)
                    .WithDispatcher("akka.test.stream-dispatcher");
                var impl = Sys.ActorOf(props, "broken-stage-actor");

                var subscriber = new ActorGraphInterpreter.BoundarySubscriber<TOut>(impl, shell, 0);
                var publisher = new FaultyFlowPublisher<TOut2>(impl, shell);

                impl.Tell(new ActorGraphInterpreter.ExposedPublisher(shell, 0, publisher));

                return Flow.FromSinkAndSource(Sink.FromSubscriber(subscriber), Source.FromPublisher(publisher));
            }

            return flow.Via(CreateGraph());
        }

        private sealed class FaultyFlowPublisher<TOut> : ActorPublisher<TOut>
        {
            public FaultyFlowPublisher(IActorRef impl, GraphInterpreterShell shell) : base(impl)
            {
                WakeUpMessage = new ActorGraphInterpreter.SubscribePending(shell, 0);
            }

            protected override object WakeUpMessage { get; }
        }

        private static IPublisher<TOut> ToPublisher<TOut, TMat>(Source<TOut, TMat> source,
            ActorMaterializer materializer) => source.RunWith(Sink.AsPublisher<TOut>(false), materializer);

        private static IPublisher<TOut> ToFanoutPublisher<TOut, TMat>(Source<TOut, TMat> source,
            ActorMaterializer materializer, int elasticity)
            =>
                source.RunWith(
                    Sink.AsPublisher<TOut>(true).WithAttributes(Attributes.CreateInputBuffer(elasticity, elasticity)),
                    materializer);

        private static (ISubscriber<TIn>, IPublisher<TOut>) MaterializeIntoSubscriberAndPublisher<TIn, TOut, TMat>(
            Flow<TIn, TOut, TMat> flow, ActorMaterializer materializer)
            => flow.RunWith(Source.AsSubscriber<TIn>(), Sink.AsPublisher<TOut>(false), materializer);
    }
}
