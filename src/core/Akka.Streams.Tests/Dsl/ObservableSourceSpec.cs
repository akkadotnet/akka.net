//-----------------------------------------------------------------------
// <copyright file="ObservableSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(ObservableSourceSpec))]
    public class ObservableSourceSpec : AkkaSpec
    {
        #region internal classes
        
        private sealed class TestObservable<T> : IObservable<T>
        {
            private sealed class TestDisposer : IDisposable
            {
                private readonly TestObservable<T> _observable;

                public TestDisposer(TestObservable<T> observable)
                {
                    _observable = observable;
                }

                public void Dispose()
                {
                    _observable.Observer = null;
                    _observable.Subscribed = false;
                }
            }

            public bool Subscribed { get; private set; }
            public IObserver<T> Observer { get; private set; }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                Observer = observer;
                Subscribed = true;
                return new TestDisposer(this);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Event(T element) => Observer.OnNext(element);

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Error(Exception error) => Observer.OnError(error);

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Complete() => Observer.OnCompleted();
        }

        #endregion

        public const string SpecConfig = @"akka.loglevel = DEBUG";

        private readonly ActorMaterializer _materializer;
        public ObservableSourceSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            _materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void An_ObservableSource_must_subscribe_to_an_observable()
        {
            this.AssertAllStagesStopped(() =>
            {

                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Complete();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_receive_events_from_an_observable()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                sub.Request(2);

                o.Event(1);
                o.Event(2);
                o.Event(3);

                s.ExpectNext(1);
                s.ExpectNext(2);
                s.ExpectNoMsg();

                sub.Request(2);

                s.ExpectNext(3);
                s.ExpectNoMsg();

                o.Event(4);

                s.ExpectNext(4);

                o.Complete();
            }, _materializer);
        }

        [Fact(Skip = "Buggy")]
        public void An_ObservableSource_must_receive_errors_from_an_observable()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();
                sub.Request(2);

                var e = new Exception("hello");

                o.Event(1);
                o.Error(e);
                o.Event(2);

                s.ExpectNext(1);
                s.ExpectError().ShouldBe(e);
                s.ExpectNoMsg();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_receive_completion_from_an_observable()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1);
                o.Complete();

                sub.Request(5);
                
                s.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_be_able_to_unsubscribe()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1);

                sub.Cancel();

                Thread.Sleep(100);

                o.Subscribed.ShouldBeFalse();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_ignore_new_element_on_DropNew_overflow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o, maxBufferCapacity: 2, overflowStrategy: OverflowStrategy.DropNew)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1);
                o.Event(2);
                o.Event(3); // this should be dropped

                sub.Request(3);

                s.ExpectNext(1);
                s.ExpectNext(2);
                s.ExpectNoMsg();

                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_drop_oldest_element_on_DropHead_overflow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o, maxBufferCapacity: 2, overflowStrategy: OverflowStrategy.DropHead)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1); // this should be dropped
                o.Event(2);
                o.Event(3);

                sub.Request(3);

                s.ExpectNext(2);
                s.ExpectNext(3);
                s.ExpectNoMsg();

                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_drop_newest_element_on_DropTail_overflow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o, maxBufferCapacity: 2, overflowStrategy: OverflowStrategy.DropTail)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1);
                o.Event(2); // this should be dropped
                o.Event(3);

                sub.Request(3);

                s.ExpectNext(1);
                s.ExpectNext(3);
                s.ExpectNoMsg();

                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void An_ObservableSource_must_fail_on_Fail_overflow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var o = new TestObservable<int>();
                var s = this.CreateManualSubscriberProbe<int>();
                Source.FromObservable(o, maxBufferCapacity: 2, overflowStrategy: OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);

                var sub = s.ExpectSubscription();

                o.Event(1);
                o.Event(2);
                o.Event(3); // this should cause an error

                sub.Request(3);
                
                s.ExpectError();
                s.ExpectNoMsg();

                sub.Cancel();
            }, _materializer);
        }
    }
}
