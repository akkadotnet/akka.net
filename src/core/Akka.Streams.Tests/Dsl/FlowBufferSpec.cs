//-----------------------------------------------------------------------
// <copyright file="FlowBufferSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowBufferSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowBufferSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Buffer_must_pass_elements_through_normally_in_backpressured_mode()
        {
            var future = Source.From(Enumerable.Range(1, 1000))
                .Buffer(100, OverflowStrategy.Backpressure)
                .Grouped(1001)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1,1000));
        }

        [Fact]
        public void Buffer_must_pass_elements_through_normally_in_backpressured_mode_with_buffer_size_one()
        {
            var future = Source.From(Enumerable.Range(1, 1000))
                .Buffer(1, OverflowStrategy.Backpressure)
                .Grouped(1001)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 1000));
        }

        [Fact]
        public void Buffer_must_pass_elements_through_a_chain_of_backpressured_buffers_of_different_size()
        {
            this.AssertAllStagesStopped(() =>
            {
                var future = Source.From(Enumerable.Range(1, 1000))
                    .Buffer(1, OverflowStrategy.Backpressure)
                    .Buffer(10, OverflowStrategy.Backpressure)
                    .Buffer(256, OverflowStrategy.Backpressure)
                    .Buffer(1, OverflowStrategy.Backpressure)
                    .Buffer(5, OverflowStrategy.Backpressure)
                    .Buffer(128, OverflowStrategy.Backpressure)
                    .Grouped(1001)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                future.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 1000));
            }, Materializer);
        }

        [Fact]
        public void Buffer_must_accept_elements_that_fit_in_the_buffer_while_downstream_is_silent()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Buffer(100, OverflowStrategy.Backpressure)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            // Fill up buffer
            Enumerable.Range(1, 100).ForEach(i => publisher.SendNext(i));

            // drain
            Enumerable.Range(1, 100).ForEach(i =>
            {
                sub.Request(1);
                subscriber.ExpectNext(i);
            });

            sub.Cancel();
        }

        [Fact]
        public void Buffer_must_drop_head_elements_if_buffer_is_full_and_configured_so()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Buffer(100, OverflowStrategy.DropHead)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            // Fill up buffer
            Enumerable.Range(1, 200).ForEach(i => publisher.SendNext(i));

            // The next request would  be otherwise in race with the last onNext in the above loop
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            // drain
            for (var i = 101; i <= 200; i++)
            {
                sub.Request(1);
                subscriber.ExpectNext(i);
            }

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));

            publisher.SendNext(-1);
            sub.Request(1);
            subscriber.ExpectNext(-1);

            sub.Cancel();
        }

        [Fact]
        public void Buffer_must_drop_tail_elements_if_buffer_is_full_and_configured_so()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Buffer(100, OverflowStrategy.DropTail)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            // Fill up buffer
            Enumerable.Range(1, 200).ForEach(i => publisher.SendNext(i));

            // The next request would  be otherwise in race with the last onNext in the above loop
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            // drain
            for (var i = 1; i <= 99; i++)
            {
                sub.Request(1);
                subscriber.ExpectNext(i);
            }

            sub.Request(1);
            subscriber.ExpectNext(200);

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));

            publisher.SendNext(-1);
            sub.Request(1);
            subscriber.ExpectNext(-1);

            sub.Cancel();
        }

        [Fact]
        public void Buffer_must_drop_all_elements_if_buffer_is_full_and_configured_so()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Buffer(100, OverflowStrategy.DropBuffer)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            // Fill up buffer
            Enumerable.Range(1, 150).ForEach(i => publisher.SendNext(i));

            // The next request would  be otherwise in race with the last onNext in the above loop
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            // drain
            for (var i = 101; i <= 150; i++)
            {
                sub.Request(1);
                subscriber.ExpectNext(i);
            }

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));

            publisher.SendNext(-1);
            sub.Request(1);
            subscriber.ExpectNext(-1);

            sub.Cancel();
        }

        [Fact]
        public void Buffer_must_drop_new_elements_if_buffer_is_full_and_configured_so()
        {
            var t =
                this.SourceProbe<int>()
                    .Buffer(100, OverflowStrategy.DropNew)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
            var publisher = t.Item1;
            var subscriber = t.Item2;

            subscriber.EnsureSubscription();

            // Fill up buffer
            Enumerable.Range(1, 150).ForEach(i => publisher.SendNext(i));

            // The next request would  be otherwise in race with the last onNext in the above loop
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            // drain
            for (var i = 1; i <= 100; i++)
                subscriber.RequestNext(i);

            subscriber.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));

            publisher.SendNext(-1);
            subscriber.RequestNext(-1);

            subscriber.Cancel();
        }

        [Fact]
        public void Buffer_must_fail_upstream_if_buffer_is_full_and_configured_so()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateManualSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .Buffer(100, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var sub = subscriber.ExpectSubscription();

                // Fill up buffer
                Enumerable.Range(1, 100).ForEach(i => publisher.SendNext(i));

                // drain
                for (var i = 1; i <= 10; i++)
                {
                    sub.Request(1);
                    subscriber.ExpectNext(i);
                }

                // overflow the buffer
                for (var i = 101; i <= 111; i++)
                    publisher.SendNext(i);

                publisher.ExpectCancellation();

                var actualError = subscriber.ExpectError();
                actualError.Should().BeOfType<BufferOverflowException>();
                actualError.Message.Should().Be("Buffer overflow (max capacity was 100)");
            }, Materializer);
        }

        [Theory]
        [InlineData(OverflowStrategy.DropHead)]
        [InlineData(OverflowStrategy.DropTail)]
        [InlineData(OverflowStrategy.DropBuffer)]
        public void Buffer_must_work_with_strategy_if_bugger_size_of_one(OverflowStrategy strategy)
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Buffer(1, strategy)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            // Fill up buffer
            Enumerable.Range(1, 200).ForEach(i => publisher.SendNext(i));

            // The request below is in race otherwise with the onNext(200) above
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Request(1);
            subscriber.ExpectNext(200);

            publisher.SendNext(-1);
            sub.Request(1);
            subscriber.ExpectNext(-1);

            sub.Cancel();
        }
    }
}
