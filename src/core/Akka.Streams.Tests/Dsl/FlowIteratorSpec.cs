//-----------------------------------------------------------------------
// <copyright file="FlowIteratorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowIteratorSpec : AbstractFlowIteratorSpec
    {
        public FlowIteratorSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Source<int, NotUsed> CreateSource(int elements)
            => Source.FromEnumerator(() => Enumerable.Range(1, elements).GetEnumerator());
    }

    public class FlowIterableSpec : AbstractFlowIteratorSpec
    {
        public FlowIterableSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Source<int, NotUsed> CreateSource(int elements)
            => Source.From(Enumerable.Range(1, elements));

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_OnError_when_iterator_throws()
        {
            var iterable = Enumerable.Range(1, 3).Select(x =>
            {
                if (x == 2)
                    throw new IllegalStateException("not two");
                return x;
            });
            var p = Source.From(iterable).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            sub.Request(1);
            c.ExpectNext(1);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            EventFilter.Exception<AggregateException>()
                .And.Exception<IllegalStateException>("not two").ExpectOne(() => sub.Request(2));
            var error = c.ExpectError().InnerException;
            error.Message.Should().Be("not two");
            sub.Request(2);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_OnError_when_Source_construction_throws()
        {
            var p = Source.From(new ThrowEnumerable()).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            c.ExpectSubscriptionAndError().Message.Should().Be("no good iterator");
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_OnError_when_MoveNext_throws()
        {
            var p = Source.From(new ThrowEnumerable(false)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            var error = c.ExpectSubscriptionAndError().InnerException;
            error.Message.Should().Be("no next");
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        private sealed class ThrowEnumerable : IEnumerable<int>
        {
            private readonly bool _throwOnGetEnumerator;

            public ThrowEnumerable(bool throwOnGetEnumerator = true)
            {
                _throwOnGetEnumerator = throwOnGetEnumerator;
            }

            public IEnumerator<int> GetEnumerator()
            {
                if(_throwOnGetEnumerator)
                 throw new IllegalStateException("no good iterator");
                
                return new ThrowEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }


        private sealed class ThrowEnumerator : IEnumerator<int>
        {
            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public bool MoveNext()
            {
                throw new IllegalStateException("no next");
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }

            public int Current { get; } = -1;

            object IEnumerator.Current => Current;
        }
    }

    public abstract class AbstractFlowIteratorSpec : AkkaSpec
    {
        protected ActorMaterializer Materializer { get; }

        protected AbstractFlowIteratorSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
            
        }

        protected abstract Source<int, NotUsed> CreateSource(int elements);

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_elements()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();
                
                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                sub.Request(1);
                c.ExpectNext(1);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                sub.Request(3);
                c.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_complete_empty()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(0).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                c.ExpectSubscriptionAndComplete();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_elements_with_multiple_subscribers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(true), Materializer);
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                
                p.Subscribe(c1);
                p.Subscribe(c2);
                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();
                sub1.Request(1);
                sub2.Request(2);
                c1.ExpectNext(1);
                c2.ExpectNext(1);
                c2.ExpectNext(2);
                c1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                c2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                sub1.Request(2);
                sub2.Request(2);
                c1.ExpectNext(2);
                c1.ExpectNext(3);
                c2.ExpectNext(3);
                c1.ExpectComplete();
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_elements_to_later_subscriber()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(true), Materializer);
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                
                p.Subscribe(c1);
                var sub1 = c1.ExpectSubscription();
                sub1.Request(1);
                c1.ExpectNext(1, TimeSpan.FromSeconds(60));
                c1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                p.Subscribe(c2);
                var sub2 = c2.ExpectSubscription();
                sub2.Request(3);
                //element 1 is already gone
                c2.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectComplete();

                sub1.Request(3);
                c1.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_elements_with_one_transformation_step()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(3)
                    .Select(x => x*2)
                    .RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                sub.Request(10);
                c.ExpectNext(2)
                    .ExpectNext(4)
                    .ExpectNext(6)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_produce_elements_with_two_transformation_steps()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(4)
                    .Where(x => x%2 == 0)
                    .Select(x => x*2)
                    .RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                sub.Request(10);
                c.ExpectNext(4)
                    .ExpectNext(8)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_an_iterable_must_not_produce_after_cancel()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = c.ExpectSubscription();

                sub.Request(1);
                c.ExpectNext(1);
                sub.Cancel();
                sub.Request(2);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }
    }
}
