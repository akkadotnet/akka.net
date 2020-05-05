//-----------------------------------------------------------------------
// <copyright file="PublisherSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class PublisherSinkSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public PublisherSinkSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_PublisherSink_must_be_unique_when_created_twice()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    RunnableGraph.FromGraph(
                        GraphDsl.Create(Sink.AsPublisher<int>(false),
                            Sink.AsPublisher<int>(false), Keep.Both,
                            (b, p1, p2) =>
                            {
                                var broadcast = b.Add(new Broadcast<int>(2));
                                var source =
                                    Source.From(Enumerable.Range(0, 6))
                                        .MapMaterializedValue(_ => default((IPublisher<int>, IPublisher<int>)));
                                b.From(source).To(broadcast.In);
                                b.From(broadcast.Out(0)).Via(Flow.Create<int>().Select(i => i * 2)).To(p1.Inlet);
                                b.From(broadcast.Out(1)).To(p2.Inlet);
                                return ClosedShape.Instance;
                            })).Run(Materializer);

                var pub1 = t.Item1;
                var pub2 = t.Item2;

                var f1 = Source.FromPublisher(pub1).Select(x => x).RunAggregate(0, (sum, i) => sum + i, Materializer);
                var f2 = Source.FromPublisher(pub2).Select(x => x).RunAggregate(0, (sum, i) => sum + i, Materializer);

                f1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                f2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                f1.Result.Should().Be(30);
                f2.Result.Should().Be(15);
            }, Materializer);
        }

        [Fact]
        public void A_PublisherSink_must_work_with_SubscriberSource()
        {
            var t = Source.AsSubscriber<int>().ToMaterialized(Sink.AsPublisher<int>(false), Keep.Both).Run(Materializer);
            var sub = t.Item1;
            var pub = t.Item2;

            Source.From(Enumerable.Range(1, 100)).To(Sink.FromSubscriber(sub)).Run(Materializer);

            var task = Source.FromPublisher(pub).Limit(1000).RunWith(Sink.Seq<int>(), Materializer);
            task.Wait(TimeSpan.FromSeconds(3));
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));

        }

        [Fact]
        public void A_PublisherSink_must_be_able_to_use_Publisher_in_materialized_value_transformation()
        {
            var f = Source.From(Enumerable.Range(1, 3))
                .RunWith(
                    Sink.AsPublisher<int>(false)
                        .MapMaterializedValue(
                            p => Source.FromPublisher(p).RunAggregate(0, (sum, i) => sum + i, Materializer)),
                    Materializer);
            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f.Result.Should().Be(6);
        }
    }
}
