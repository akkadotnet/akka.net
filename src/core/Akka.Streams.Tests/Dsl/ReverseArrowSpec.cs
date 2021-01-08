//-----------------------------------------------------------------------
// <copyright file="ReverseArrowSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit;
using FluentAssertions;
using Microsoft.CSharp.RuntimeBinder;
using Xunit;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class ReverseArrowSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public ReverseArrowSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static Task<IImmutableList<int>> MaterializedValue
            => Task.FromResult<IImmutableList<int>>(ImmutableList<int>.Empty);

        private static Source<int, Task<IImmutableList<int>>> Source
            => Streams.Dsl.Source.From(Enumerable.Range(1, 3)).MapMaterializedValue(_ => MaterializedValue);

        private static Sink<int, Task<IImmutableList<int>>> Sink =>
            Flow.Create<int>().Limit(10).ToMaterialized(Streams.Dsl.Sink.Seq<int>(), Keep.Right);
        
        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_Inlets()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                b.To(s.Inlet).From(Source);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {1, 2, 3});
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_SinkShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                b.To(s).From(Source);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_Sink()
        {
            var sub = this.CreateManualSubscriberProbe<int>();
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.To(Streams.Dsl.Sink.FromSubscriber(sub))
                    .From(Streams.Dsl.Source.From(Enumerable.Range(1, 3)));
                
                return ClosedShape.Instance;
            })).Run(Materializer);
            
            sub.ExpectSubscription().Request(10);
            sub.ExpectNext(1, 2, 3);
            sub.ExpectComplete();
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_not_work_from_Outlets()
        {
            RunnableGraph.FromGraph(
                GraphDsl.Create(b =>
                {
                    var o = b.Add(Source).Outlet;
                    b.Invoking(builder => ((dynamic) builder).To(o).From(Source))
                        .ShouldThrow<RuntimeBinderException>();
                    b.To(Sink).From(o);
                    return ClosedShape.Instance;
                }));
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_not_work_from_SourceShape()
        {
            RunnableGraph.FromGraph(
                GraphDsl.Create(b =>
                {
                    var o = b.Add(Source);
                    b.Invoking(builder => ((dynamic) builder).To(o).From(Source))
                        .ShouldThrow<RuntimeBinderException>();
                    b.To(Sink).From(o);
                    return ClosedShape.Instance;
                }));
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_not_work_from_Source()
        {
            var b = new GraphDsl.Builder<NotUsed>();
            b.Invoking(builder => ((dynamic) builder).To(Source).From(Source)).ShouldThrow<RuntimeBinderException>();
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_FlowShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(Flow.Create<int>());
                b.To(f).From(Source);
                b.From(f).To(s);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_UniformFanInShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Merge<int, int>(2));
                b.To(f).From(Source);
                b.To(f).From(Streams.Dsl.Source.Empty<int>().MapMaterializedValue(_ => MaterializedValue));
                b.From(f).To(s);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_from_UniformFanOutShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Broadcast<int>(2));
                b.To(f).From(Source);
                b.From(f).To(Streams.Dsl.Sink.Ignore<int>().MapMaterializedValue(_ => MaterializedValue));
                b.From(f).To(s);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_Outlets()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var o = b.Add(Source).Outlet;
                b.To(s).From(o);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_SourceShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var o = b.Add(Source);
                b.To(s).From(o);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_Source()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                b.To(s).From(Source);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_FlowShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(Flow.Create<int>());
                b.To(s).From(f);
                b.From(Source).To(f);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_UniformFanInShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Merge<int, int>(2));
                b.To(s).From(f);
                b.From(Streams.Dsl.Source.Empty<int>().MapMaterializedValue(_ => MaterializedValue)).To(f);
                b.From(Source).To(f);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_fail_towards_already_full_UniformFanInShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Merge<int, int>(2));
                var src = b.Add(Source);
                b.From(Streams.Dsl.Source.Empty<int>().MapMaterializedValue(_ => MaterializedValue)).To(f);
                b.From(src).To(f);

                b.Invoking(builder => builder.To(s).Via(f).From(src))
                    .ShouldThrow<ArgumentException>()
                    .WithMessage("No more inlets on junction");

                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_towards_UniformFanOutShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Broadcast<int>(2));
                b.To(s).From(f);
                b.To(Streams.Dsl.Sink.Ignore<int>().MapMaterializedValue(_ => MaterializedValue)).From(f);
                b.From(Source).To(f);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_fail_towards_already_full_UniformFanOutShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                var f = b.Add(new Broadcast<int>(2));
                var sink2 = b.Add(Streams.Dsl.Sink.Ignore<int>().MapMaterializedValue(_ => MaterializedValue));
                var src = b.Add(Source);
                b.From(src).To(f);
                b.To(sink2).From(f);

                b.Invoking(builder => builder.To(s).Via(f).From(src))
                    .ShouldThrow<ArgumentException>()
                    .WithMessage("The output port [StatefulSelectMany.out] is already connected");

                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_across_a_Flow()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                b.To(s).Via(Flow.Create<int>().MapMaterializedValue(_ => MaterializedValue)).From(Source);
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void Reverse_Arrows_in_the_GraphDsl_must_work_across_a_FlowShape()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink, (b, s) =>
            {
                b.To(s).Via(b.Add(Flow.Create<int>().MapMaterializedValue(_ => MaterializedValue))).From(Source);
                
                return ClosedShape.Instance;
            })).Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] { 1, 2, 3 });
        }
    }
}
