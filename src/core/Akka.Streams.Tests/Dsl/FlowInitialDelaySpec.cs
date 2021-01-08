//-----------------------------------------------------------------------
// <copyright file="FlowInitialDelaySpec.cs" company="Akka.NET Project">
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
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

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
        public void Flow_InitialDelay_must_work_with_zero_delay()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.Zero)
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1,10));
            }, Materializer);
        }

        [Fact]
        public void Flow_InitialDelay_must_delay_elements_by_the_specified_time_but_not_more()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.FromSeconds(2))
                    .InitialTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.Ignore<int>(), Materializer);
                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(2))).ShouldThrow<TimeoutException>();
            }, Materializer);
        }

        [Fact]
        public void Flow_InitialDelay_must_properly_ignore_timer_while_backpressured()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.FromSeconds(0.5))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.EnsureSubscription();
                probe.ExpectNoMsg(TimeSpan.FromSeconds(1.5));
                probe.Request(20);
                probe.ExpectNextN(Enumerable.Range(1, 10));

                probe.ExpectComplete();
            }, Materializer);
        }
    }
}
