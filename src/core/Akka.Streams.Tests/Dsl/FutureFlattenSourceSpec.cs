//-----------------------------------------------------------------------
// <copyright file="FutureFlattenSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class FutureFlattenSourceSpec : AkkaSpec
    {
        private readonly IMaterializer _materializer;

        private static readonly Source<int, string> underlying =
            Source.From(new List<int>() { 1, 2, 3 }).MapMaterializedValue(_ => "foo");

        public FutureFlattenSourceSpec() => _materializer = ActorMaterializer.Create(Sys);

        [Fact]
        public void TaskSource_must_emit_the_elements_of_the_already_successful_task_source()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(Task.FromResult(underlying))
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                sourceMatVal.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                sinkMatVal.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                // should complete as soon as inner source has been materialized
                sourceMatVal.Result.Should().Be("foo");
                sinkMatVal.Result.Should().BeEquivalentTo(ImmutableList.CreateRange(new List<int>() { 1, 2, 3 }));
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_emit_no_elements_before_the_task_of_source_successful()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();

                Source.FromTaskSource(sourcePromise.Task).RunWith(Sink.AsPublisher<int>(true), _materializer).Subscribe(c);
                var sub = c.ExpectSubscription();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                sub.Request(3);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                sourcePromise.SetResult(underlying);
                c.ExpectNext(1);
                c.ExpectNext(2);
                c.ExpectNext(3);
                c.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_emit_the_elements_of_the_task_source()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();

                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(sourcePromise.Task)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                sourcePromise.SetResult(underlying);

                // should complete as soon as inner source has been materialized
                sourceMatVal.Result.Should().Be("foo");
                sinkMatVal.Result.Should().BeEquivalentTo(ImmutableList.CreateRange(new List<int>() { 1, 2, 3 }));
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_handle_downstream_cancelling_before_the_underlying_task_completes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var probe = this.CreateSubscriberProbe<int>();

                var sourceMatVal = Source.FromTaskSource(sourcePromise.Task)
                    .ToMaterialized(Sink.FromSubscriber(probe), Keep.Left)
                    .Run(_materializer);

                // wait for cancellation to occur
                probe.EnsureSubscription();
                probe.Request(1);
                probe.Cancel();

                // try to avoid a race between probe cancel and completing the promise
                Thread.Sleep(100);

                // even though canceled the underlying matval should arrive
                sourcePromise.SetResult(underlying);
                var failure = sourceMatVal.Exception.Flatten().InnerException;
                failure.Should().BeAssignableTo<StreamDetachedException>();
                failure.Message.Should().Be("Stream cancelled before Source Task completed");
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_fail_if_the_underlying_task_is_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var failure = new TestException("foo");
                var underlying = Task.FromException<Source<int, string>>(failure);

                var (sourceMatVal, sinkMatVal) = Source.FromTaskSource(underlying)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(_materializer);

                // wait until the underlying task is completed
                Thread.Sleep(100);

                sourceMatVal.Exception.Flatten().InnerException.Should().Be(failure);
                sinkMatVal.Exception.Flatten().InnerException.Should().Be(failure);
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_fail_as_the_underlying_task_fails_after_outer_source_materialization()
        {
            this.AssertAllStagesStopped(() =>
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

                // we don't know that materialization completed yet (this is still a bit racy)
                materializationLatch.Ready(RemainingOrDefault);                
                sourcePromise.SetException(failure);
                Thread.Sleep(100);

                sourceMatVal.Exception.Flatten().InnerException.Should().Be(failure);
                sinkMatVal.Exception.Flatten().InnerException.Should().Be(failure);
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_fail_as_the_underlying_task_fails_after_outer_source_materialization_with_no_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var failure = new TestException("foo");
                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var testProbe = this.CreateSubscriberProbe<int>();

                var sourceMatVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(testProbe))
                    .Run(_materializer);

                testProbe.ExpectSubscription();
                sourcePromise.SetException(failure);
                Thread.Sleep(100);

                sourceMatVal.Exception.Flatten().InnerException.Should().Be(failure);
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_handle_backpressure_when_the_task_completes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();
                var publisher = this.CreatePublisherProbe<int>();

                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var matVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(_materializer);

                subscriber.EnsureSubscription();
                sourcePromise.SetResult(Source.FromPublisher(publisher).MapMaterializedValue(_ => "woho"));

                // materialized value completes but still no demand
                matVal.Result.Should().Be("woho");

                // then demand and let an element through to see it works
                subscriber.EnsureSubscription();
                subscriber.Request(1);
                publisher.ExpectRequest();
                publisher.SendNext(1);
                subscriber.ExpectNext(1);
                publisher.SendComplete();
                subscriber.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_carry_through_cancellation_to_later_materialized_source()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();
                var publisher = this.CreatePublisherProbe<int>();

                var sourcePromise = new TaskCompletionSource<Source<int, string>>();
                var matVal = Source.FromTaskSource(sourcePromise.Task)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(_materializer);

                subscriber.EnsureSubscription();
                sourcePromise.SetResult(Source.FromPublisher(publisher).MapMaterializedValue(_ => "woho"));

                // materialized value completes but still no demand
                matVal.Result.Should().Be("woho");

                // then demand and let an element through to see it works
                subscriber.EnsureSubscription();
                subscriber.Cancel();
                publisher.ExpectCancellation();
            }, _materializer);
        }

        [Fact]
        public void TaskSource_must_fail_when_the_task_source_materialization_fails()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inner = Task.FromResult(Source.FromGraph(new FailingMatGraphStage()));
                var (innerSourceMat, outerSinkMat) = Source.FromTaskSource(inner).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(_materializer);

                // wait until the underlying tasks are completed
                Thread.Sleep(100);

                outerSinkMat.Exception.Flatten().InnerException.Should().Be(new TestException("INNER_FAILED"));
                innerSourceMat.Exception.Flatten().InnerException.Should().Be(new TestException("INNER_FAILED"));
            }, _materializer);
        }

        private class FailingMatGraphStage : GraphStageWithMaterializedValue<SourceShape<int>, string>
        {
            private readonly Outlet<int> _out = new Outlet<int>("whatever");

            public override SourceShape<int> Shape => new SourceShape<int>(_out);

            public override ILogicAndMaterializedValue<string> CreateLogicAndMaterializedValue(Attributes inheritedAttributes) => 
                throw new TestException("INNER_FAILED");
        }
    }
}
