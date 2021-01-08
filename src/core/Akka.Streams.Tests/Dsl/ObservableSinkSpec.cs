//-----------------------------------------------------------------------
// <copyright file="ObservableSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(ObservableSinkSpec))]
    public class ObservableSinkSpec : AkkaSpec
    {
        #region internal classes

        private sealed class TestObserver<T> : IObserver<T>
        {
            private readonly TestProbe _probe;

            public TestObserver(AkkaSpec spec)
            {
                _probe = spec.CreateTestProbe();
            }

            public void OnNext(T value) => _probe.Ref.Tell(new OnNext(value), ActorRefs.NoSender);
            public void OnError(Exception error) => _probe.Ref.Tell(new OnError(error), ActorRefs.NoSender);
            public void OnCompleted() => _probe.Ref.Tell(OnComplete.Instance, ActorRefs.NoSender);

            public T ExpectEvent(T expected) => (T)_probe.ExpectMsg<OnNext>(x => Equals(x.Element, expected)).Element;
            public TError ExpectError<TError>(TError error) where TError : Exception => (TError)_probe.ExpectMsg<OnError>(x => Equals(x.Cause, error)).Cause;
            public void ExpectCompleted() => _probe.ExpectMsg<OnComplete>();
            public void ExpectNoMsg() => _probe.ExpectNoMsg();
        }

        #endregion

        public const string SpecConfig = @"akka.loglevel = DEBUG";
        private ActorMaterializer Materializer { get; }

        public ObservableSinkSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void An_ObservableSink_must_allow_the_same_observer_to_be_subscribed_only_once()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = new TestObserver<int>(this);
                var observable = Source.From(new[] { 1, 2, 3 })
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe);
                var d2 = observable.Subscribe(probe);

                d1.ShouldBe(d2);

                probe.ExpectEvent(1);
                probe.ExpectEvent(2);
                probe.ExpectEvent(3);
                probe.ExpectCompleted();
                probe.ExpectNoMsg();

            }, Materializer);
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void An_ObservableSink_must_propagate_events_to_all_observers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe1 = new TestObserver<int>(this);
                var probe2 = new TestObserver<int>(this);
                var observable = Source.From(new[] { 1, 2 })
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe1);
                var d2 = observable.Subscribe(probe2);

                probe1.ExpectEvent(1);
                probe1.ExpectEvent(2);
                probe1.ExpectCompleted();
                probe1.ExpectNoMsg();

                probe2.ExpectEvent(1);
                probe2.ExpectEvent(2);
                probe2.ExpectCompleted();
                probe2.ExpectNoMsg();

            }, Materializer);
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void An_ObservableSink_must_propagate_error_to_all_observers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var e = new Exception("boom");
                var probe1 = new TestObserver<int>(this);
                var probe2 = new TestObserver<int>(this);
                var observable = Source.Failed<int>(e)
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe1);
                var d2 = observable.Subscribe(probe2);

                probe1.ExpectError(e);
                probe1.ExpectNoMsg();

                probe2.ExpectError(e);
                probe2.ExpectNoMsg();

            }, Materializer);
        }
        
        [Fact]
        public void An_ObservableSink_subscriber_must_be_disposable()
        {
                var probe = new TestObserver<int>(this);
                var tuple = Source.Queue<int>(1, OverflowStrategy.DropHead)
                    .ToMaterialized(Sink.AsObservable<int>(), Keep.Both)
                    .Run(Materializer);
                var queue = tuple.Item1;
                var observable = tuple.Item2;

                var d1 = observable.Subscribe(probe);

                var t = queue.OfferAsync(1);
                t.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();
                t.Result.ShouldBe(QueueOfferResult.Enqueued.Instance);

                probe.ExpectEvent(1);

                t = queue.OfferAsync(2);
                t.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();
                t.Result.ShouldBe(QueueOfferResult.Enqueued.Instance);

                probe.ExpectEvent(2);

                d1.Dispose();

                t = queue.OfferAsync(3);
                t.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();

                probe.ExpectCompleted();
                probe.ExpectNoMsg();
        }
    }
}
