//-----------------------------------------------------------------------
// <copyright file="FlowIdleInjectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
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
        public void KeepAlive_must_not_emit_additional_elements_if_upstream_is_fastEnough()
        {
            this.AssertAllStagesStopped(() =>
            {
                var result = Source.From(Enumerable.Range(1, 10))
                    .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                    .Grouped(1000)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                result.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void KeepAlive_must_emit_elements_periodically_after_silent_periods()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceWithIdleGap = Source.Combine(Source.From(Enumerable.Range(1, 5)),
                    Source.From(Enumerable.Range(6, 5)).InitialDelay(TimeSpan.FromSeconds(2)),
                    i => new Merge<int, int>(i));
                

                var result = sourceWithIdleGap
                    .KeepAlive(TimeSpan.FromSeconds(0.6), () => 0)
                    .Grouped(1000)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                result.Result.ShouldAllBeEquivalentTo(
                    Enumerable.Range(1, 5).Concat(new[] {0, 0, 0}).Concat(Enumerable.Range(6, 5)));
            }, Materializer);
        }

        [Fact]
        public void KeepAlive_must_immediately_pull_upstream()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.Request(1);

            upstream.SendNext(1);
            downstream.ExpectNext(1);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_immediately_pull_upstream_after_busy_period()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.Request(10);
            downstream.ExpectNextN(10).ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));

            downstream.Request(1);

            upstream.SendNext(1);
            downstream.ExpectNext(1);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_work_if_timer_fires_before_initial_request()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.EnsureSubscription();
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
            downstream.Request(1);
            downstream.ExpectNext(0);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_work_if_timer_fires_before_initial_request_after_busy_period()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.Request(10);
            downstream.ExpectNextN(Enumerable.Range(1, 10));
            
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
            downstream.Request(1);
            downstream.ExpectNext(0);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_prefer_upstream_element_over_injected()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.EnsureSubscription();
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
            upstream.SendNext(1);
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(0.5));

            downstream.Request(1);
            downstream.ExpectNext(1);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_prefer_upstream_element_over_injected_after_busy_period()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.Combine(Source.From(Enumerable.Range(1, 10)), Source.FromPublisher(upstream),
                i => new Merge<int, int>(i))
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.Request(10);
            downstream.ExpectNextN(Enumerable.Range(1, 10));
            
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
            upstream.SendNext(1);
            downstream.ExpectNoMsg(TimeSpan.FromSeconds(0.5));

            downstream.Request(1);
            downstream.ExpectNext(1);

            upstream.SendComplete();
            downstream.ExpectComplete();
        }

        [Fact]
        public void KeepAlive_must_reset_deadline_properly_after_injected_element()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher(upstream)
                .KeepAlive(TimeSpan.FromSeconds(1), () => 0)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.Request(2);
            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            downstream.ExpectNext(0);

            downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            downstream.ExpectNext(0);
        }
    }
}
