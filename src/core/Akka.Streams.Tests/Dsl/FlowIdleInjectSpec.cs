﻿//-----------------------------------------------------------------------
// <copyright file="FlowIdleInjectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowIdleInjectSpec : AkkaSpec
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowIdleInjectSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public async Task KeepAlive_must_not_emit_additional_elements_if_upstream_is_fastEnough()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var task = Source.From(Enumerable.Range(1, 10))                                                                             
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)                                                                             
                .Grouped(1000)                                                                             
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                (await task.WaitAsync(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task KeepAlive_must_emit_elements_periodically_after_silent_periods()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var sourceWithIdleGap = Source.Combine(Source.From(Enumerable.Range(1, 5)),                                                                             
                    Source.From(Enumerable.Range(6, 5)).InitialDelay(TimeSpan.FromSeconds(2)),                                                                             
                    i => new Merge<int, int>(i));
                var task = sourceWithIdleGap
                    .KeepAlive(TimeSpan.FromSeconds(0.6), () => 0)
                    .Grouped(1000)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                (await task.WaitAsync(3.Seconds())).Should().BeEquivalentTo(
                    Enumerable.Range(1, 5).Concat(new[] { 0, 0, 0 }).Concat(Enumerable.Range(6, 5)));
            }, Materializer);
        }

        [Fact]
        public async Task KeepAlive_must_immediately_pull_upstream()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.RequestAsync(1);

            await upstream.SendNextAsync(1);
            await downstream.ExpectNextAsync(1);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_immediately_pull_upstream_after_busy_period()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.RequestAsync(10);
            downstream.ExpectNextN(10).Should().BeEquivalentTo(Enumerable.Range(1, 10));

            await downstream.RequestAsync(1);

            await upstream.SendNextAsync(1);
            await downstream.ExpectNextAsync(1);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_work_if_timer_fires_before_initial_request()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.EnsureSubscriptionAsync();
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1.5));
            await downstream.RequestAsync(1);
            await downstream.ExpectNextAsync(0);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_work_if_timer_fires_before_initial_request_after_busy_period()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.RequestAsync(10);
            downstream.ExpectNextN(Enumerable.Range(1, 10));
            
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1.5));
            await downstream.RequestAsync(1);
            await downstream.ExpectNextAsync(0);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_prefer_upstream_element_over_injected()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.EnsureSubscriptionAsync();
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1.5));
            await upstream.SendNextAsync(1);
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(0.5));

            await downstream.RequestAsync(1);
            await downstream.ExpectNextAsync(1);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_prefer_upstream_element_over_injected_after_busy_period()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.RequestAsync(10);
            downstream.ExpectNextN(Enumerable.Range(1, 10));
            
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1.5));
            await upstream.SendNextAsync(1);
            await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(0.5));

            await downstream.RequestAsync(1);
            await downstream.ExpectNextAsync(1);

            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }

        [Fact]
        public async Task KeepAlive_must_reset_deadline_properly_after_injected_element()
        {
            var upstream = this.CreatePublisherProbe<int>();
            var downstream = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.RequestAsync(2);
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
            await downstream.ExpectNextAsync(0);

            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
            await downstream.ExpectNextAsync(0);
        }
    }
}
