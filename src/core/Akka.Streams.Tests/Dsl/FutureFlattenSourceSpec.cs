//-----------------------------------------------------------------------
// <copyright file="FutureFlattenSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FutureFlattenSourceSpec : AkkaSpec
    {
        private readonly IMaterializer _materializer;

        private static readonly Source<int, string> Underlying =
            Source.From(new List<int> { 1, 2, 3 }).MapMaterializedValue(_ => "foo");

        public FutureFlattenSourceSpec(ITestOutputHelper helper): base(helper)
        {
            _materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task TaskSource_must_emit_the_elements_of_the_already_successful_task_source()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(Task.FromResult(Underlying))
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                // wait until the underlying task is completed
                await sourceMatVal.ShouldCompleteWithin(3.Seconds());
                await sinkMatVal.ShouldCompleteWithin(3.Seconds());

                // should complete as soon as inner source has been materialized
                sourceMatVal.Result.Should().Be("foo");
                sinkMatVal.Result.Should().BeEquivalentTo(ImmutableList.CreateRange(new List<int>() { 1, 2, 3 }));
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_emit_no_elements_before_the_task_of_source_successful()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();

                Source.FromTaskSource(sourcePromise.Task).RunWith(Sink.AsPublisher<int>(true), _materializer).Subscribe(c);
                
                var sub = await c.ExpectSubscriptionAsync();
                await c.AsyncBuilder().ExpectNoMsg(TimeSpan.FromMilliseconds(100)).ExecuteAsync();
                sub.Request(3);
                await c.AsyncBuilder().ExpectNoMsg(TimeSpan.FromMilliseconds(100)).ExecuteAsync();
                sourcePromise.SetResult(Underlying);
                await c.AsyncBuilder()
                    .ExpectNext(1, 2, 3)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_emit_the_elements_of_the_task_source()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();

                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(sourcePromise.Task)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                sourcePromise.SetResult(Underlying);

                // wait until the underlying task is completed
                await sourceMatVal.ShouldCompleteWithin(3.Seconds());
                await sinkMatVal.ShouldCompleteWithin(3.Seconds());
                
                // should complete as soon as inner source has been materialized
                sourceMatVal.Result.Should().Be("foo");
                sinkMatVal.Result.Should().BeEquivalentTo(ImmutableList.CreateRange(new List<int>() { 1, 2, 3 }));
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_handle_downstream_cancelling_before_the_underlying_task_completes()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var probe = this.CreateSubscriberProbe<int>();

                var sourceMatVal = Source.FromTaskSource(sourcePromise.Task)
                    .ToMaterialized(Sink.FromSubscriber(probe), Keep.Left)
                    .Run(_materializer);

                // wait for cancellation to occur
                await probe.AsyncBuilder()
                    .EnsureSubscription()
                    .Request(1)
                    .Cancel()
                    .ExecuteAsync();

                // try to avoid a race between probe cancel and completing the promise
                await Task.Delay(100);
                
                // even though canceled the underlying matval should arrive
                sourcePromise.SetResult(Underlying);
                
                // wait until the underlying task is completed
                var ex = await sourceMatVal.ShouldThrowWithin<StreamDetachedException>(3.Seconds());
                ex.Message.Should().Be("Stream cancelled before Source Task completed");
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_fail_if_the_underlying_task_is_failed()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failure = new TestException("foo");
                var underlying = Task.FromException<Source<int, string>>(failure);

                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(underlying)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                // wait until the underlying task is completed
                await sourceMatVal.ShouldThrowWithin(failure, 3.Seconds());
                await sinkMatVal.ShouldThrowWithin(failure, 3.Seconds());
            }, _materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task TaskSource_must_fail_as_the_underlying_task_fails_after_outer_source_materialization()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failure = new TestException("foo");
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var materializationLatch = new TestLatch(1);

                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(sourcePromise.Task)
                    .MapMaterializedValue(value =>
                    {
                        materializationLatch.CountDown();
                        return value;
                    })
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                // we don't know that materialization completed yet
                materializationLatch.Ready(RemainingOrDefault);                
                sourcePromise.SetException(failure);
                
                // wait until the underlying tasks are completed
                await sourceMatVal.ShouldThrowWithin(failure, 3.Seconds());
                await sinkMatVal.ShouldThrowWithin(failure, 3.Seconds());
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_fail_as_the_underlying_task_fails_after_outer_source_materialization_with_no_demand()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failure = new TestException("foo");
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var testProbe = this.CreateSubscriberProbe<int>();

                var sourceMatVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(testProbe))
                    .Run(_materializer);

                await testProbe.ExpectSubscriptionAsync();
                sourcePromise.SetException(failure);
                
                // wait until the underlying tasks are completed
                await sourceMatVal.ShouldThrowWithin(failure, 3.Seconds());
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_handle_backpressure_when_the_task_completes()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();
                var publisher = this.CreatePublisherProbe<int>();

                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var matVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(_materializer);

                await subscriber.EnsureSubscriptionAsync();
                sourcePromise.SetResult(Source.FromPublisher(publisher).MapMaterializedValue(_ => "woho"));

                await matVal.ShouldCompleteWithin(3.Seconds());
                // materialized value completes but still no demand
                matVal.Result.Should().Be("woho");

                // then demand and let an element through to see it works
                await subscriber.AsyncBuilder().Request(1).ExecuteAsync();
                await publisher.ExpectRequestAsync();
                publisher.SendNext(1);
                await subscriber.AsyncBuilder().ExpectNext(1).ExecuteAsync();
                publisher.SendComplete();
                await subscriber.AsyncBuilder().ExpectComplete().ExecuteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_carry_through_cancellation_to_later_materialized_source()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();
                var publisher = this.CreatePublisherProbe<int>();

                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var matVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(_materializer);

                await subscriber.EnsureSubscriptionAsync();
                sourcePromise.SetResult(Source.FromPublisher(publisher).MapMaterializedValue(_ => "woho"));

                await matVal.ShouldCompleteWithin(3.Seconds());
                // materialized value completes but still no demand
                matVal.Result.Should().Be("woho");

                // then demand and let an element through to see it works
                await subscriber.AsyncBuilder()
                    .EnsureSubscription()
                    .Cancel()
                    .ExecuteAsync();
                await publisher.ExpectCancellationAsync();
            }, _materializer);
        }

        [Fact]
        public async Task TaskSource_must_fail_when_the_task_source_materialization_fails()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var inner = Task.FromResult(Source.FromGraph(new FailingMatGraphStage()));
                var (innerSourceMat, outerSinkMat) = Source.FromTaskSource(inner).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(_materializer);

                // wait until the underlying tasks are completed
                await outerSinkMat.ShouldThrowWithin(FailingMatGraphStage.Exception, 3.Seconds());
                await innerSourceMat.ShouldThrowWithin(FailingMatGraphStage.Exception, 3.Seconds());
            }, _materializer);
        }

        private class FailingMatGraphStage : GraphStageWithMaterializedValue<SourceShape<int>, string>
        {
            public static readonly TestException Exception = new("INNER_FAILED");
            
            private readonly Outlet<int> _out = new("whatever");

            public override SourceShape<int> Shape => new(_out);

            public override ILogicAndMaterializedValue<string> CreateLogicAndMaterializedValue(Attributes inheritedAttributes) => 
                throw Exception;
        }
    }
}
