//-----------------------------------------------------------------------
// <copyright file="FlowJoinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public class FlowJoinSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowJoinSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_cycles()
        {
            this.AssertAllStagesStopped(() =>
            {
                const int end = 47;
                var t = Enumerable.Range(0, end + 1).GroupBy(i => i%2 == 0).ToList();
                var even = t.First(x => x.Key).ToList();
                var odd = t.First(x => !x.Key).ToList();
                var source = Source.From(Enumerable.Range(0, end + 1));
                var result = even.Concat(odd).Concat(odd.Select(x => x*10));
                var probe = this.CreateManualSubscriberProbe<IEnumerable<int>>();

                var flow1 = Flow.FromGraph(GraphDsl.Create(b =>
                {
                    var merge = b.Add(new Merge<int>(2));
                    var broadcast = b.Add(new Broadcast<int>(2));
                    b.From(source).To(merge.In(0));
                    b.From(merge.Out).To(broadcast.In);
                    b.From(broadcast.Out(0))
                        .Via(Flow.Create<int>().Grouped(1000))
                        .To(Sink.FromSubscriber(probe));
                    return new FlowShape<int, int>(merge.In(1), broadcast.Out(1));
                }));

                var flow2 =
                    Flow.Create<int>()
                        .Where(x => x%2 == 1)
                        .Select(x => x*10)
                        .Buffer((end + 1)/2, OverflowStrategy.Backpressure)
                        .Take((end + 1)/2);

                flow1.Join(flow2).Run(Materializer);

                var sub = probe.ExpectSubscription();
                sub.Request(1);
                probe.ExpectNext().ShouldAllBeEquivalentTo(result);
                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_merge_cycle()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source =
                    Source.Single("lonely traveler").MapMaterializedValue(_ => Task.FromResult(""));

                var flow1 = Flow.FromGraph(GraphDsl.Create(Sink.First<string>(), (b, sink) =>
                {
                    var merge = b.Add(new Merge<string>(2));
                    var broadcast = b.Add(new Broadcast<string>(2, true));

                    b.From(source).To(merge.In(0));
                    b.From(merge.Out).To(broadcast.In);
                    b.From(broadcast.Out(0)).To(sink);
                    return new FlowShape<string, string>(merge.In(1), broadcast.Out(1));
                }));

                var t = flow1.Join(Flow.Create<string>()).Run(Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be("lonely traveler");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_merge_preferred_cycle()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source =
                    Source.Single("lonely traveler").MapMaterializedValue(_ => Task.FromResult(""));

                var flow1 = Flow.FromGraph(GraphDsl.Create(Sink.First<string>(), (b, sink) =>
                {
                    var merge = b.Add(new MergePreferred<string>(1));
                    var broadcast = b.Add(new Broadcast<string>(2, true));

                    b.From(source).To(merge.Preferred);
                    b.From(merge.Out).To(broadcast.In);
                    b.From(broadcast.Out(0)).To(sink);
                    return new FlowShape<string, string>(merge.In(0), broadcast.Out(1));
                }));

                var t = flow1.Join(Flow.Create<string>()).Run(Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be("lonely traveler");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_zip_cycle()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(new[] {"traveler1", "traveler2"})
                    .MapMaterializedValue<TestSubscriber.Probe<(string, string)>>(_ => null);

                var flow = Flow.FromGraph(GraphDsl.Create(this.SinkProbe<(string,string)>(), (b, sink) =>
                {
                    var zip = b.Add(new Zip<string, string>());
                    var broadcast = b.Add(new Broadcast<(string, string)>(2));

                    b.From(source).To(zip.In0);
                    b.From(zip.Out).To(broadcast.In);
                    b.From(broadcast.Out(0)).To(sink);
                    return new FlowShape<string, (string, string)>(zip.In1, broadcast.Out(1));
                }));

                var feedback = Flow.FromGraph(GraphDsl.Create(Source.Single("ignition"), (b, ignition) =>
                {
                    var f = b.Add(Flow.Create<(string, string)>().Select(t => t.Item1));
                    var merge = b.Add(new Merge<string>(2));

                    b.From(ignition).To(merge.In(0));
                    b.From(f).To(merge.In(1));

                    return new FlowShape<(string, string), string>(f.Inlet, merge.Out);
                }));

                var probe = flow.Join(feedback).Run(Materializer);
                probe.RequestNext(("traveler1", "ignition"));
                probe.RequestNext(("traveler2", "traveler1"));
            }, Materializer);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_concat_cycle()
        {
            this.AssertAllStagesStopped(() =>
            {
                var flow = Flow.FromGraph(GraphDsl.Create(TestSource.SourceProbe<string>(this), Sink.First<string>(), Keep.Both, (b, source, sink) =>
                {
                    var concat = b.Add(Concat.Create<string>());
                    var broadcast = b.Add(new Broadcast<string>(2, true));

                    b.From(source).To(concat.In(0));
                    b.From(concat.Out).To(broadcast.In);
                    b.From(broadcast.Out(0)).To(sink);
                    return new FlowShape<string, string>(concat.In(1), broadcast.Out(1));
                }));

                var tuple = flow.Join(Flow.Create<string>()).Run(Materializer);
                var probe = tuple.Item1;
                var t = tuple.Item2;
                probe.SendNext("lonely traveler");
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be("lonely traveler");
                probe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_using_Join_must_allow_for_interleave_cycle()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.Single("lonely traveler").MapMaterializedValue(_ => Task.FromResult(""));
                var flow = Flow.FromGraph(GraphDsl.Create(Sink.First<string>(), (b, sink) =>
                {
                    var interleave = b.Add(Interleave.Create<string>(2, 1));
                    var broadcast = b.Add(new Broadcast<string>(2, true));

                    b.From(source).To(interleave.In(0));
                    b.From(interleave.Out).To(broadcast.In);
                    b.From(broadcast.Out(0)).To(sink);
                    return new FlowShape<string, string>(interleave.In(1), broadcast.Out(1));
                }));
                
                var t = flow.Join(Flow.Create<string>()).Run(Materializer);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be("lonely traveler");
            }, Materializer);
        }
    }
}
