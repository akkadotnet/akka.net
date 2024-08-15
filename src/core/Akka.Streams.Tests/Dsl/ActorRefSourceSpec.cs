﻿//-----------------------------------------------------------------------
// <copyright file="ActorRefSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task A_ActorRefSource_must_emit_received_messages_to_the_stream()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                .To(Sink.FromSubscriber(s))
                .Run(Materializer);
            var sub = await s.ExpectSubscriptionAsync();
            sub.Request(2);
            actorRef.Tell(1);
            await s.ExpectNextAsync(1);
            actorRef.Tell(2);
            await s.ExpectNextAsync(2);
            actorRef.Tell(3);
            await s.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public async Task A_ActorRefSource_must_buffer_when_needed()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var actorRef = Source.ActorRef<int>(100, OverflowStrategy.DropHead)
                .To(Sink.FromSubscriber(s))
                .Run(Materializer);
            var sub = await s.ExpectSubscriptionAsync();
            foreach (var x in Enumerable.Range(1, 20))
                actorRef.Tell(x);

            sub.Request(10);
            foreach (var x in Enumerable.Range(1, 10))
                await s.ExpectNextAsync(x);
            sub.Request(10);
            foreach (var x in Enumerable.Range(11, 10))
                await s.ExpectNextAsync(x);

            foreach (var x in Enumerable.Range(200, 200))
                actorRef.Tell(x);
            sub.Request(100);

            foreach (var x in Enumerable.Range(300, 100))
                await s.ExpectNextAsync(x);

        }

        [Fact]
        public async Task A_ActorRefSource_must_drop_new_when_full_and_with_DropNew_strategy()
        {
            var t = Source.ActorRef<int>(100, OverflowStrategy.DropNew)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Materializer);
            var actorRef = t.Item1;
            var sub = t.Item2;

            foreach (var x in Enumerable.Range(1, 20))
                actorRef.Tell(x);

            sub.Request(10);

            foreach (var x in Enumerable.Range(1, 10))
                await sub.ExpectNextAsync(x);

            sub.Request(10);

            foreach (var x in Enumerable.Range(11, 10))
                await sub.ExpectNextAsync(x);

            foreach (var x in Enumerable.Range(200, 200))
                actorRef.Tell(x);

            sub.Request(100);
            foreach(var x in Enumerable.Range(200, 100))
                await sub.ExpectNextAsync(x);
        }

        [Fact]
        public async Task A_ActorRefSource_must_terminate_when_the_stream_is_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(0, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                Watch(actorRef);
                var sub = await s.ExpectSubscriptionAsync();
                sub.Cancel();
                ExpectTerminated(actorRef);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_not_fail_when_0_buffer_space_and_demand_is_signalled()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(0, OverflowStrategy.DropHead)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                Watch(actorRef);
                var sub = await s.ExpectSubscriptionAsync();
                sub.Request(100);
                sub.Cancel();
                ExpectTerminated(actorRef);
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_signal_buffered_elements_and_complete_the_stream_after_receiving_Status_Success()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                var sub = await s.ExpectSubscriptionAsync();
                actorRef.Tell(1);
                actorRef.Tell(2);
                actorRef.Tell(3);
                actorRef.Tell(new Status.Success("ok"));
                sub.Request(10);
                s.ExpectNext(1, 2, 3);
                await s.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_not_buffer_elements_after_receiving_Status_Success()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(3, OverflowStrategy.DropBuffer)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                var sub = await s.ExpectSubscriptionAsync();
                actorRef.Tell(1);
                actorRef.Tell(2);
                actorRef.Tell(3);
                actorRef.Tell(new Status.Success("ok"));
                actorRef.Tell(100);
                actorRef.Tell(100);
                actorRef.Tell(100);
                sub.Request(10);
                s.ExpectNext(1, 2, 3);
                await s.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_complete_and_materialize_the_stream_after_receiving_Status_Success()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var (actorRef, done) = Source.ActorRef<int>(3, OverflowStrategy.DropBuffer)                                                                             
                .ToMaterialized(Sink.Ignore<int>(), Keep.Both)                                                                             
                .Run(Materializer);
                actorRef.Tell(new Status.Success("ok"));
                done.ContinueWith(_ => Done.Instance).Result.Should().Be(Done.Instance);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_fail_the_stream_when_receiving_Status_Failure()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                await s.ExpectSubscriptionAsync();
                var ex = new TestException("testfailure");
                actorRef.Tell(new Status.Failure(ex));
                s.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_ActorRefSource_must_set_actor_name_equal_to_stage_name()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var s = this.CreateManualSubscriberProbe<int>();
                const string name = "SomeCustomName";
                var actorRef = Source.ActorRef<int>(10, OverflowStrategy.Fail)
                    .WithAttributes(Attributes.CreateName(name))
                    .To(Sink.FromSubscriber(s))
                    .Run(Materializer);
                actorRef.Path.ToString().Should().Contain(name);
                actorRef.Tell(PoisonPill.Instance);
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
