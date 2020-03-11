//-----------------------------------------------------------------------
// <copyright file="FlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.TestEvent;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod
// ReSharper disable UnusedVariable

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSpec : AkkaSpec
    {
        private interface IFruit { };
        private sealed class Apple : IFruit { };
        private static IEnumerable<Apple> Apples() => Enumerable.Range(1, 1000).Select(_ => new Apple()); 

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
                akka.actor.debug.receive=off
                akka.loglevel=INFO
                ");

        public ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowSpec(ITestOutputHelper helper) : base(Config.WithFallback(ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf")), helper)
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
        public void A_flow_must_request_initial_elements_from_upstream(string name, int n)
        {
            ChainSetup<int, int, NotUsed> setup;

            if (name.Equals("identity"))
                setup = new ChainSetup<int, int, NotUsed>(Identity, Settings.WithInputBuffer(n, n),
                    (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            else
                setup = new ChainSetup<int, int, NotUsed>(Identity2, Settings.WithInputBuffer(n, n),
                    (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, setup.Settings.MaxInputBufferSize);
        }

        [Fact]
        public void A_Flow_must_request_more_elements_from_upstream_when_downstream_requests_more_elements()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings,
                (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, Settings.MaxInputBufferSize);
            setup.DownstreamSubscription.Request(1);
            setup.Upstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            setup.DownstreamSubscription.Request(2);
            setup.Upstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            setup.UpstreamSubscription.SendNext("a");
            setup.Downstream.ExpectNext("a");
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.Upstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            setup.UpstreamSubscription.SendNext("b");
            setup.UpstreamSubscription.SendNext("c");
            setup.UpstreamSubscription.SendNext("d");
            setup.Downstream.ExpectNext("b");
            setup.Downstream.ExpectNext("c");
        }

        [Fact]
        public void A_Flow_must_deliver_events_when_publisher_sends_elements_and_then_completes()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings,
                (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            setup.DownstreamSubscription.Request(1);
            setup.UpstreamSubscription.SendNext("test");
            setup.UpstreamSubscription.SendComplete();
            setup.Downstream.ExpectNext("test");
            setup.Downstream.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_deliver_complete_signal_when_publisher_immediately_completes()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings,
                  (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            setup.UpstreamSubscription.SendComplete();
            setup.Downstream.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_deliver_error_signal_when_publisher_immediately_fails()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings,
                (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            var weirdError = new Exception("weird test exception");
            setup.UpstreamSubscription.SendError(weirdError);
            setup.Downstream.ExpectError().Should().Be(weirdError);
        }

        [Fact]
        public void A_Flow_must_cancel_upstream_when_single_subscriber_cancels_subscription_while_receiving_data()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings), ToPublisher, this);
            setup.DownstreamSubscription.Request(5);
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("test");
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("test2");
            setup.Downstream.ExpectNext("test");
            setup.Downstream.ExpectNext("test2");
            setup.DownstreamSubscription.Cancel();

            // because of the "must cancel its upstream Subscription if its last downstream Subscription has been canceled" rule
            setup.UpstreamSubscription.ExpectCancellation();
        }

        [Fact]
        public void A_Flow_must_materialize_into_Publisher_Subscriber()
        {
            var flow = Flow.Create<string>();
            var t = MaterializeIntoSubscriberAndPublisher(flow, Materializer);
            var flowIn = t.Item1;
            var flowOut = t.Item2;

            var c1 = this.CreateManualSubscriberProbe<string>();
            flowOut.Subscribe(c1);

            var source = Source.From(new[] {"1", "2", "3"}).RunWith(Sink.AsPublisher<string>(false), Materializer);
            source.Subscribe(flowIn);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(3);
            c1.ExpectNext("1");
            c1.ExpectNext("2");
            c1.ExpectNext("3");
            c1.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_materialize_into_Publisher_Subscriber_and_transformation_processor()
        {
            var flow = Flow.Create<int>().Select(i=>i.ToString());
            var t = MaterializeIntoSubscriberAndPublisher(flow, Materializer);
            var flowIn = t.Item1;
            var flowOut = t.Item2;

            var c1 = this.CreateManualSubscriberProbe<string>();
            flowOut.Subscribe(c1);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(3);
            c1.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            var source = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
            source.Subscribe(flowIn);
            
            c1.ExpectNext("1");
            c1.ExpectNext("2");
            c1.ExpectNext("3");
            c1.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_materialize_into_Publisher_Subscriber_and_multiple_transformation_processor()
        {
            var flow = Flow.Create<int>().Select(i => i.ToString()).Select(s => "elem-" + s);
            var t = MaterializeIntoSubscriberAndPublisher(flow, Materializer);
            var flowIn = t.Item1;
            var flowOut = t.Item2;

            var c1 = this.CreateManualSubscriberProbe<string>();
            flowOut.Subscribe(c1);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(3);
            c1.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            var source = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
            source.Subscribe(flowIn);

            c1.ExpectNext("elem-1");
            c1.ExpectNext("elem-2");
            c1.ExpectNext("elem-3");
            c1.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_subscribe_Subscriber()
        {
            var flow = Flow.Create<string>();
            var c1 = this.CreateManualSubscriberProbe<string>();
            var sink = flow.To(Sink.FromSubscriber(c1));
            var publisher = Source.From(new[] { "1", "2", "3" }).RunWith(Sink.AsPublisher<string>(false), Materializer);
            Source.FromPublisher(publisher).To(sink).Run(Materializer);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(3);
            c1.ExpectNext("1");
            c1.ExpectNext("2");
            c1.ExpectNext("3");
            c1.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_perform_transformation_operation()
        {
            var flow = Flow.Create<int>().Select(i =>
            {
                TestActor.Tell(i.ToString());
                return i.ToString();
            });
            var publisher = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
            Source.FromPublisher(publisher).Via(flow).To(Sink.Ignore<string>()).Run(Materializer);

            ExpectMsg("1");
            ExpectMsg("2");
            ExpectMsg("3");
        }

        [Fact]
        public void A_Flow_must_perform_transformation_operation_and_subscribe_Subscriber()
        {
            var flow = Flow.Create<int>().Select(i => i.ToString());
            var c1 = this.CreateManualSubscriberProbe<string>();
            var sink = flow.To(Sink.FromSubscriber(c1));
            var publisher = Source.From(new[] { 1, 2, 3 }).RunWith(Sink.AsPublisher<int>(false), Materializer);
            Source.FromPublisher(publisher).To(sink).Run(Materializer);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(3);
            c1.ExpectNext("1");
            c1.ExpectNext("2");
            c1.ExpectNext("3");
            c1.ExpectComplete();
        }

        [Fact]
        public void A_Flow_must_be_materializable_several_times_with_fanout_publisher()
        {
            this.AssertAllStagesStopped(() =>
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

                var sub1 = s1.ExpectSubscription();
                var sub2 = s2.ExpectSubscription();
                var sub3 = s3.ExpectSubscription();

                sub1.Request(3);
                s1.ExpectNext("1");
                s1.ExpectNext("2");
                s1.ExpectNext("3");
                s1.ExpectComplete();

                sub2.Request(3);
                sub3.Request(3);
                s2.ExpectNext("1");
                s2.ExpectNext("2");
                s2.ExpectNext("3");
                s2.ExpectComplete();
                s3.ExpectNext("1");
                s3.ExpectNext("2");
                s3.ExpectNext("3");
                s3.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_must_be_covariant()
        {
            Source<IFruit, NotUsed> f1 = Source.From<IFruit>(Apples());
            IPublisher<IFruit> p1 = Source.From<IFruit>(Apples()).RunWith(Sink.AsPublisher<IFruit>(false), Materializer);
            SubFlow<IFruit, NotUsed, IRunnableGraph<NotUsed>> f2 =
                Source.From<IFruit>(Apples()).SplitWhen(_ => true);
            SubFlow<IFruit, NotUsed, IRunnableGraph<NotUsed>> f3 =
                Source.From<IFruit>(Apples()).GroupBy(2, _ => true);
            Source<(IImmutableList<IFruit>, Source<IFruit, NotUsed>), NotUsed> f4 =
                Source.From<IFruit>(Apples()).PrefixAndTail(1);
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
        }

        [Fact]
        public void A_Flow_must_be_possible_to_convert_to_a_processor_and_should_be_able_to_take_a_Processor()
        {
            var identity1 = Flow.Create<int>().ToProcessor();
            var identity2 = Flow.FromProcessor(() => identity1.Run(Materializer));
            var task = Source.From(Enumerable.Range(1, 10))
                .Via(identity2)
                .Limit(100)
                .RunWith(Sink.Seq<int>(), Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1,10));

            // Reusable:
            task = Source.From(Enumerable.Range(1, 10))
                .Via(identity2)
                .Limit(100)
                .RunWith(Sink.Seq<int>(), Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_adapt_speed_to_the_currently_slowest_subscriber()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 1), this);
            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            setup.DownstreamSubscription.Request(5);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1); // because initialInputBufferSize=1

            setup.UpstreamSubscription.SendNext("firstElement");
            setup.Downstream.ExpectNext("firstElement");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("element2");

            setup.Downstream.ExpectNoMsg(TimeSpan.FromSeconds(1));
            downstream2Subscription.Request(1);
            downstream2.ExpectNext("firstElement");

            setup.Downstream.ExpectNext("element2");

            downstream2Subscription.Request(1);
            downstream2.ExpectNext("element2");
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_support_slow_subscriber_with_fan_out_2()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 2), this);
            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            setup.DownstreamSubscription.Request(5);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1); // because initialInputBufferSize=1

            setup.UpstreamSubscription.SendNext("element1");
            setup.Downstream.ExpectNext("element1");

            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("element2");
            setup.Downstream.ExpectNext("element2");
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("element3");
            // downstream2 has not requested anything, fan-out buffer 2
            setup.Downstream.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(100)));

            downstream2Subscription.Request(2);
            setup.Downstream.ExpectNext("element3");
            downstream2.ExpectNext("element1");
            downstream2.ExpectNext("element2");
            downstream2.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(100)));

            setup.UpstreamSubscription.Request(1);
            setup.UpstreamSubscription.SendNext("element4");
            setup.Downstream.ExpectNext("element4");

            downstream2Subscription.Request(2);
            downstream2.ExpectNext("element3");
            downstream2.ExpectNext("element4");

            setup.UpstreamSubscription.SendComplete();
            setup.Downstream.ExpectComplete();
            downstream2.ExpectComplete();
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_support_incoming_subscriber_while_elements_were_requested_before()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 1), this);

            setup.DownstreamSubscription.Request(5);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a1");
            setup.Downstream.ExpectNext("a1");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a2");
            setup.Downstream.ExpectNext("a2");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);

            // link now while an upstream element is already requested
            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            // situation here:
            // downstream 1 now has 3 outstanding
            // downstream 2 has 0 outstanding

            setup.UpstreamSubscription.SendNext("a3");
            setup.Downstream.ExpectNext("a3");
            downstream2.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(100))); // as nothing was requested yet, fanOutBox needs to cache element in this case

            downstream2Subscription.Request(1);
            downstream2.ExpectNext("a3");

            // d1 now has 2 outstanding
            // d2 now has 0 outstanding
            // buffer should be empty so we should be requesting one new element

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1); // because of buffer size 1
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_be_unblocked_when_blocking_subscriber_cancels_subscription()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 1), this);
            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            setup.DownstreamSubscription.Request(5);
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("firstElement");
            setup.Downstream.ExpectNext("firstElement");

            downstream2Subscription.Request(1);
            downstream2.ExpectNext("firstElement");
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("element2");

            setup.Downstream.ExpectNext("element2");
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.SendNext("element3");
            setup.UpstreamSubscription.ExpectRequest(1);


            setup.Downstream.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(200)));
            setup.Upstream.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(200)));
            downstream2.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(200)));

            // should unblock fanoutbox
            downstream2Subscription.Cancel();
            setup.Downstream.ExpectNext("element3");
            setup.UpstreamSubscription.SendNext("element4");
            setup.Downstream.ExpectNext("element4");

            setup.UpstreamSubscription.SendComplete();
            setup.Downstream.ExpectComplete();
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_after_OnSubscribe_if_initial_upstream_was_completed()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 1), this);
            var downstream2 = this.CreateManualSubscriberProbe<string>();
            // don't link it just yet

            setup.DownstreamSubscription.Request(5);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a1");
            setup.Downstream.ExpectNext("a1");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a2");
            setup.Downstream.ExpectNext("a2");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);

            // link now while an upstream element is already requested
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            setup.UpstreamSubscription.SendNext("a3");
            setup.UpstreamSubscription.SendComplete();
            setup.Downstream.ExpectNext("a3");
            setup.Downstream.ExpectComplete();
            
            downstream2.ExpectNoMsg(Dilated(TimeSpan.FromMilliseconds(100))); // as nothing was requested yet, fanOutBox needs to cache element in this case
            
            downstream2Subscription.Request(1);
            downstream2.ExpectNext("a3");
            downstream2.ExpectComplete();

            var downstream3 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream3);
            downstream3.ExpectSubscription();
            downstream3.ExpectError().Should().BeOfType<NormalShutdownException>();
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_should_be_called_instead_of_OnSubscribed_after_initial_upstream_reported_an_error()
        {
            var setup = new ChainSetup<int, string, NotUsed>(flow => flow.Select<int,int,string,NotUsed>(_ =>
            {
                throw new TestException("test");
            }), Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 1), this);

            setup.DownstreamSubscription.Request(1);
            setup.UpstreamSubscription.ExpectRequest(1);

            setup.UpstreamSubscription.SendNext(5);
            setup.UpstreamSubscription.ExpectRequest(1);
            setup.UpstreamSubscription.ExpectCancellation();
            setup.Downstream.ExpectError().Should().BeOfType<TestException>();

            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            downstream2.ExpectSubscriptionAndError().Should().BeOfType<TestException>();
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_must_call_future_subscribers_OnError_when_all_subscriptions_were_cancelled ()
        {
            var setup = new ChainSetup<string, string, NotUsed>(Identity, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 16), this);

            // make sure stream is initialized before canceling downstream
            Thread.Sleep(100);

            setup.UpstreamSubscription.ExpectRequest(1);
            setup.DownstreamSubscription.Cancel();
            setup.UpstreamSubscription.ExpectCancellation();

            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            // IllegalStateException shut down
            downstream2.ExpectSubscriptionAndError().Should().BeAssignableTo<IllegalStateException>();
        }

        [Fact]
        public void A_Flow_with_multiple_subscribers_FanOutBox_should_be_created_from_a_function_easily()
        {
            Source.From(Enumerable.Range(0, 10))
                .Via(Flow.FromFunction<int, int>(i => i + 1))
                .RunWith(Sink.Seq<int>(), Materializer)
                .Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
        }

        [Fact]
        public void A_broken_Flow_must_cancel_upstream_and_call_onError_on_current_and_future_downstream_subscribers_if_an_internal_error_occurs()
        {
            var setup = new ChainSetup<string, string, NotUsed>(FaultyFlow<string,string,string>, Settings.WithInputBuffer(1, 1),
                (settings, factory) => ActorMaterializer.Create(factory, settings),
                (source, materializer) => ToFanoutPublisher(source, materializer, 16), this);

            Action<TestSubscriber.ManualProbe<string>> checkError = sprobe =>
            {
                var error = sprobe.ExpectError();
                error.Should().BeOfType<AbruptTerminationException>();
                error.Message.Should().StartWith("Processor actor");
            };

            var downstream2 = this.CreateManualSubscriberProbe<string>();
            setup.Publisher.Subscribe(downstream2);
            var downstream2Subscription = downstream2.ExpectSubscription();

            setup.DownstreamSubscription.Request(5);
            downstream2Subscription.Request(5);
            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a1");
            setup.Downstream.ExpectNext("a1");
            downstream2.ExpectNext("a1");

            setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
            setup.UpstreamSubscription.SendNext("a2");
            setup.Downstream.ExpectNext("a2");
            downstream2.ExpectNext("a2");

            var filters = new EventFilterBase[]
            {
                new ErrorFilter(typeof(NullReferenceException)),
                new ErrorFilter(typeof(IllegalStateException)),
                new ErrorFilter(typeof(PostRestartException)),// This is thrown because we attach the dummy failing actor to toplevel
            };

            try
            {
                Sys.EventStream.Publish(new Mute(filters));

                setup.Upstream.ExpectRequest(setup.UpstreamSubscription, 1);
                setup.UpstreamSubscription.SendNext("a3");
                setup.UpstreamSubscription.ExpectCancellation();

                // IllegalStateException terminated abruptly
                checkError(setup.Downstream);
                checkError(downstream2);

                var downstream3 = this.CreateManualSubscriberProbe<string>();
                setup.Publisher.Subscribe(downstream3);
                downstream3.ExpectSubscription();
                // IllegalStateException terminated abruptly
                checkError(downstream3);
            }
            finally
            {
                Sys.EventStream.Publish(new Unmute(filters));
            }

        }

        [Fact]
        public void A_broken_Flow_must_suitably_override_attribute_handling_methods()
        {
            var f = Flow.Create<int>().Select(x => x + 1).Async().AddAttributes(Attributes.None).Named("name");
            f.Module.Attributes.GetAttribute<Attributes.Name>().Value.Should().Be("name");
            f.Module.Attributes.GetAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
        }

        [Fact]
        public void A_broken_Flow_must_work_without_fusing()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithAutoFusing(false).WithInputBuffer(1, 1);
            var noFusingMaterializer = ActorMaterializer.Create(Sys, settings);

            // The map followed by a filter is to ensure that it is a CompositeModule passed in to Flow[Int].via(...)
            var sink = Flow.Create<int>().Via(Flow.Create<int>().Select(i => i + 1).Where(_ => true))
                .ToMaterialized(Sink.First<int>(), Keep.Right);
            Source.Single(4711).RunWith(sink, noFusingMaterializer).AwaitResult().ShouldBe(4712);
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
                var next = message as OnNext?;
                if (next.HasValue && next.Value.Id == 0 && next.Value.Event == _brokenMessage)
                    throw new NullReferenceException($"I'm so broken {next.Value.Event}");

                return base.AroundReceive(receive, message);
            }
        }

        private Flow<TIn, TOut2, NotUsed> FaultyFlow<TIn, TOut, TOut2>(Flow<TIn, TOut, NotUsed> flow) where TOut : TOut2
        {
            Func<Flow<TOut, TOut2, NotUsed>> createGraph = () =>
            {
                var stage = new Select<TOut, TOut2>(x => x);
                var assembly = new GraphAssembly(new IGraphStageWithMaterializedValue<Shape, object>[] { stage }, new[] { Attributes.None },
                    new Inlet[] { stage.Shape.Inlet , null}, new[] { 0, -1 }, new Outlet[] { null, stage.Shape.Outlet }, new[] { -1, 0 });

                var t = assembly.Materialize(Attributes.None, assembly.Stages.Select(s => s.Module).ToArray(),
                    new Dictionary<IModule, object>(), _ => { });

                var connections = t.Item1;
                var logics = t.Item2;

                var shell = new GraphInterpreterShell(assembly, connections, logics, stage.Shape, Settings,
                    (ActorMaterializerImpl) Materializer);

                var props =
                    Props.Create(() => new BrokenActorInterpreter(shell, "a3"))
                        .WithDeploy(Deploy.Local)
                        .WithDispatcher("akka.test.stream-dispatcher");
                var impl = Sys.ActorOf(props, "broken-stage-actor");

                var subscriber = new ActorGraphInterpreter.BoundarySubscriber<TOut>(impl, shell, 0);
                var publisher = new FaultyFlowPublisher<TOut2>(impl, shell);

                impl.Tell(new ActorGraphInterpreter.ExposedPublisher(shell, 0, publisher));

                return Flow.FromSinkAndSource(Sink.FromSubscriber(subscriber),
                    Source.FromPublisher(publisher));
            };

            return flow.Via(createGraph());
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
