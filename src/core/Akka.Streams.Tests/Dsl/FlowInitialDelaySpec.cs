//-----------------------------------------------------------------------
// <copyright file="FlowInitialDelaySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowInitialDelaySpec : AkkaSpec
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowInitialDelaySpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public async Task Flow_InitialDelay_must_work_with_zero_delay()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var task = Source.From(Enumerable.Range(1, 10))
                .InitialDelay(TimeSpan.Zero)
                .Grouped(100)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                (await task.WaitAsync(1.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task Flow_InitialDelay_must_delay_elements_by_the_specified_time_but_not_more()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var task = Source.From(Enumerable.Range(1, 10))
                .InitialDelay(TimeSpan.FromSeconds(2))
                .InitialTimeout(TimeSpan.FromSeconds(1))
                .RunWith(Sink.Ignore<int>(), Materializer);
                
                await Awaiting(() => task.WaitAsync(2.Seconds()))
                    .Should().ThrowAsync<TimeoutException>();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task Flow_InitialDelay_must_properly_ignore_timer_while_backpressured()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = this.CreateSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.FromSeconds(0.5))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                await probe.EnsureSubscriptionAsync();
                await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(1.5));
                await probe.RequestAsync(20);
                await probe.ExpectNextNAsync(Enumerable.Range(1, 10));

                await probe.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
