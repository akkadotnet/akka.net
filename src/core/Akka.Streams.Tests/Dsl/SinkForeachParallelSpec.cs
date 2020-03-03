//-----------------------------------------------------------------------
// <copyright file="SinkForeachParallelSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Threading;

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

        [Fact]
        public void A_ForeachParallel_must_produce_elements_in_the_order_they_are_ready()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();
                var latch = Enumerable.Range(1, 4)
                    .Select(i => (i, new TestLatch(1)))
                    .ToDictionary(t => t.Item1, t => t.Item2);
                var p = Source.From(Enumerable.Range(1, 4)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    latch[n].Ready(TimeSpan.FromSeconds(5));
                    probe.Ref.Tell(n);
                }), Materializer);
                latch[2].CountDown();
                probe.ExpectMsg(2);
                latch[4].CountDown();
                probe.ExpectMsg(4);
                latch[3].CountDown();
                probe.ExpectMsg(3);

                p.IsCompleted.Should().BeFalse();

                latch[1].CountDown();
                probe.ExpectMsg(1);

                p.Wait(TimeSpan.FromSeconds(4)).Should().BeTrue();
                p.IsCompleted.Should().BeTrue();

            }, Materializer);
        }

        [Fact(Skip = "Racy - timing is rather sensitive on Azure DevOps")]
        public void A_ForeachParallel_must_not_run_more_functions_in_parallel_then_specified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();
                var latch = Enumerable.Range(1, 5)
                    .Select(i => (i, new TestLatch()))
                    .ToDictionary(t => t.Item1, t => t.Item2);
                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    probe.Ref.Tell(n);
                    latch[n].Ready(TimeSpan.FromSeconds(5));
                }), Materializer);

                probe.ExpectMsgAllOf(1, 2, 3, 4);
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

                p.IsCompleted.Should().BeFalse();

                Enumerable.Range(1, 4).ForEach(i => latch[i].CountDown());

                latch[5].CountDown();
                probe.ExpectMsg(5);

                p.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                p.IsCompleted.Should().BeTrue();

            }, Materializer);
        }

        [Fact]
        public void A_ForeachParallel_must_resume_after_function_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();
                var latch = new TestLatch(1);

                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(4, n =>
                {
                    if (n == 3)
                        throw new TestException("err1");

                    probe.Ref.Tell(n);
                    latch.Ready(TimeSpan.FromSeconds(10));
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)), Materializer);

                latch.CountDown();
                probe.ExpectMsgAllOf(1, 2, 4, 5);

                p.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public void A_ForeachParallel_must_finish_after_function_thrown_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = CreateTestProbe();
                var latch = new TestLatch(1);

                var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachParallel<int>(3, n =>
                {
                    if (n == 3)
                        throw new TestException("err2");

                    probe.Ref.Tell(n);
                    latch.Ready(TimeSpan.FromSeconds(10));
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider)), Materializer);
                
                // make sure the stream is up and running, otherwise the latch is maybe ready before the third message arrives
                Thread.Sleep(500);
                latch.CountDown();
                probe.ExpectMsgAllOf(1, 2);

                var ex = p.Invoking(t => t.Wait(TimeSpan.FromSeconds(1))).ShouldThrow<AggregateException>().Which;
                ex.Flatten().InnerException.Should().BeOfType<TestException>();
                ex.Flatten().InnerException.Message.Should().Be("err2");

                p.IsCompleted.Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public void A_ForeachParallel_must_handle_empty_source()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = Source.From(new List<int>()).RunWith(Sink.ForEachParallel<int>(3, i => { }), Materializer);
                p.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            }, Materializer);
        }
    }
}
