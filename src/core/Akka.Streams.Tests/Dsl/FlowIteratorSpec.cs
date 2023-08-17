//-----------------------------------------------------------------------
// <copyright file="FlowIteratorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task A_Flow_based_on_an_iterable_must_produce_OnError_when_iterator_throws()
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
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(1);
            await c.ExpectNextAsync(1);
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            EventFilter.Exception<AggregateException>()
                .And.Exception<IllegalStateException>("not two").ExpectOne(() => sub.Request(2));
            var error = c.ExpectError().InnerException;
            error.Message.Should().Be("not two");
            sub.Request(2);
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_OnError_when_Source_construction_throws()
        {
            var p = Source.From(new ThrowEnumerable()).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            c.ExpectSubscriptionAndError().Message.Should().Be("no good iterator");
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_OnError_when_MoveNext_throws()
        {
            var p = Source.From(new ThrowEnumerable(false)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            var error = c.ExpectSubscriptionAndError().InnerException;
            error.Message.Should().Be("no next");
            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
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
        public async Task A_Flow_based_on_an_iterable_must_produce_elements()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();

                sub.Request(1);
                await c.ExpectNextAsync(1);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                sub.Request(3);
                await c.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_complete_empty()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = CreateSource(0).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                await c.ExpectSubscriptionAndCompleteAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_elements_with_multiple_subscribers()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(true), Materializer);
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c1);
                p.Subscribe(c2);
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();
                sub1.Request(1);
                sub2.Request(2);
                await c1.ExpectNextAsync(1);
                await c2.ExpectNextAsync(1);
                await c2.ExpectNextAsync(2);
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await c2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                sub1.Request(2);
                sub2.Request(2);
                await c1.ExpectNextAsync(2);
                await c1.ExpectNextAsync(3);
                await c2.ExpectNextAsync(3);
                await c1.ExpectCompleteAsync();
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_elements_to_later_subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(true), Materializer);
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c1);
                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(1);
                await c1.ExpectNextAsync(1, TimeSpan.FromSeconds(60));
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                p.Subscribe(c2);
                var sub2 = await c2.ExpectSubscriptionAsync();
                sub2.Request(3);
                //element 1 is already gone
                await c2.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectCompleteAsync();

                sub1.Request(3);
                await c1.ExpectNext(2)
                    .ExpectNext(3)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_elements_with_one_transformation_step()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var p = CreateSource(3)                                                                             
                .Select(x => x * 2)                                                                             
                .RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();

                sub.Request(10);
                await c.ExpectNext(2)
                    .ExpectNext(4)
                    .ExpectNext(6)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_produce_elements_with_two_transformation_steps()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var p = CreateSource(4)                                                                             
                .Where(x => x % 2 == 0)                                                                             
                .Select(x => x * 2)                                                                             
                .RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();

                sub.Request(10);
                await c.ExpectNext(4)
                    .ExpectNext(8)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_an_iterable_must_not_produce_after_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = CreateSource(3).RunWith(Sink.AsPublisher<int>(false), Materializer);
                var c = this.CreateManualSubscriberProbe<int>();

                p.Subscribe(c);
                var sub = await c.ExpectSubscriptionAsync();

                sub.Request(1);
                await c.ExpectNextAsync(1);
                sub.Cancel();
                sub.Request(2);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }
    }
}
