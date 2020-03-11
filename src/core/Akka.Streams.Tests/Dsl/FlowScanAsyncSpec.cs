//-----------------------------------------------------------------------
// <copyright file="FlowScanAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Decider = Akka.Streams.Supervision.Decider;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowScanAsyncSpec : AkkaSpec
    {
        public FlowScanAsyncSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = Sys.Materializer();
        }

        private ActorMaterializer Materializer { get; }

        private static readonly Flow<int, int, NotUsed> SumScanFlow = Flow.Create<int>()
            .ScanAsync(0, (accumulator, next) => Task.FromResult(accumulator + next));


        [Fact]
        public void A_ScanAsync_must_work_with_a_empty_source()
        {
            Source.Empty<int>()
                .Via(SumScanFlow)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(1)
                .ExpectNext(0)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_must_work_with_a_single_source()
        {
            Source.Single(1)
                .Via(SumScanFlow)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(2)
                .ExpectNext(0, 1)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_must_work_with_a_large_source()
        {
            var elements = Enumerable.Range(1, 100000).Select(i => (long)i).ToList();
            var expectedSum = elements.Sum();
            var eventualActual = Source.From(elements)
                .ScanAsync(0L, (l, l1) => Task.FromResult(l + l1))
                .RunWith(Sink.Last<long>(), Materializer);
            eventualActual.AwaitResult().ShouldBe(expectedSum);
        }

        [Fact(Skip = "Racy")]
        public void A_ScanAsync_must_work_with_slow_tasks()
        {
            var delay = TimeSpan.FromMilliseconds(500);
            var elements = new [] { 1, 1 };

            Source.From(elements)
                .ScanAsync(0, (i, i1) => Task.Delay(delay).ContinueWith(_ => i + i1))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(3)
                .ExpectNext(TimeSpan.FromMilliseconds(100), 0)
                .ExpectNext(TimeSpan.FromSeconds(1), 1)
                .ExpectNext(TimeSpan.FromSeconds(1), 2)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_must_throw_error_with_a_failed_source()
        {
            var expected = new TestException("failed source");
            Source.Failed<int>(expected)
                .Via(SumScanFlow)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .ExpectSubscriptionAndError()
                .ShouldBe(expected);
        }

        [Fact]
        public void A_ScanAsync_with_the_restarting_decider_must_skip_error_values_with_a_failed_scan()
        {
            var elements = new[] { 1, -1, 1 };

            WhenFailedScan(elements, 0, decider: Deciders.RestartingDecider)
                .ExpectNext(1, 1)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_with_the_restarting_decider_must_emit_zero_with_a_failed_task()
        {
            var elements = new[] { 1, -1, 1 };

            WhenFailedTask(elements, 0, decider: Deciders.RestartingDecider)
                .ExpectNext(1, 1)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_with_the_resuming_decider_must_skip_values_with_a_failed_scan()
        {
            var elements = new[] { 1, -1, 1 };

            WhenFailedScan(elements, 0, decider: Deciders.ResumingDecider)
                .ExpectNext(1, 2)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_with_the_resuming_decider_must_skip_values_with_a_failed_task()
        {
            var elements = new[] { 1, -1, 1 };

            WhenFailedTask(elements, 0, decider: Deciders.ResumingDecider)
                .ExpectNext(1, 2)
                .ExpectComplete();
        }

        [Fact]
        public void A_ScanAsync_with_the_stopping_decider_must_throw_error_with_a_failed_scan_function()
        {
            var expected = new TestException("boom");
            var elements = new[] { -1 };

            WhenFailedScan(elements, 0, expected).ExpectError().ShouldBe(expected);
        }

        [Fact]
        public void A_ScanAsync_with_the_stopping_decider_must_throw_error_with_a_failed_task()
        {
            var expected = new TestException("boom");
            var elements = new[] { -1 };

            WhenFailedTask(elements, 0, expected).ExpectError().InnerException.ShouldBe(expected);
        }

        [Fact]
        public void A_ScanAsync_with_the_stopping_decider_must_throw_error_with_a_null_element()
        {
            const string expectedMessage = ReactiveStreamsCompliance.ElementMustNotBeNullMsg;
            var elements = new[] { "null" };

            var error = WhenNullElement(elements, "").ExpectError();

            error.Should().BeOfType<ArgumentNullException>();
            error.Message.Should().StartWith(expectedMessage);
        }

        private TestSubscriber.ManualProbe<int> WhenFailedScan(ICollection<int> elements, int zero, Exception exception = null,
            Decider decider = null)
        {
            exception = exception ?? new Exception("boom");
            decider = decider ?? Deciders.StoppingDecider;

            return Source.From(elements)
                .ScanAsync(zero, (i, i1) =>
                {
                    if (i1 >= 0)
                        return Task.FromResult(i + i1);

                    throw exception;
                })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(elements.Count + 1)
                .ExpectNext(zero);
        }

        private TestSubscriber.ManualProbe<int> WhenFailedTask(ICollection<int> elements, int zero,
            Exception exception = null,
            Decider decider = null)
        {
            exception = exception ?? new Exception("boom");
            decider = decider ?? Deciders.StoppingDecider;

            return Source.From(elements)
                .ScanAsync(zero, (i, i1) => Task.Run(() =>
                {
                    if (i1 >= 0)
                        return i + i1;

                    throw exception;
                }))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(elements.Count + 1)
                .ExpectNext(zero);
        }

        private TestSubscriber.ManualProbe<string> WhenNullElement(ICollection<string> elements, string zero, Decider decider = null)
        {
            decider = decider ?? Deciders.StoppingDecider;

            return Source.From(elements)
                .ScanAsync(zero, (i, i1) => Task.FromResult(i1 != "null" ? i1 : null))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider))
                .RunWith(this.SinkProbe<string>(), Materializer)
                .Request(elements.Count + 1)
                .ExpectNext(zero);
        }
    }
}

