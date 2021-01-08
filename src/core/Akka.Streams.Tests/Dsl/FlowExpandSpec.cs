//-----------------------------------------------------------------------
// <copyright file="FlowExpandSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    public class FlowExpandSpec : AkkaSpec
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowExpandSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public void Expand_must_pass_through_elements_unchanged_when_there_is_no_rate_difference()
        {
            // Shadow the fuzzed materializer (see the ordering guarantee needed by the for loop below).
            var materializer = ActorMaterializer.Create(Sys, Settings.WithFuzzingMode(false));

            var subscriber = this.CreateSubscriberProbe<int>();
            var publisher = this.CreatePublisherProbe<int>();

            // Simply repeat the last element as an extrapolation step
            Source.FromPublisher(publisher)
                .Expand(i => Enumerable.Repeat(i, 200).GetEnumerator())
                .To(Sink.FromSubscriber(subscriber))
                .Run(materializer);

            for (var i = 1; i <= 100; i++)
            {
                // Order is important here: If the request comes first it will be extrapolated!
                publisher.SendNext(i);
                subscriber.RequestNext(i);
            }

            subscriber.Cancel();
        }

        [Fact]
        public void Expand_must_expand_elements_while_upstream_is_silent()
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            var publisher = this.CreatePublisherProbe<int>();

            // Simply repeat the last element as an extrapolation step
            Source.FromPublisher(publisher)
                .Expand(i => Enumerable.Repeat(i, 200).GetEnumerator())
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            publisher.SendNext(42);

            for (var i = 1; i <= 100; i++)
                subscriber.RequestNext(42);
            
            publisher.SendNext(-42);

            // The request below is otherwise in race with the above sendNext
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            subscriber.RequestNext(-42);

            subscriber.Cancel();
        }

        [Fact]
        public void Expand_must_do_not_drop_last_element()
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            var publisher = this.CreatePublisherProbe<int>();

            // Simply repeat the last element as an extrapolation step
            Source.FromPublisher(publisher)
                .Expand(i => Enumerable.Repeat(i, 200).GetEnumerator())
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            publisher.SendNext(1);
            subscriber.RequestNext(1);

            publisher.SendNext(2);
            publisher.SendComplete();

            // The request below is otherwise in race with the above sendNext(2) (and completion)
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            subscriber.RequestNext(2);
            subscriber.ExpectComplete();
        }

        [Fact]
        public void Expand_must_work_on_a_variable_rate_chain()
        {
            var future = Source.From(Enumerable.Range(1, 100))
                .Select(x =>
                {
                    if (ThreadLocalRandom.Current.Next(1, 3) == 2)
                        Thread.Sleep(10);
                    return x;
                })
                .Expand(i => Enumerable.Repeat(i, 200).GetEnumerator())
                .RunAggregate(new HashSet<int>(), (agg, elem) =>
                {
                    agg.Add(elem);
                    return agg;
                }, Materializer);

            future.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
        }

        [Fact]
        public void Expand_must_backpressure_publisher_when_subscriber_is_slower()
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            var publisher = this.CreatePublisherProbe<int>();

            // Simply repeat the last element as an extrapolation step
            Source.FromPublisher(publisher)
                .Expand(i => Enumerable.Repeat(i, 200).GetEnumerator())
                .To(Sink.FromSubscriber(subscriber))
                .Run(Materializer);

            publisher.SendNext(1);
            subscriber.RequestNext(1);
            subscriber.RequestNext(1);

            var pending = publisher.Pending;
            // Deplete pending requests coming from input buffer
            while (pending > 0)
            {
                publisher.UnsafeSendNext(2);
                pending--;
            }

            // The above sends are absorbed in the input buffer, and will result in two one-sized batch requests
            pending += publisher.ExpectRequest();
            pending += publisher.ExpectRequest();
            while (pending > 0)
            {
                publisher.UnsafeSendNext(2);
                pending--;
            }

            publisher.ExpectNoMsg(TimeSpan.FromSeconds(1));

            subscriber.Request(2);
            subscriber.ExpectNext(2);
            subscriber.ExpectNext(2);

            //now production is resumed
            publisher.ExpectRequest();
        }

        [Fact]
        public void Expand_must_work_properly_with_finite_extrapolations()
        {
            var t = TestSource.SourceProbe<int>(this)
                .Expand(i => Enumerable.Range(0, 4).Select(x => (i, x)).Take(3).GetEnumerator())
                .ToMaterialized(this.SinkProbe<(int, int)>(), Keep.Both)
                .Run(Materializer);
            var source = t.Item1;
            var sink = t.Item2;

            source.SendNext(1);

            sink.Request(4)
                .ExpectNext((1, 0), (1, 1), (1, 2))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            source.SendNext(2).SendComplete();

            sink.ExpectNext((2, 0)).ExpectComplete();
        }
    }
}
