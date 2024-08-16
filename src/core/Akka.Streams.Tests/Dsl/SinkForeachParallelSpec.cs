﻿//-----------------------------------------------------------------------
// <copyright file="SinkForeachParallelSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Threading;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using System.Threading.Tasks;

namespace Akka.Streams.Tests.Dsl
{
    public class SinkForeachParallelSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SinkForeachParallelSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [LocalFact(SkipLocal = "Racy due to timing on Azure DevOps")]
        public async Task A_ForeachParallel_must_produce_elements_in_the_order_they_are_ready()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = CreateTestProbe();
                var latch = Enumerable.Range(1, 4)
                    .Select(i => (i, new TestLatch(1)))
                    .ToDictionary(t => t.Item1, t => t.Item2);
#pragma warning disable CS0618 // Type or member is obsolete
                var p = Source.From(Enumerable.Range(1, 4)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    latch[n].Ready(TimeSpan.FromSeconds(5));
                    probe.Ref.Tell(n);
                }), Materializer);
#pragma warning restore CS0618 // Type or member is obsolete
                latch[2].CountDown();
                await probe.ExpectMsgAsync(2, TimeSpan.FromSeconds(5));
                latch[4].CountDown();
                await probe.ExpectMsgAsync(4, TimeSpan.FromSeconds(5));
                latch[3].CountDown();
                await probe.ExpectMsgAsync(3, TimeSpan.FromSeconds(5));

                p.IsCompleted.Should().BeFalse();

                latch[1].CountDown();
                await probe.ExpectMsgAsync(1, TimeSpan.FromSeconds(5));

                p.Wait(TimeSpan.FromSeconds(4)).Should().BeTrue();
                p.IsCompleted.Should().BeTrue();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy - timing is rather sensitive on Azure DevOps")]
        public async Task A_ForeachParallel_must_not_run_more_functions_in_parallel_then_specified()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = CreateTestProbe();
                var latch = Enumerable.Range(1, 5)
                    .Select(i => (i, new TestLatch()))
                    .ToDictionary(t => t.Item1, t => t.Item2);
#pragma warning disable CS0618 // Type or member is obsolete
                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    probe.Ref.Tell(n);
                    latch[n].Ready(TimeSpan.FromSeconds(5));
                }), Materializer);
#pragma warning restore CS0618 // Type or member is obsolete

                probe.ExpectMsgAllOf(new[] { 1, 2, 3, 4 });
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                p.IsCompleted.Should().BeFalse();
                
                Enumerable.Range(1, 4).ForEach(i => latch[i].CountDown());

                latch[5].CountDown();
                await probe.ExpectMsgAsync(5);

                p.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                p.IsCompleted.Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public async Task A_ForeachParallel_must_resume_after_function_failure()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = CreateTestProbe();
                var latch = new TestLatch(1);

#pragma warning disable CS0618 // Type or member is obsolete
                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    if (n == 3)
                        throw new TestException("err1");

                    probe.Ref.Tell(n);
                    latch.Ready(TimeSpan.FromSeconds(10));
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)), Materializer);
#pragma warning restore CS0618 // Type or member is obsolete

                latch.CountDown();
                probe.ExpectMsgAllOf(new[] { 1, 2, 4, 5 });

                p.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_ForeachParallel_must_finish_after_function_thrown_exception()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = CreateTestProbe();
                var latch = new TestLatch(1);

#pragma warning disable CS0618 // Type or member is obsolete
                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(3, n =>
                {
                    if (n == 3)
                        throw new TestException("err2");

                    probe.Ref.Tell(n);
                    latch.Ready(TimeSpan.FromSeconds(10));
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider)), Materializer);
#pragma warning restore CS0618 // Type or member is obsolete

                // make sure the stream is up and running, otherwise the latch is maybe ready before the third message arrives
                await Task.Delay(500);
                latch.CountDown();
                probe.ExpectMsgAllOf(new[] { 1, 2 });

                var ex = p.Invoking(t => t.Wait(TimeSpan.FromSeconds(1))).Should().Throw<AggregateException>().Which;
                ex.Flatten().InnerException.Should().BeOfType<TestException>();
                ex.Flatten().InnerException.Message.Should().Be("err2");

                p.IsCompleted.Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public async Task A_ForeachParallel_must_handle_empty_source()
        {
            await this.AssertAllStagesStoppedAsync(() => {
#pragma warning disable CS0618 // Type or member is obsolete
                var p = Source.From(new List<int>()).RunWith(Sink.ForEachParallel<int>(3, _ => { }), Materializer);
#pragma warning restore CS0618 // Type or member is obsolete
                p.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
