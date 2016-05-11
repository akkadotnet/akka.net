//-----------------------------------------------------------------------
// <copyright file="SinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
            this.CreateManualProbe<int>(),
            this.CreateManualProbe<int>(),
            this.CreateManualProbe<int>()
        };

        [Fact]
        public void A_Sink_must_be_composable_without_importing_modules()
        {
            var probes = CreateProbes();
            var sink = Sink.FromGraph(GraphDsl.Create<SinkShape<int>, NotUsed>(b =>
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
            var s = Sink.First<int>().WithAttributes(new Attributes()).Named("");
        }

        [Fact]
        public void A_Sink_must_support_contramap()
        {
            Source.From(Enumerable.Range(0, 9))
                .ToMaterialized(Sink.Seq<int>().ContraMap<int>(i => i + 1), Keep.Right)
                .Run(Materializer)
                .Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 9));
        }
    }
}


