//-----------------------------------------------------------------------
// <copyright file="FlowBatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowBatchSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowBatchSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Batch_must_pass_through_elements_unchanged_when_there_is_no_rate_difference()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Batch(max: 2, seed: i => i, aggregate: (sum, i) => sum + i)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 100; i++)
            {
                sub.Request(1);
                publisher.SendNext(i);
                subscriber.ExpectNext(i);
            }

            sub.Cancel();
        }

        [Fact]
        public void Batch_must_aggregate_elements_while_downstream_is_silent()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateSubscriberProbe<List<int>>();

            Source.FromPublisher(publisher).Batch(long.MaxValue, i => new List<int> {i}, (ints, i) =>
            {
                ints.Add(i);
                return ints;
            }).To(Sink.FromSubscriber(subscriber)).Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 10; i++)
                publisher.SendNext(i);

            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));
            sub.Request(1);
            subscriber.ExpectNext().ShouldAllBeEquivalentTo(new [] {1, 2, 3, 4, 5, 6, 7, 8, 9, 10});
            sub.Cancel();
        }

        [Fact(Skip = "Racy")]
        public async Task Batch_must_work_on_a_variable_rate_chain()
        {
            var result = Source.From(Enumerable.Range(1, 1000)).Batch(100, i => i, (sum, i) => sum + i).Select(i =>
            {
                if (ThreadLocalRandom.Current.Next(1, 3) == 1)
                    Thread.Sleep(10);
                return i;
            }).RunAggregate(0, (i, i1) => i + i1, Materializer);
            result.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            result.Should().Be(500500);
        }

        [Fact]
        public void Batch_must_backpressure_subscriber_when_upstream_is_slower()
        {
            var publisher = this.CreatePublisherProbe<int>();
            var subscriber = this.CreateSubscriberProbe<int>();

            Source.FromPublisher(publisher)
                .Batch(2, i => i, (sum, i) => sum + i)
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);
            var sub = subscriber.EnsureSubscription();

            sub.Request(1);
            publisher.SendNext(1);
            subscriber.ExpectNext(1);

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            publisher.SendNext(2);
            subscriber.ExpectNext(2);

            publisher.SendNext(3);
            publisher.SendNext(4);
            // The request can be in race with the above onNext(4) so the result would be either 3 or 7.
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Request(1);
            subscriber.ExpectNext(7);

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Cancel();
        }

        [Fact]
        public void Batch_must_work_with_a_buffer_and_aggregate()
        {
            var future =
                Source.From(Enumerable.Range(1, 50))
                    .Batch(long.MaxValue, i => i, (sum, i) => sum + i)
                    .Buffer(50, OverflowStrategy.Backpressure)
                    .RunAggregate(0, (sum, i) => sum + i, Materializer);
            future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            future.Result.Should().Be(Enumerable.Range(1, 50).Sum());
        }
    }
}
