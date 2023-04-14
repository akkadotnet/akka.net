//-----------------------------------------------------------------------
// <copyright file="ObservableSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
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

            public async Task<T> ExpectEventAsync(T expected) => (T)(await _probe.ExpectMsgAsync<OnNext>(x => Equals(x.Element, expected))).Element;
            public async Task<TError> ExpectErrorAsync<TError>(TError error) where TError : Exception => (TError)(await _probe.ExpectMsgAsync<OnError>(x => Equals(x.Cause, error))).Cause;
            public async Task ExpectCompletedAsync() => await _probe.ExpectMsgAsync<OnComplete>();
            public async Task ExpectNoMsgAsync() => await _probe.ExpectNoMsgAsync();
        }

        #endregion

        public const string SpecConfig = @"akka.loglevel = DEBUG";
        private ActorMaterializer Materializer { get; }

        public ObservableSinkSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task An_ObservableSink_must_allow_the_same_observer_to_be_subscribed_only_once()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = new TestObserver<int>(this);
                var observable = Source.From(new[] { 1, 2, 3 })
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe);
                var d2 = observable.Subscribe(probe);

                d1.ShouldBe(d2);

                await probe.ExpectEventAsync(1);
                await probe.ExpectEventAsync(2);
                await probe.ExpectEventAsync(3);
                await probe.ExpectCompletedAsync();
                await probe.ExpectNoMsgAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task An_ObservableSink_must_propagate_events_to_all_observers()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe1 = new TestObserver<int>(this);
                var probe2 = new TestObserver<int>(this);
                var observable = Source.From(new[] { 1, 2 })
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe1);
                var d2 = observable.Subscribe(probe2);

                await probe1.ExpectEventAsync(1);
                await probe1.ExpectEventAsync(2);
                await probe1.ExpectCompletedAsync();
                await probe1.ExpectNoMsgAsync();

                await probe2.ExpectEventAsync(1);
                await probe2.ExpectEventAsync(2);
                await probe2.ExpectCompletedAsync();
                await probe2.ExpectNoMsgAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task An_ObservableSink_must_propagate_error_to_all_observers()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var e = new Exception("boom");
                var probe1 = new TestObserver<int>(this);
                var probe2 = new TestObserver<int>(this);
                var observable = Source.Failed<int>(e)
                    .RunWith(Sink.AsObservable<int>(), Materializer);

                var d1 = observable.Subscribe(probe1);
                var d2 = observable.Subscribe(probe2);

                await probe1.ExpectErrorAsync(e);
                await probe1.ExpectNoMsgAsync();

                await probe2.ExpectErrorAsync(e);
                await probe2.ExpectNoMsgAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task An_ObservableSink_subscriber_must_be_disposable()
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

                await probe.ExpectEventAsync(1);

                t = queue.OfferAsync(2);
                t.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();
                t.Result.ShouldBe(QueueOfferResult.Enqueued.Instance);

                await probe.ExpectEventAsync(2);

                d1.Dispose();

                t = queue.OfferAsync(3);
                t.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();

                await probe.ExpectCompletedAsync();
                await probe.ExpectNoMsgAsync();
        }
    }
}
