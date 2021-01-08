//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class ActorRefSourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public ActorRefSourceSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }
        
        [Fact]
        public void A_ActorRefSource_must_emit_received_messages_to_the_stream()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                .To(Sink.FromSubscriber(s))
                .Run(Materializer);
            var sub = s.ExpectSubscription();
            sub.Request(2);
            actorRef.Tell(1);
            s.ExpectNext(1);
            actorRef.Tell(2);
            s.ExpectNext(2);
            actorRef.Tell(3);
            s.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }
        
        [Fact]
        public void A_ActorRefSource_must_buffer_when_needed()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var actorRef = Source.ActorRef<int>(100, OverflowStrategy.DropHead)
                .To(Sink.FromSubscriber(s))
                .Run(Materializer);
            var sub = s.ExpectSubscription();
            Enumerable.Range(1, 20).ForEach(x => actorRef.Tell(x));
            sub.Request(10);
            Enumerable.Range(1, 10).ForEach(x => s.ExpectNext(x));
            sub.Request(10);
            Enumerable.Range(11, 10).ForEach(x => s.ExpectNext(x));

            Enumerable.Range(200, 200).ForEach(x => actorRef.Tell(x));
            sub.Request(100);
            Enumerable.Range(300, 100).ForEach(x => s.ExpectNext(x));
        }

        [Fact]
        public void A_ActorRefSource_must_drop_new_when_full_and_with_DropNew_strategy()
        {
            var t = Source.ActorRef<int>(100, OverflowStrategy.DropNew)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var actorRef = t.Item1;
            var sub = t.Item2;

            Enumerable.Range(1, 20).ForEach(x => actorRef.Tell(x));
            sub.Request(10);
            Enumerable.Range(1, 10).ForEach(x => sub.ExpectNext(x));
            sub.Request(10);
            Enumerable.Range(11, 10).ForEach(x => sub.ExpectNext(x));

            Enumerable.Range(200, 200).ForEach(x => actorRef.Tell(x));
            sub.Request(100);
            Enumerable.Range(200, 100).ForEach(x => sub.ExpectNext(x));
        }

        [Fact]
        public void A_ActorRefSource_must_terminate_when_the_stream_is_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(0, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                Watch(actorRef);
                var sub = s.ExpectSubscription();
                sub.Cancel();
                ExpectTerminated(actorRef);
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_not_fail_when_0_buffer_space_and_demand_is_signalled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(0, OverflowStrategy.DropHead)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                Watch(actorRef);
                var sub = s.ExpectSubscription();
                sub.Request(100);
                sub.Cancel();
                ExpectTerminated(actorRef);
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_completes_the_stream_immediately_when_receiving_PoisonPill()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                s.ExpectSubscription();
                actorRef.Tell(PoisonPill.Instance);
                s.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_signal_buffered_elements_and_complete_the_stream_after_receiving_Status_Success()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                var sub = s.ExpectSubscription();
                actorRef.Tell(1);
                actorRef.Tell(2);
                actorRef.Tell(3);
                actorRef.Tell(new Status.Success("ok"));
                sub.Request(10);
                s.ExpectNext(1, 2, 3);
                s.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_not_buffer_elements_after_receiving_Status_Success()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(3, OverflowStrategy.DropBuffer)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                var sub = s.ExpectSubscription();
                actorRef.Tell(1);
                actorRef.Tell(2);
                actorRef.Tell(3);
                actorRef.Tell(new Status.Success("ok"));
                actorRef.Tell(100);
                actorRef.Tell(100);
                actorRef.Tell(100);
                sub.Request(10);
                s.ExpectNext(1, 2, 3);
                s.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_after_receiving_Status_Success_allow_for_earlier_completion_with_PoisonPill()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(3, OverflowStrategy.DropBuffer)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                var sub = s.ExpectSubscription();
                actorRef.Tell(1);
                actorRef.Tell(2);
                actorRef.Tell(3);
                actorRef.Tell(new Status.Success("ok"));
                sub.Request(2); // not all elements drained yet
                s.ExpectNext(1, 2);
                actorRef.Tell(PoisonPill.Instance);
                s.ExpectComplete(); // element `3` not signaled
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_fail_the_stream_when_receiving_Status_Failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                s.ExpectSubscription();
                var ex = new TestException("testfailure");
                actorRef.Tell(new Status.Failure(ex));
                s.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void A_ActorRefSource_must_set_actor_name_equal_to_stage_name()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                const string name = "SomeCustomName";
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .WithAttributes(Attributes.CreateName(name))
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                actorRef.Path.ToString().Should().Contain(name);
                actorRef.Tell(PoisonPill.Instance);
            }, Materializer);
        }
    }
}
