﻿//-----------------------------------------------------------------------
// <copyright file="FlowFromTaskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowFromTaskSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowFromTaskSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_produce_one_element_from_already_successful_Future()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = Source.FromTask(Task.FromResult(1)).RunWith(Sink.AsPublisher<int>(true), Materializer);
                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                sub.Request(1);
                await c.ExpectNextAsync(1);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_produce_error_from_already_failed_Task()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var ex = new TestException("test");
                var c = this.CreateManualSubscriberProbe<int>();
                var p =
                    Source.FromTask(Task.Run(new Func<int>(() => { throw ex; })))
                        .RunWith(Sink.AsPublisher<int>(false), Materializer);
                p.Subscribe(c);
                c.ExpectSubscriptionAndError().Should().Be(ex);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_produce_one_element_when_Task_is_completed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var promise = new TaskCompletionSource<int>();
                var c = this.CreateManualSubscriberProbe<int>();
                var p = Source.FromTask(promise.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(1);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                promise.SetResult(1);
                await c.ExpectNextAsync(1);
                await c.ExpectCompleteAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_produce_one_element_when_Task_is_completed_but_not_before_request()
        {
            var promise = new TaskCompletionSource<int>();
            var c = this.CreateManualSubscriberProbe<int>();
            var p = Source.FromTask(promise.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            p.Subscribe(c);
            var sub = await c.ExpectSubscriptionAsync();
            promise.SetResult(1);
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            sub.Request(1);
            await c.ExpectNextAsync(1);
            await c.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_produce_elements_with_multiple_subscribers()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var promise = new TaskCompletionSource<int>();
                var p = Source.FromTask(promise.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                p.Subscribe(c1);
                p.Subscribe(c2);
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();
                sub1.Request(1);
                promise.SetResult(1);
                sub2.Request(2);
                await c1.ExpectNextAsync(1);
                await c2.ExpectNextAsync(1);
                await c1.ExpectCompleteAsync();
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_Task_must_allow_cancel_before_receiving_element()
        {
            var promise = new TaskCompletionSource<int>();
            var c = this.CreateManualSubscriberProbe<int>();
            var p = Source.FromTask(promise.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            var keepAlive = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(keepAlive);
            p.Subscribe(c);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(1);
            sub.Cancel();
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
            promise.SetResult(1);
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
        }
    }
}
