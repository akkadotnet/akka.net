using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Reflection;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    public class FusingSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FusingSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static Source<int, Unit> Graph (bool async)
        {
            return
                Source.Unfold(1, x => Tuple.Create(x, x))
                    .Filter(x => x%2 == 1)
                    .AlsoTo(
                        Flow.Create<int>()
                            .Fold(0, (sum, i) => sum + i)
                            .To(Sink.First<int>().Named("otherSink"))
                            .AddAttributes(async ? Attributes.CreateAsyncBoundary() : Attributes.None))
                    .Via(Flow.Create<int>().Fold(1, (sum, i) => sum + i).Named("mainSink"));
        }

        private static void SinglePath<TShape, TMat>(Fusing.FusedGraph<TShape, TMat> fusedGraph,
            Attributes.IAttribute from, Attributes.IAttribute to) where TShape : Shape
        {
            var module = fusedGraph.Module as FusedModule;
            var starts = module.Info.AllModules.Where(m => m.Attributes.Contains(from)).ToList();
            starts.Count.Should().Be(1);
            var start = starts[0];
            var ups = module.Info.Upstreams;
            var owner = module.Info.OutOwners;

            var currentModule = start;

            while (true)
            {
                var copied = currentModule as CopiedModule;
                if (copied != null && copied.Attributes.And(copied.CopyOf.Attributes).Contains(to))
                    break;
                if (currentModule.Attributes.Contains(to))
                    break;

                var outs = ups.Where(u => currentModule.InPorts.Contains(u.Key)).Select(x=>x.Value).ToList();
                outs.Count.Should().Be(1);
                currentModule = owner[outs[0]];
            }
        }

        private static void Verify<TShape, TMat>(Fusing.FusedGraph<TShape, TMat> fused, int modules, int downstreams) where TShape : Shape
        {
            var module = fused.Module as FusedModule;
            module.SubModules.Length.Should().Be(modules);
            module.Downstreams.Count.Should().Be(modules -1);
            module.Info.Downstreams.Count.Should().BeGreaterOrEqualTo(downstreams);
            module.Info.Upstreams.Count.Should().BeGreaterOrEqualTo(downstreams);
            SinglePath(fused, new Attributes.Name("mainSink"), new Attributes.Name("unfold"));
            SinglePath(fused, new Attributes.Name("otherSink"), new Attributes.Name("unfold"));
        }

        private static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }

        [Fact]
        public void Fusing_must_fuse_a_moderately_complex_graph()
        {
            var g = Graph(false);
            var fused = Fusing.Aggressive(g);
            Verify(fused, modules: 1, downstreams: 5);
        }

        [Fact]
        public void Fusing_must_not_fuse_accross_AsyncBoundary()
        {
            var g = Graph(true);
            var fused = Fusing.Aggressive(g);
            Verify(fused, modules: 2, downstreams: 5);
        }

        [Fact]
        public void Fusing_must_not_fuse_a_FusedGraph_again()
        {
            var g = Fusing.Aggressive(Graph(false));
            Fusing.Aggressive(g).Should().BeSameAs(g);
        }

        [Fact]
        public void Fusing_must_properly_fuse_a_FusedGraph_that_has_been_extended_no_AsyncBoundary()
        {
            var src = Fusing.Aggressive(Graph(false));
            var fused = Fusing.Aggressive(Source.FromGraph(src).To(Sink.First<int>()));
            Verify(fused, modules: 1, downstreams: 6);
        }

        [Fact]
        public void Fusing_must_properly_fuse_a_FusedGraph_that_has_been_extended_with_AsyncBoundary()
        {
            var src = Fusing.Aggressive(Graph(true));
            var fused = Fusing.Aggressive(Source.FromGraph(src).To(Sink.First<int>()));
            Verify(fused, modules: 2, downstreams: 6);
        }

        [Fact]
        public void A_SubFusingActorMaterializer_must_work_with_asynchronous_boundaries_in_the_subflows()
        {
            var async = Flow.Create<int>().Map(x => x*2).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Map(x => x*10)
                .FlatMapMerge(5, i => Source.From(Enumerable.Range(i, 10)).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Distinct().OrderBy(i => i).ShouldAllBeEquivalentTo(Enumerable.Range(0, 199).Where(i => i%2 == 0));
        }

        [Fact]
        public void A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_manual ()
        {
            Func<string> refFunc = () =>
            {
                var bus = (BusLogging) GraphInterpreter.Current.Log;
                return GetInstanceField(typeof(BusLogging), bus, "_logSource") as string;
            };

            var async = Flow.Create<int>().Map(x =>
            {
                TestActor.Tell(refFunc());
                return x;
            }).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Map(x =>
                {
                    TestActor.Tell(refFunc());
                    return x;
                })
                .FlatMapMerge(5, i => Source.Single(i).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3));
            t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 10));

            var refs = ReceiveN(20);
            // main flow + 10 subflows
            refs.Distinct().Should().HaveCount(11);
        }

        [Fact]
        public void A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_combinator()
        {
            Func<string> refFunc = () =>
            {
                var bus = (BusLogging)GraphInterpreter.Current.Log;
                return GetInstanceField(typeof(BusLogging), bus, "_logSource") as string;
            };

            var flow = Flow.Create<int>().Map(x =>
            {
                TestActor.Tell(refFunc());
                return x;
            });
            var t = Source.From(Enumerable.Range(0, 10))
                .Map(x =>
                {
                    TestActor.Tell(refFunc());
                    return x;
                })
                .FlatMapMerge(5, i => Source.Single(i).Via(flow.Async()))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3));
            t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 10));

            var refs = ReceiveN(20);
            // main flow + 10 subflows
            refs.Distinct().Should().HaveCount(11);
        }
    }
}
