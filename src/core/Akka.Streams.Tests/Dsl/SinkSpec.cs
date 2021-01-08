//-----------------------------------------------------------------------
// <copyright file="SinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SinkSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SinkSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private TestSubscriber.ManualProbe<int>[] CreateProbes() => new[]
        {
            this.CreateManualSubscriberProbe<int>(),
            this.CreateManualSubscriberProbe<int>(),
            this.CreateManualSubscriberProbe<int>()
        };

        [Fact]
        public void A_Sink_must_be_composable_without_importing_modules()
        {
            var probes = CreateProbes();
            var sink = Sink.FromGraph(GraphDsl.Create(b =>
            {
                var broadcast = b.Add(new Broadcast<int>(3));
                for (var i = 0; i < 3; i++)
                {
                    var closure = i;
                    b.From(broadcast.Out(i))
                        .Via(Flow.Create<int>().Where(x => x == closure))
                        .To(Sink.FromSubscriber(probes[i]));
                }
                return new SinkShape<int>(broadcast.In);
            }));
            Source.From(new[] {0, 1, 2}).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s=>s.Request(3));
            for (var i = 0; i < probes.Length; i++)
                probes[i].ExpectNext(i);
            probes.ForEach(p => p.ExpectComplete());
        }

        [Fact]
        public void A_Sink_must_be_composable_with_importing_1_modules()
        {
            var probes = CreateProbes();
            var sink = Sink.FromGraph(GraphDsl.Create(Sink.FromSubscriber(probes[0]), (b, shape) =>
            {
                var broadcast = b.Add(new Broadcast<int>(3));
                b.From(broadcast.Out(0)).Via(Flow.Create<int>().Where(x => x == 0)).To(shape.Inlet);
                b.From(broadcast.Out(1))
                    .Via(Flow.Create<int>().Where(x => x == 1))
                    .To(Sink.FromSubscriber(probes[1]));
                b.From(broadcast.Out(2))
                    .Via(Flow.Create<int>().Where(x => x == 2))
                    .To(Sink.FromSubscriber(probes[2]));
                return new SinkShape<int>(broadcast.In);
            }));
            Source.From(new[] { 0, 1, 2 }).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s => s.Request(3));
            for (var i = 0; i < probes.Length; i++)
                probes[i].ExpectNext(i);
            probes.ForEach(p => p.ExpectComplete());
        }

        [Fact]
        public void A_Sink_must_be_composable_with_importing_2_modules()
        {
            var probes = CreateProbes();
            var sink =
                Sink.FromGraph(GraphDsl.Create(Sink.FromSubscriber(probes[0]),
                    Sink.FromSubscriber(probes[1]), (_, __) => NotUsed.Instance, (b, shape0, shape1) =>
                    {
                        var broadcast = b.Add(new Broadcast<int>(3));
                        b.From(broadcast.Out(0)).Via(Flow.Create<int>().Where(x => x == 0)).To(shape0.Inlet);
                        b.From(broadcast.Out(1)).Via(Flow.Create<int>().Where(x => x == 1)).To(shape1.Inlet);
                        b.From(broadcast.Out(2))
                            .Via(Flow.Create<int>().Where(x => x == 2))
                            .To(Sink.FromSubscriber(probes[2]));
                        return new SinkShape<int>(broadcast.In);
                    }));
            Source.From(new[] { 0, 1, 2 }).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s => s.Request(3));
            for (var i = 0; i < probes.Length; i++)
                probes[i].ExpectNext(i);
            probes.ForEach(p => p.ExpectComplete());
        }

        [Fact]
        public void A_Sink_must_be_composable_with_importing_3_modules()
        {
            var probes = CreateProbes();
            var sink =
                Sink.FromGraph(GraphDsl.Create(Sink.FromSubscriber(probes[0]),
                    Sink.FromSubscriber(probes[1]), Sink.FromSubscriber(probes[2]),
                    (_, __, ___) => NotUsed.Instance, (b, shape0, shape1, shape2) =>
                    {
                        var broadcast = b.Add(new Broadcast<int>(3));
                        b.From(broadcast.Out(0)).Via(Flow.Create<int>().Where(x => x == 0)).To(shape0.Inlet);
                        b.From(broadcast.Out(1)).Via(Flow.Create<int>().Where(x => x == 1)).To(shape1.Inlet);
                        b.From(broadcast.Out(2)).Via(Flow.Create<int>().Where(x => x == 2)).To(shape2.Inlet);
                        return new SinkShape<int>(broadcast.In);
                    }));
            Source.From(new[] { 0, 1, 2 }).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s => s.Request(3));
            for (var i = 0; i < probes.Length; i++)
                probes[i].ExpectNext(i);
            probes.ForEach(p => p.ExpectComplete());
        }

        [Fact]
        public void A_Sink_must_combine_to_many_outputs_with_simplified_API()
        {
            var probes = CreateProbes();
            var sink = Sink.Combine(i => new Broadcast<int>(i), Sink.FromSubscriber(probes[0]),
                Sink.FromSubscriber(probes[1]), Sink.FromSubscriber(probes[2]));

            Source.From(new[] { 0, 1, 2 }).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s => s.Request(1));
            probes.ForEach(p => p.ExpectNext(0));
            subscriptions.ForEach(s=>s.Request(2));
            probes.ForEach(p =>
            {
                p.ExpectNext(1, 2);
                p.ExpectComplete();
            });
        }

        [Fact]
        public void A_Sink_must_combine_to_two_sinks_with_simplified_API()
        {
            var probes = CreateProbes().Take(2).ToArray();
            var sink = Sink.Combine(i => new Broadcast<int>(i), Sink.FromSubscriber(probes[0]),
                Sink.FromSubscriber(probes[1]));

            Source.From(new[] { 0, 1, 2 }).RunWith(sink, Materializer);

            var subscriptions = probes.Select(p => p.ExpectSubscription()).ToList();
            subscriptions.ForEach(s => s.Request(1));
            probes.ForEach(p => p.ExpectNext(0));
            subscriptions.ForEach(s => s.Request(2));
            probes.ForEach(p =>
            {
                p.ExpectNext(1, 2);
                p.ExpectComplete();
            });
        }

        [Fact]
        public void A_Sink_must_suitably_override_attribute_handling_methods()
        {
            var s = Sink.First<int>().Async().AddAttributes(Attributes.None).Named("name");

            s.Module.Attributes.GetAttribute<Attributes.Name>().Value.Should().Be("name");
            s.Module.Attributes.GetFirstAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
        }

        [Fact]
        public void A_Sink_must_support_contramap()
        {
            Source.From(Enumerable.Range(0, 9))
                .ToMaterialized(Sink.Seq<int>().ContraMap<int>(i => i + 1), Keep.Right)
                .Run(Materializer)
                .Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 9));
        }

        [Fact]
        public void Sink_prematerialization_must_materialize_the_sink_and_wrap_its_exposed_publisher_in_a_Source()
        {
            var publisherSink = Sink.AsPublisher<string>(false);
            var tup = publisherSink.PreMaterialize(Sys.Materializer());
            var matPub = tup.Item1;
            var sink = tup.Item2;

            var probe = Source.FromPublisher(matPub).RunWith(this.SinkProbe<string>(), Sys.Materializer());
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            Source.Single("hello").RunWith(sink, Sys.Materializer());

            probe.EnsureSubscription();
            probe.RequestNext("hello");
            probe.ExpectComplete();
        }

        [Fact]
        public void Sink_prematerialization_must_materialize_the_sink_and_wrap_its_exposed_fanout_publisher_in_a_Source_twice()
        {
            var publisherSink = Sink.AsPublisher<string>(true);
            var tup = publisherSink.PreMaterialize(Sys.Materializer());
            var matPub = tup.Item1;
            var sink = tup.Item2;

            var probe1 = Source.FromPublisher(matPub).RunWith(this.SinkProbe<string>(), Sys.Materializer());
            var probe2 = Source.FromPublisher(matPub).RunWith(this.SinkProbe<string>(), Sys.Materializer());

            Source.Single("hello").RunWith(sink, Sys.Materializer());

            probe1.EnsureSubscription();
            probe1.RequestNext("hello");
            probe1.ExpectComplete();

            probe2.EnsureSubscription();
            probe2.RequestNext("hello");
            probe2.ExpectComplete();
        }

        [Fact]
        public void
            Sink_prematerialization_must_materialize_the_sink_and_wrap_its_exposed_nonfanout_publisher_and_fail_the_second_materialization()
        {
            var publisherSink = Sink.AsPublisher<string>(false);
            var tup = publisherSink.PreMaterialize(Sys.Materializer());
            var matPub = tup.Item1;
            var sink = tup.Item2;

            var probe1 = Source.FromPublisher(matPub).RunWith(this.SinkProbe<string>(), Sys.Materializer());
            var probe2 = Source.FromPublisher(matPub).RunWith(this.SinkProbe<string>(), Sys.Materializer());

            Source.Single("hello").RunWith(sink, Sys.Materializer());

            probe1.EnsureSubscription();
            probe1.RequestNext("hello");
            probe1.ExpectComplete();

            probe2.EnsureSubscription();
            probe2.ExpectError().Message.Should().Contain("only supports one subscriber");
        }
    }
}


