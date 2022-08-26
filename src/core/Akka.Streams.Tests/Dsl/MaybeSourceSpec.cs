//-----------------------------------------------------------------------
// <copyright file="MaybeSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class MaybeSourceSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;
        public MaybeSourceSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact(DisplayName = "The Maybe Source must complete materialized promise with None when stream cancels")]
        public void CompleteMaterializedPromiseWithNoneWhenCancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>();
                var pubSink = Sink.AsPublisher<int>(false);

                var (tcs, neverPub) = neverSource
                    .ToMaterialized(pubSink, Keep.Both)
                    .Run(_materializer);

                var c = this.CreateManualSubscriberProbe<int>();
                neverPub.Subscribe(c);
                var subs = c.ExpectSubscription();

                subs.Request(1000);
                c.ExpectNoMsg(100.Milliseconds());
                
                subs.Cancel();

                tcs.Task.Wait(3.Seconds()).Should().BeTrue();
                tcs.Task.Result.Should().Be(0);
            }, _materializer);
        }

        [Fact(DisplayName = "The Maybe Source must complete materialized promise with 0 when stream cancels with a failure cause")]
        public void CompleteMaterializedTaskWithNoneWhenStreamCancelsWithFailure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (tcs, killSwitch) = Source.Maybe<int>()
                    .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                    .To(Sink.Ignore<int>())
                    .Run(_materializer);

                var boom = new TestException("Boom");
                killSwitch.Abort(boom);
                // Could make sense to fail it with the propagated exception instead but that breaks
                // the assumptions in the CoupledTerminationFlowSpec
                tcs.Task.Wait(3.Seconds()).Should().BeTrue();
                tcs.Task.Result.Should().Be(0);
            }, _materializer);
        }

        [Fact(DisplayName = "The Maybe Source must allow external triggering of empty completion")]
        public void AllowExternalTriggeringOfEmptyCompletion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>().Where(_ => false);
                var counterSink = Sink.Aggregate<int, int>(0, (acc, _) => acc + 1);
                var (neverPromise, counterFuture) = neverSource
                    .ToMaterialized(counterSink, Keep.Both)
                    .Run(_materializer);
            
                // external cancellation
                neverPromise.TrySetResult(0).Should().BeTrue();
                counterFuture.Wait(3.Seconds()).Should().BeTrue();
                counterFuture.Result.Should().Be(0);
            }, _materializer);
        }

        // MaybeSource code is different compared to JVM, maybe that's why? -- Greg
        // TODO: Why isn't the probe receive an OnComplete?
        [Fact(
            DisplayName = "The Maybe Source must allow external triggering of empty completion when there was no demand",
            Skip = "Not working, check Maybe<T> source.")]
        public void AllowExternalTriggerOfEmptyCompletionWhenNoDemand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateSubscriberProbe<int>();
                var promise = Source
                    .Maybe<int>()
                    .To(Sink.FromSubscriber(probe))
                    .Run(_materializer);
            
                // external cancellation
                probe.EnsureSubscription();
                promise.TrySetResult(0).Should().BeTrue();
                probe.ExpectComplete();
            }, _materializer);
        }

        [Fact(DisplayName = "The Maybe Source must allow external triggering of non-empty completion")]
        public void AllowExternalTriggerNonEmptyCompletion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>();
                var counterSink = Sink.First<int>();

                var (neverPromise, counterFuture) = neverSource
                    .ToMaterialized(counterSink, Keep.Both)
                    .Run(_materializer);
            
                // external cancellation
                neverPromise.TrySetResult(6).Should().BeTrue();
                counterFuture.Wait(3.Seconds()).Should().BeTrue();
                counterFuture.Result.Should().Be(6);
            }, _materializer);
        }

        [Fact(DisplayName = "The Maybe Source must allow external triggering of onError")]
        public void AllowExternalTriggerOnError()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>();
                var counterSink = Sink.Aggregate<int, int>(0, (acc, _) => acc + 1);

                var (neverPromise, counterFuture) = neverSource
                    .ToMaterialized(counterSink, Keep.Both)
                    .Run(_materializer);
            
                // external cancellation
                neverPromise.TrySetException(new TestException("Boom")).Should().BeTrue();

                Invoking(() => counterFuture.Wait(3.Seconds()))
                    .Should().Throw<AggregateException>()
                    .WithInnerException<AggregateException>()
                    .WithInnerException<TestException>()
                    .WithMessage("Boom");
            }, _materializer);
        }

        // MaybeSource code is different compared to JVM, maybe that's why? -- Greg
        // TODO: Why isn't Maybe<T> throws AbruptStageTerminationException?
        [Fact(
            DisplayName = "The Maybe Source must complete materialized future when materializer is shutdown",
            Skip = "Not working, no exception is thrown. Check Maybe<T> source.")]
        public void CompleteMaterializedTaskWhenShutDown()
        {
            var mat = ActorMaterializer.Create(Sys);
            var neverSource = Source.Maybe<int>();
            var pubSink = Sink.AsPublisher<int>(false);

            var (f, neverPub) = neverSource
                .ToMaterialized(pubSink, Keep.Both)
                .Run(mat);

            var c = this.CreateManualSubscriberProbe<int>();
            neverPub.Subscribe(c);
            c.ExpectSubscription();
            
            mat.Shutdown();
            Invoking(() => f.Task.Wait(3.Seconds())).Should()
                .Throw<AbruptStageTerminationException>();
        }
    }
}
