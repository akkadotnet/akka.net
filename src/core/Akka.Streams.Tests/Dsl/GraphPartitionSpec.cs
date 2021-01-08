//-----------------------------------------------------------------------
// <copyright file="GraphPartitionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
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

    public class GraphPartitionSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphPartitionSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2,16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Partition_must_partition_to_three_subscribers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = Sink.Seq<int>();
                var t = RunnableGraph.FromGraph(GraphDsl.Create(s, s, s, ValueTuple.Create, (b, sink1, sink2, sink3) =>
                {
                    var partition = b.Add(new Partition<int>(3, i => i > 3 ? 0 : (i < 3 ? 1 : 2)));
                    var source =
                        Source.From(Enumerable.Range(1, 5))
                            .MapMaterializedValue(_ => default((Task<IImmutableList<int>>, Task<IImmutableList<int>>, Task<IImmutableList<int>>)));

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(sink1.Inlet);
                    b.From(partition.Out(1)).To(sink2.Inlet);
                    b.From(partition.Out(2)).To(sink3.Inlet);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var task = Task.WhenAll(t.Item1, t.Item2, t.Item3);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result[0].ShouldAllBeEquivalentTo(new[] {4, 5});
                task.Result[1].ShouldAllBeEquivalentTo(new[] {1, 2});
                task.Result[2].ShouldAllBeEquivalentTo(new[] {3});
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_complete_stage_after_upstream_completes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = this.CreateSubscriberProbe<string>();
                var c2 = this.CreateSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var partition = b.Add(new Partition<string>(2, s => s.Length > 4 ? 0 : 1));
                    var source = Source.From(new[] {"this", "is", "just", "another", "test"});

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(partition.Out(1)).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                c1.Request(1);
                c2.Request(4);
                c1.ExpectNext("another");
                c2.ExpectNext("this", "is", "just", "test");
                c1.ExpectComplete();
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_remember_first_pull_even_thought_first_element_target_another_out()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = this.CreateSubscriberProbe<int>();
                var c2 = this.CreateSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var partition = b.Add(new Partition<int>(2, i => i < 6 ? 0 : 1));
                    var source = Source.From(new [] {6,3});

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(partition.Out(1)).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                c1.Request(1);
                c1.ExpectNoMsg(TimeSpan.FromSeconds(1));
                c2.Request(1);
                c2.ExpectNext(6);
                c1.ExpectNext(3);
                c1.ExpectComplete();
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_cancel_upstream_when_downstreams_cancel()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p1 = this.CreatePublisherProbe<int>();
                var c1 = this.CreateSubscriberProbe<int>();
                var c2 = this.CreateSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var partition = b.Add(new Partition<int>(2, i => i < 6 ? 0 : 1));
                    var source = Source.FromPublisher(p1.Publisher);

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0))
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c1));
                    b.From(partition.Out(1))
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var p1Sub = p1.ExpectSubscription();
                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();
                sub1.Request(3);
                sub2.Request(3);
                p1Sub.SendNext(1);
                p1Sub.SendNext(8);
                c1.ExpectNext(1);
                c2.ExpectNext(8);
                p1Sub.SendNext(2);
                c1.ExpectNext(2);
                sub1.Cancel();
                sub2.Cancel();
                p1Sub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_work_with_merge()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = Sink.Seq<int>();
                var input = new[] {5, 2, 9, 1, 1, 1, 10};

                var task = RunnableGraph.FromGraph(GraphDsl.Create(s, (b, sink) =>
                {
                    var partition = b.Add(new Partition<int>(2, i => i < 4 ? 0 : 1));
                    var merge = b.Add(new Merge<int>(2));
                    var source = Source.From(input).MapMaterializedValue<Task<IImmutableList<int>>>(_ => null);

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(merge.In(0));
                    b.From(partition.Out(1)).To(merge.In(1));
                    b.From(merge.Out).To(sink.Inlet);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                task.Wait(RemainingOrDefault).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(input);
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_stage_completion_is_waiting_for_pending_output()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = this.CreateSubscriberProbe<int>();
                var c2 = this.CreateSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var partition = b.Add(new Partition<int>(2, i => i < 6 ? 0 : 1));
                    var source = Source.From(new[] { 6 });

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(partition.Out(1)).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                c1.Request(1);
                c1.ExpectNoMsg(TimeSpan.FromSeconds(1));
                c2.Request(1);
                c2.ExpectNext(6);
                c1.ExpectComplete();
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Partition_must_fail_stage_if_partitioner_outcome_is_out_of_bound()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = this.CreateSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var partition = b.Add(new Partition<int>(2, i => i < 0 ? -1 : 0));
                    var source = Source.From(new[] { -3 });

                    b.From(source).To(partition.In);
                    b.From(partition.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(partition.Out(1)).To(Sink.Ignore<int>().MapMaterializedValue(_ => NotUsed.Instance));

                    return ClosedShape.Instance;
                })).Run(Materializer);


                c1.Request(1);
                var error = c1.ExpectError();
                error.Should().BeOfType<PartitionOutOfBoundsException>();
                error.Message.Should()
                    .Be(
                        "partitioner must return an index in the range [0,1]. returned: [-1] for input [Int32].");
            }, Materializer);
        }
    }
}

