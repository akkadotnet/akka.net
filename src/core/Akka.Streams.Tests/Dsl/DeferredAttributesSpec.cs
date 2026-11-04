//-----------------------------------------------------------------------
// <copyright file="DeferredAttributesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public sealed class DeferredAttributesSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public DeferredAttributesSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Theory(DisplayName = "Deferred operators should pass inherited supervision to their inner graphs")]
        [InlineData("flow", false)]
        [InlineData("flow", true)]
        [InlineData("sink", false)]
        [InlineData("sink", true)]
        [InlineData("source", false)]
        [InlineData("source", true)]
        public async Task Should_resume_inner_processing_when_supervision_is_inherited(string kind, bool delayed)
        {
            var probe = await StartAsync(kind, delayed, false, new InvalidOperationException("bad element"));

            await probe.ExpectNextAsync(1, Timeout, Cancellation);
            await probe.ExpectNextAsync(3, Timeout, Cancellation);
            await probe.ExpectCompleteAsync(cancellationToken: Cancellation);
        }

        [Theory(DisplayName = "Deferred operators should preserve more-specific inner supervision")]
        [InlineData("flow", false)]
        [InlineData("flow", true)]
        [InlineData("sink", false)]
        [InlineData("sink", true)]
        [InlineData("source", false)]
        [InlineData("source", true)]
        public async Task Should_stop_inner_processing_when_inner_supervision_overrides_resume(string kind, bool delayed)
        {
            var failure = new InvalidOperationException("bad element");
            var probe = await StartAsync(kind, delayed, true, failure);

            // A failure may overtake buffered elements. Require the original error,
            // regardless of how many earlier elements have already been delivered.
            for (var i = 0; i < 4; i++)
            {
                var signal = await probe.ExpectEventAsync(Timeout, Cancellation);
                if (signal is TestSubscriber.OnError error)
                {
                    Assert.Same(failure, error.Cause);
                    return;
                }

                Assert.IsType<TestSubscriber.OnNext<int>>(signal);
            }

            Assert.Fail("The inner stopping strategy should fail the stream.");
        }

        private async Task<TestSubscriber.Probe<int>> StartAsync(
            string kind, bool delayed, bool innerStops, Exception failure)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<TGraph> CreateAsync<TGraph>(TGraph graph)
            {
                entered.TrySetResult(true);
                if (delayed)
                    await release.Task.WaitAsync(Cancellation);
                return graph;
            }

            var inner = Flow.Create<int>().Select(value => value == 2 ? throw failure : value);
            if (innerStops)
                inner = inner.WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider));

            var resume = ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider);
            var input = Source.From(new[] { 1, 2, 3 });
            TestSubscriber.Probe<int>? probe = null;
            Task<Option<TestSubscriber.Probe<int>>>? selectedSink = null;

            switch (kind)
            {
                case "flow":
                    probe = input.Via(Flow.LazyInitAsync(() => CreateAsync(inner)).WithAttributes(resume))
                        .RunWith(this.SinkProbe<int>(), _materializer);
                    break;
                case "sink":
                    var sink = inner.ToMaterialized(this.SinkProbe<int>(), Keep.Right);
                    selectedSink = input.RunWith(
                        Sink.LazyInitAsync(() => CreateAsync(sink)).WithAttributes(resume), _materializer);
                    break;
                case "source":
                    probe = Source.FromTaskSource(CreateAsync(input.Via(inner))).WithAttributes(resume)
                        .RunWith(this.SinkProbe<int>(), _materializer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            try
            {
                // RequestAsync waits for subscription. For the task-backed source,
                // this also lets the outer stream start while the source task is pending.
                if (probe is not null)
                    await probe.RequestAsync(3);
                await entered.Task.WaitAsync(Timeout, Cancellation);
            }
            finally
            {
                release.TrySetResult(true);
            }

            if (selectedSink is not null)
            {
                probe = (await selectedSink.WaitAsync(Timeout, Cancellation)).Value;
                await probe.RequestAsync(3);
            }

            return probe ?? throw new InvalidOperationException("No subscriber was materialized.");
        }
    }
}
