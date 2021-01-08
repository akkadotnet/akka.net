//-----------------------------------------------------------------------
// <copyright file="GraphDslCompileSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.CSharp.RuntimeBinder;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable RedundantTypeArgumentsOfMethod
// ReSharper disable InvokeAsExtensionMethod
// ReSharper disable UnusedVariable

namespace Akka.Streams.Tests.Dsl
{
    public class GraphDslCompileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphDslCompileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }


        private interface IFruit
        {

        }

        private class Apple : IFruit
        {

        }

        private sealed class OpStage<TIn, TOut> : PushStage<TIn, TOut> where TIn : TOut
        {
            public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
            {
                return context.Push(element);
            }
        }

        private static IStage<TIn, TOut> Op<TIn, TOut>() where TIn : TOut => new OpStage<TIn, TOut>();

        private static IEnumerator<Apple> Apples() => Enumerable.Repeat(new Apple(), int.MaxValue).GetEnumerator();

        private static Flow<string, string, NotUsed> F1
            => Flow.Create<string>().Transform(Op<string, string>).Named("F1");
        private static Flow<string, string, NotUsed> F2
            => Flow.Create<string>().Transform(Op<string, string>).Named("F2");
        private static Flow<string, string, NotUsed> F3
            => Flow.Create<string>().Transform(Op<string, string>).Named("F3");
        private static Flow<string, string, NotUsed> F4
            => Flow.Create<string>().Transform(Op<string, string>).Named("F4");
        private static Flow<string, string, NotUsed> F5
            => Flow.Create<string>().Transform(Op<string, string>).Named("F5");
        private static Flow<string, string, NotUsed> F6
            => Flow.Create<string>().Transform(Op<string, string>).Named("F6");

        private static Source<string, NotUsed> In1 => Source.From(new[] { "a", "b", "c" });
        private static Source<string, NotUsed> In2 => Source.From(new[] { "d", "e", "f" });

        private static Sink<string, NotUsed> Out1
            => Sink.AsPublisher<string>(false).MapMaterializedValue(_ => NotUsed.Instance);

        private static Sink<string, NotUsed> Out2 => Sink.First<string>().MapMaterializedValue(_ => NotUsed.Instance);


        [Fact]
        public void A_Graph_should_build_simple_merge()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                b.From(In1).Via(F1).To(merge.In(0));
                b.From(In2).Via(F2).To(merge.In(1));
                b.From(merge.Out).Via(F3).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_simple_broadcast()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var broadcast = b.Add(new Broadcast<string>(2));
                b.From(In1).Via(F1).To(broadcast.In);
                b.From(broadcast.Out(0)).Via(F2).To(Out1);
                b.From(broadcast.Out(1)).Via(F3).To(Out2);
                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_simple_merge_broadcast()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast = b.Add(new Broadcast<string>(2));
                b.From(In1).Via(F1).To(merge.In(0));
                b.From(In2).Via(F2).To(merge.In(1));
                b.From(merge).Via(F3).To(broadcast);
                b.From(broadcast.Out(0)).Via(F4).To(Out1);
                b.From(broadcast.Out(1)).Via(F4).To(Out2);
                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_simple_merge_broadcast_with_implicits()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast = b.Add(new Broadcast<string>(2));
                var i1 = b.Add(In1);
                var i2 = b.Add(In2);
                var o1 = b.Add(Out1);
                var o2 = b.Add(Out2);

                b.From(i1).Via(F1).To(merge.In(0));
                b.From(merge.Out).Via(F2).To(broadcast.In);
                b.From(broadcast.Out(0)).Via(F3).To(o1);
                b.From(i2).Via(F4).To(merge.In(1));
                b.From(broadcast.Out(1)).Via(F5).To(o2);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        /*
         * in ---> f1 -+-> f2 -+-> f3 ---> b.add(out1)
         *             ^       |
         *             |       V
         *             f5 <-+- f4
         *                  |
         *                  V
         *                  f6 ---> b.add(out2)
         */
        [Fact(Skip = "FIXME needs cycle detection capability")]
        public void A_Graph_should_detect_cycle_in()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast1 = b.Add(new Broadcast<string>(2));
                var broadcast2 = b.Add(new Broadcast<string>(2));
                var feedbackLoopBuffer = Flow.Create<string>().Buffer(10, OverflowStrategy.DropBuffer);

                b.From(In1).Via(F1).To(merge.In(0));
                b.From(merge).Via(F2).To(broadcast1);
                b.From(broadcast1.Out(0)).Via(F3).To(Out1);
                b.From(broadcast1.Out(1)).Via(feedbackLoopBuffer).To(broadcast2);
                b.From(broadcast2.Out(0)).Via(F5).To(merge.In(1)); // cycle
                b.From(broadcast2.Out(1)).Via(F6).To(Out2);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_express_complex_topologies_in_a_readable_way()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast1 = b.Add(new Broadcast<string>(2));
                var broadcast2 = b.Add(new Broadcast<string>(2));
                var feedbackLoopBuffer = Flow.Create<string>().Buffer(10, OverflowStrategy.DropBuffer);
                var i1 = b.Add(In1);
                var o1 = b.Add(Out1);
                var o2 = b.Add(Out2);

                b.From(i1).Via(F2).Via(merge).Via(F2).Via(broadcast1).Via(F3).To(o1);
                b.From(broadcast1).Via(feedbackLoopBuffer).Via(broadcast2).Via(F5).To(merge);
                b.From(broadcast2).Via(F6).To(o2);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_broadcast_merge()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast = b.Add(new Broadcast<string>(2));

                b.From(In1).Via(F1).Via(broadcast).Via(F2).Via(merge).Via(F3).To(Out1);
                b.From(broadcast).Via(F4).To(merge);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_wikipedia_Topological_sorting()
        {
            // see https://en.wikipedia.org/wiki/Topological_sorting#mediaviewer/File:Directed_acyclic_graph.png
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var b3 = b.Add(new Broadcast<string>(2));
                var b7 = b.Add(new Broadcast<string>(2));
                var b11 = b.Add(new Broadcast<string>(3));
                var m8 = b.Add(new Merge<string>(2));
                var m9 = b.Add(new Merge<string>(2));
                var m10 = b.Add(new Merge<string>(2));
                var m11 = b.Add(new Merge<string>(2));
                var in3 = Source.From(new[] { "b" });
                var in5 = Source.From(new[] { "b" });
                var in7 = Source.From(new[] { "a" });
                var out2 = Sink.AsPublisher<string>(false).MapMaterializedValue(_ => NotUsed.Instance);
                var out9 = Sink.AsPublisher<string>(false).MapMaterializedValue(_ => NotUsed.Instance);
                var out10 = Sink.AsPublisher<string>(false).MapMaterializedValue(_ => NotUsed.Instance);
                Func<string, Flow<string, string, NotUsed>> f =
                    s => Flow.Create<string>().Transform(Op<string, string>).Named(s);

                b.From(in7).Via(f("a")).Via(b7).Via(f("b")).Via(m11).Via(f("c")).Via(b11).Via(f("d")).To(out2);
                b.From(b11).Via(f("e")).Via(m9).Via(f("f")).To(out9);
                b.From(b7).Via(f("g")).Via(m8).Via(f("h")).To(m9);
                b.From(b11).Via(f("i")).Via(m10).Via(f("j")).To(out10);
                b.From(in5).Via(f("k")).To(m11);
                b.From(in3).Via(f("a")).Via(b3).Via(f("m")).To(m8);
                b.From(b3).Via(f("n")).To(m10);
                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_make_it_optional_to_specify_flows()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<string>(2));
                var broadcast = b.Add(new Broadcast<string>(2));

                b.From(In1).Via(merge).Via(broadcast).To(Out1);
                b.From(In2).To(merge);
                b.From(broadcast).To(Out2);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_build_unzip_zip()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var zip = b.Add(new Zip<int, string>());
                var unzip = b.Add(new UnZip<int, string>());
                var sink = Sink.AsPublisher<(int, string)>(false).MapMaterializedValue(_ => NotUsed.Instance);
                var source =
                    Source.From(new[]
                    {
                        new KeyValuePair<int, string>(1, "a"), new KeyValuePair<int, string>(2, "b"),
                        new KeyValuePair<int, string>(3, "c")
                    });


                b.From(source).To(unzip.In);
                b.From(unzip.Out0).Via(Flow.Create<int>().Select(x => x * 2)).To(zip.In0);
                b.From(unzip.Out1).To(zip.In1);
                b.From(zip.Out).To(sink);

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_distinguish_between_input_and_output_ports()
        {
            Action action = () =>
            {
                RunnableGraph.FromGraph(GraphDsl.Create(builder =>
                {
                    var zip = builder.Add(new Zip<int, string>());
                    var unzip = builder.Add(new UnZip<int, string>());
                    var wrongOut = Sink.AsPublisher<(int, int)>(false).MapMaterializedValue(_ => NotUsed.Instance);
                    var whatever = Sink.AsPublisher<object>(false).MapMaterializedValue(_ => NotUsed.Instance);

                    builder.Invoking(
                        b => ((dynamic)b).From(Source.From(new[] { 1, 2, 3 })).Via(((dynamic)zip).Left).To(wrongOut))
                        .ShouldThrow<RuntimeBinderException>();

                    builder.Invoking(
                        b => ((dynamic)b).From(Source.From(new[] { "a", "b", "c" })).To(((dynamic)zip).Left))
                        .ShouldThrow<RuntimeBinderException>();

                    builder.Invoking(
                        b => ((dynamic)b).From(Source.From(new[] { "a", "b", "c" })).To(zip.Out))
                        .ShouldThrow<RuntimeBinderException>();

                    builder.Invoking(
                        b => ((dynamic)b).From(((dynamic)zip).Left).To(((dynamic)zip).Right))
                        .ShouldThrow<RuntimeBinderException>();

                    var source =
                        Source.From(new[]
                        {
                            new KeyValuePair<int, string>(1, "a"), new KeyValuePair<int, string>(2, "b"),
                            new KeyValuePair<int, string>(3, "c")
                        });
                    builder.Invoking(
                        b => ((dynamic)b).From(source).Via(unzip.In).To(whatever))
                        .ShouldThrow<RuntimeBinderException>();

                    return ClosedShape.Instance;
                }));
            };

            action.ShouldThrow<ArgumentException>();
        }

        [Fact(Skip = "FIXME Covariance  is not supported")]
        public void A_Graph_should_build_with_variance()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var merge = b.Add(new Merge<IFruit>(2));
                var s1 = Source.FromEnumerator<IFruit>(Apples);
                var s2 = Source.FromEnumerator<Apple>(Apples);

                b.From(s1).Via(Flow.Create<IFruit>()).To(merge.In(0));
                //b.From(s2).Via(Flow.Create<Apple>()).To(merge.In(1));

                b.From(merge.Out)
                    .Via(Flow.Create<IFruit>().Select(x => x))
                    .To(Sink.FromSubscriber(this.CreateManualSubscriberProbe<IFruit>()));

                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact(Skip = "FIXME Covariance  is not supported")]
        public void A_Graph_should_build_with_variance_when_indices_are_not_specified()
        {
            /*
                RunnableGraph.fromGraph(GraphDSL.create() { implicit b ⇒
                import GraphDSL.Implicits._
                val fruitMerge = b.add(Merge[Fruit](2))
                Source.fromIterator[Fruit](apples) ~> fruitMerge
                Source.fromIterator[Apple](apples) ~> fruitMerge
                fruitMerge ~> Sink.head[Fruit]
                "fruitMerge ~> Sink.head[Apple]" shouldNot compile

                val appleMerge = b.add(Merge[Apple](2))
                "Source[Fruit](apples) ~> appleMerge" shouldNot compile
                Source.empty[Apple] ~> appleMerge
                Source.fromIterator[Apple](apples) ~> appleMerge
                appleMerge ~> Sink.head[Fruit]

                val appleMerge2 = b.add(Merge[Apple](2))
                Source.empty[Apple] ~> appleMerge2
                Source.fromIterator[Apple](apples) ~> appleMerge2
                appleMerge2 ~> Sink.head[Apple]

                val fruitBcast = b.add(Broadcast[Fruit](2))
                Source.fromIterator[Apple](apples) ~> fruitBcast
                fruitBcast ~> Sink.head[Fruit]
                fruitBcast ~> Sink.ignore
                "fruitBcast ~> Sink.head[Apple]" shouldNot compile

                val appleBcast = b.add(Broadcast[Apple](2))
                "Source[Fruit](apples) ~> appleBcast" shouldNot compile
                Source.fromIterator[Apple](apples) ~> appleBcast
                appleBcast ~> Sink.head[Fruit]
                appleBcast ~> Sink.head[Apple]
                ClosedShape
            */
        }

        [Fact(Skip = "FIXME Covariance  is not supported")]
        public void A_Graph_should_build_with_implicits_and_variance()
        {
            /*
                RunnableGraph.fromGraph(GraphDSL.create() { implicit b ⇒
                def appleSource = b.add(Source.fromPublisher(TestPublisher.manualProbe[Apple]()))
                def fruitSource = b.add(Source.fromPublisher(TestPublisher.manualProbe[Fruit]()))
                val outA = b add Sink.fromSubscriber(TestSubscriber.manualProbe[Fruit]())
                val outB = b add Sink.fromSubscriber(TestSubscriber.manualProbe[Fruit]())
                val merge = b add Merge[Fruit](11)
                val unzip = b add Unzip[Int, String]()
                val whatever = b add Sink.asPublisher[Any](false)
                import GraphDSL.Implicits._
                b.add(Source.fromIterator[Fruit](apples)) ~> merge.in(0)
                appleSource ~> merge.in(1)
                appleSource ~> merge.in(2)
                fruitSource ~> merge.in(3)
                fruitSource ~> Flow[Fruit].map(identity) ~> merge.in(4)
                appleSource ~> Flow[Apple].map(identity) ~> merge.in(5)
                b.add(Source.fromIterator(apples)) ~> merge.in(6)
                b.add(Source.fromIterator(apples)) ~> Flow[Fruit].map(identity) ~> merge.in(7)
                b.add(Source.fromIterator(apples)) ~> Flow[Apple].map(identity) ~> merge.in(8)
                merge.out ~> Flow[Fruit].map(identity) ~> outA

                b.add(Source.fromIterator(apples)) ~> Flow[Apple] ~> merge.in(9)
                b.add(Source.fromIterator(apples)) ~> Flow[Apple] ~> outB
                b.add(Source.fromIterator(apples)) ~> Flow[Apple] ~> b.add(Sink.asPublisher[Fruit](false))
                appleSource ~> Flow[Apple] ~> merge.in(10)

                Source(List(1 -> "a", 2 -> "b", 3 -> "c")) ~> unzip.in
                unzip.out1 ~> whatever
                unzip.out0 ~> b.add(Sink.asPublisher[Any](false))

                "merge.out ~> b.add(Broadcast[Apple](2))" shouldNot compile
                "merge.out ~> Flow[Fruit].map(identity) ~> b.add(Broadcast[Apple](2))" shouldNot compile
                "fruitSource ~> merge ~> b.add(Broadcast[Apple](2))" shouldNot compile
                ClosedShape
              })
            */
        }

        [Fact]
        public void A_Graph_should_build_with_plain_flow_without_junctions()
        {
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1).Via(F1).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1).Via(F1).Via(F2).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1.Via(F1)).Via(F2).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1).To(F1.To(Out1));
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1.Via(F1)).To(Out1);
                return ClosedShape.Instance;
            })).Run(Materializer);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(In1.Via(F1)).To(F2.To(Out1));
                return ClosedShape.Instance;
            })).Run(Materializer);
        }

        [Fact]
        public void A_Graph_should_suitably_override_attribute_handling_methods()
        {
            var ga = GraphDsl.Create(b =>
            {
                var id = b.Add(GraphStages.Identity<int>());
                return new FlowShape<int, int>(id.Inlet, id.Outlet);
            }).Async().AddAttributes(Attributes.None).Named("useless");

            ga.Module.Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("useless");
            ga.Module.Attributes.GetFirstAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
        }
    }
}
