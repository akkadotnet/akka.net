//-----------------------------------------------------------------------
// <copyright file="StreamLayoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
using Fuse = Akka.Streams.Implementation.Fusing.Fusing;

namespace Akka.Streams.Tests.Implementation
{
    public class StreamLayoutSpec : Akka.TestKit.Xunit2.TestKit
    {
        #region internal classes

        private class TestAtomicModule : AtomicModule
        {
            public TestAtomicModule(int inportCount, int outportCount)
            {
                var inports = Enumerable.Range(0, inportCount).Select(i => new Inlet<object>(".in" + i)).ToImmutableArray<Inlet>();
                var outports = Enumerable.Range(0, outportCount).Select(i => new Outlet<object>(".out" + i)).ToImmutableArray<Outlet>();

                Shape = new AmorphousShape(inports, outports);
            }

            public override Shape Shape { get; }
            public override IModule ReplaceShape(Shape shape)
            {
                throw new NotImplementedException();
            }
            
            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes => Attributes.None;
            public override IModule WithAttributes(Attributes attributes)
            {
                return this;
            }
        }
        private class TestPublisher : IPublisher<object>, ISubscription
        {
            internal readonly IModule Owner;
            internal readonly OutPort Port;
            internal IModule DownstreamModule;
            internal InPort DownstreamPort;

            public TestPublisher(IModule owner, OutPort port)
            {
                Owner = owner;
                Port = port;
            }

            public void Subscribe(ISubscriber<object> subscriber)
            {
                var sub = subscriber as TestSubscriber;
                if (sub != null)
                {
                    DownstreamModule = sub.Owner;
                    DownstreamPort = sub.Port;
                    sub.OnSubscribe(this);
                }
            }

            public void Request(long n) { }
            public void Cancel() { }
        }
        private class TestSubscriber : ISubscriber<object>
        {
            internal readonly IModule Owner;
            internal readonly InPort Port;
            internal IModule UpstreamModule;
            internal OutPort UpstreamPort;

            public TestSubscriber(IModule owner, InPort port)
            {
                Owner = owner;
                Port = port;
            }

            public void OnSubscribe(ISubscription subscription)
            {
                var publisher = subscription as TestPublisher;
                if (publisher != null)
                {
                    UpstreamModule = publisher.Owner;
                    UpstreamPort = publisher.Port;
                }
            }

            public void OnError(Exception cause) { }
            public void OnComplete() { }
            void ISubscriber<object>.OnNext(object element) { }
            public void OnNext(object element) { }
        }

        private class FlatTestMaterializer : MaterializerSession
        {
            internal ImmutableList<TestPublisher> Publishers = ImmutableList<TestPublisher>.Empty;
            internal ImmutableList<TestSubscriber> Subscribers = ImmutableList<TestSubscriber>.Empty;

            public FlatTestMaterializer(IModule module) : base(module, Attributes.None)
            {
            }

            protected override object MaterializeAtomic(AtomicModule atomic, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues)
            {
                foreach (var inPort in atomic.InPorts)
                {
                    var subscriber = new TestSubscriber(atomic, inPort);
                    Subscribers = Subscribers.Add(subscriber);
                    AssignPort(inPort, UntypedSubscriber.FromTyped(subscriber));
                }
                foreach (var outPort in atomic.OutPorts)
                {
                    var publisher = new TestPublisher(atomic, outPort);
                    Publishers = Publishers.Add(publisher);
                    AssignPort(outPort, UntypedPublisher.FromTyped(publisher));
                }

                return NotUsed.Instance;
            }
        }

        #endregion

        private const int TooDeepForStack = 5000;
        // Seen tests run in 9-10 seconds, these test cases are heavy on the GC
        private static readonly TimeSpan VeryPatient = TimeSpan.FromSeconds(20);
        private readonly IMaterializer _materializer;

        private static TestAtomicModule TestStage() => new TestAtomicModule(1, 1);
        private static TestAtomicModule TestSource() => new TestAtomicModule(0, 1);
        private static TestAtomicModule TestSink() => new TestAtomicModule(1, 0);

        public StreamLayoutSpec(ITestOutputHelper output) : base(output: output)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            _materializer = ActorMaterializer.Create(Sys, ActorMaterializerSettings.Create(Sys).WithAutoFusing(false));
        }

        [Fact]
        public void StreamLayout_should_be_able_to_model_simple_linear_stages()
        {
            var stage1 = TestStage();

            stage1.InPorts.Count.Should().Be(1);
            stage1.OutPorts.Count.Should().Be(1);
            stage1.IsRunnable.Should().Be(false);
            stage1.IsFlow.Should().Be(true);
            stage1.IsSink.Should().Be(false);
            stage1.IsSource.Should().Be(false);

            var stage2 = TestStage();
            var flow12 = stage1.Compose<object, object, NotUsed>(stage2, Keep.None).Wire(stage1.OutPorts.First(), stage2.InPorts.First());

            flow12.InPorts.Should().BeEquivalentTo(stage1.InPorts);
            flow12.OutPorts.Should().BeEquivalentTo(stage2.OutPorts);
            flow12.IsRunnable.Should().Be(false);
            flow12.IsFlow.Should().Be(true);
            flow12.IsSink.Should().Be(false);
            flow12.IsSource.Should().Be(false);

            var source0 = TestSource();

            source0.InPorts.Count.Should().Be(0);
            source0.OutPorts.Count.Should().Be(1);
            source0.IsRunnable.Should().Be(false);
            source0.IsFlow.Should().Be(false);
            source0.IsSink.Should().Be(false);
            source0.IsSource.Should().Be(true);

            var sink3 = TestSink();

            sink3.InPorts.Count.Should().Be(1);
            sink3.OutPorts.Count.Should().Be(0);
            sink3.IsRunnable.Should().Be(false);
            sink3.IsFlow.Should().Be(false);
            sink3.IsSink.Should().Be(true);
            sink3.IsSource.Should().Be(false);

            var source012 = source0.Compose<object, object, NotUsed>(flow12, Keep.None).Wire(source0.OutPorts.First(), flow12.InPorts.First());

            source012.InPorts.Count.Should().Be(0);
            source012.OutPorts.Should().BeEquivalentTo(flow12.OutPorts);
            source012.IsRunnable.Should().Be(false);
            source012.IsFlow.Should().Be(false);
            source012.IsSink.Should().Be(false);
            source012.IsSource.Should().Be(true);

            var sink123 = flow12.Compose<object, object, NotUsed>(sink3, Keep.None).Wire(flow12.OutPorts.First(), sink3.InPorts.First());

            sink123.InPorts.Should().BeEquivalentTo(flow12.InPorts);
            sink123.OutPorts.Count.Should().Be(0);
            sink123.IsRunnable.Should().Be(false);
            sink123.IsFlow.Should().Be(false);
            sink123.IsSink.Should().Be(true);
            sink123.IsSource.Should().Be(false);

            var runnable0123A = source0.Compose<object, object, NotUsed>(sink123, Keep.None).Wire(source0.OutPorts.First(), sink123.InPorts.First());
            source012.Compose<object, object, NotUsed>(sink3, Keep.None).Wire(source012.OutPorts.First(), sink3.InPorts.First());
            source0
                .Compose<object, object, NotUsed>(flow12, Keep.None).Wire(source0.OutPorts.First(), flow12.InPorts.First())
                .Compose<object, object, NotUsed>(sink3, Keep.None).Wire(flow12.OutPorts.First(), sink3.InPorts.First());

            runnable0123A.InPorts.Count.Should().Be(0);
            runnable0123A.OutPorts.Count.Should().Be(0);
            runnable0123A.IsRunnable.Should().Be(true);
            runnable0123A.IsFlow.Should().Be(false);
            runnable0123A.IsSink.Should().Be(false);
            runnable0123A.IsSource.Should().Be(false);
        }

        [Fact]
        public void StreamLayout_should_be_able_to_materialize_linear_layouts()
        {
            var source = TestSource();
            var stage1 = TestStage();
            var stage2 = TestStage();
            var sink = TestSink();

            var runnable = source
                .Compose<object, object, object>(stage1, Keep.None).Wire(source.OutPorts.First(), stage1.InPorts.First())
                .Compose<object, object, object>(stage2, Keep.None).Wire(stage1.OutPorts.First(), stage2.InPorts.First())
                .Compose<object, object, object>(sink, Keep.None).Wire(stage2.OutPorts.First(), sink.InPorts.First());

            CheckMaterialized(runnable);
        }

#if !CORECLR
        [Fact(Skip = "We can't catch a StackOverflowException")]
        public void StreamLayout_should_fail_fusing_when_value_computation_is_too_complex()
        {
            // this tests that the canary in to coal mine actually works
            var g = Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Flow.Create<int>().MapMaterializedValue(_ => 1),
                    (flow, i) => flow.MapMaterializedValue(x => x + i));
            g.Invoking(flow => Streams.Fusing.Aggressive(flow)).ShouldThrow<StackOverflowException>();
        }
#endif

        [Fact]
        public void StreamLayout_should_not_fail_materialization_when_building_a_large_graph_with_simple_computation_when_starting_from_a_Source()
        {
            var g = Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Source.Single(42).MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x));

            var t = g.ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(_materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        [Fact]
        public void StreamLayout_should_not_fail_materialization_when_building_a_large_graph_with_simple_computation_when_starting_from_a_Flow()
        {
            var g = Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Flow.Create<int>().MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x));

            var t = g.RunWith(Source.Single(42).MapMaterializedValue(_ => 1), Sink.Seq<int>(), _materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        [Fact]
        public void StreamLayout_should_not_fail_materialization_when_building_a_large_graph_with_simple_computation_when_using_Via()
        {
            var g = Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Source.Single(42).MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x));

            var t = g.ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(_materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        [Fact]
        public void StreamLayout_should_not_fail_fusing_and_materialization_when_building_a_large_graph_with_simple_computation_when_starting_from_a_Source()
        {
            var g = Source.FromGraph(Fuse.Aggressive(Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Source.Single(42).MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x))));

            var m = g.ToMaterialized(Sink.Seq<int>(), Keep.Both);
            var t = m.Run(_materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        [Fact]
        public void StreamLayout_should_not_fail_fusing_and_materialization_when_building_a_large_graph_with_simple_computation_when_starting_from_a_Flow()
        {
            var g = Flow.FromGraph(Fuse.Aggressive(Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Flow.Create<int>().MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x))));

            var t = g.RunWith(Source.Single(42).MapMaterializedValue(_ => 1), Sink.Seq<int>(), _materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        [Fact]
        public void StreamLayout_should_not_fail_fusing_and_materialization_when_building_a_large_graph_with_simple_computation_when_using_Via()
        {
            var g = Source.FromGraph(Fuse.Aggressive(Enumerable.Range(1, TooDeepForStack)
                .Aggregate(Source.Single(42).MapMaterializedValue(_ => 1), (source, i) => source.Select(x => x))));

            var t = g.ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(_materializer);
            var materialized = t.Item1;
            var result = t.Item2.AwaitResult(VeryPatient);

            materialized.Should().Be(1);
            result.Count.Should().Be(1);
            result.Should().Contain(42);
        }

        private void CheckMaterialized(IModule topLevel)
        {
            var materializer = new FlatTestMaterializer(topLevel);
            materializer.Materialize();

            materializer.Publishers.IsEmpty.Should().Be(false);
            materializer.Subscribers.IsEmpty.Should().Be(false);
            materializer.Subscribers.Count.Should().Be(materializer.Publishers.Count);

            var inToSubscriber = materializer.Subscribers.ToImmutableDictionary(x => x.Port, x => x);
            var outToPublisher = materializer.Publishers.ToImmutableDictionary(x => x.Port, x => x);

            foreach (var publisher in materializer.Publishers)
            {
                publisher.Owner.IsAtomic.Should().Be(true);
                topLevel.Upstreams[publisher.DownstreamPort].Should().Be(publisher.Port);
            }

            foreach (var subscriber in materializer.Subscribers)
            {
                subscriber.Owner.IsAtomic.Should().Be(true);
                topLevel.Downstreams[subscriber.UpstreamPort].Should().Be(subscriber.Port);
            }

            var allAtomic = GetAllAtomic(topLevel);

            foreach (var atomic in allAtomic)
            {
                foreach (var inPort in atomic.InPorts)
                {
                    if (inToSubscriber.TryGetValue(inPort, out var subscriber))
                    {
                        subscriber.Owner.Should().Be(atomic);
                        subscriber.UpstreamPort.Should().Be(topLevel.Upstreams[inPort]);
                        subscriber.UpstreamModule.OutPorts.Should().Contain(x => outToPublisher[x].DownstreamPort == inPort);
                    }
                }

                foreach (var outPort in atomic.OutPorts)
                {
                    if (outToPublisher.TryGetValue(outPort, out var publisher))
                    {
                        publisher.Owner.Should().Be(atomic);
                        publisher.DownstreamPort.Should().Be(topLevel.Downstreams[outPort]);
                        publisher.DownstreamModule.InPorts.Should().Contain(x => inToSubscriber[x].UpstreamPort == outPort);
                    }
                }
            }

            materializer.Publishers.Distinct().Count().Should().Be(materializer.Publishers.Count);
            materializer.Subscribers.Distinct().Count().Should().Be(materializer.Subscribers.Count);

            // no need to return anything at the moment
        }

        private IImmutableSet<IModule> GetAllAtomic(IModule module)
        {
            var group = module.SubModules.GroupBy(x => x.IsAtomic).ToDictionary(x => x.Key, x => x.ToImmutableHashSet());

            if (!group.TryGetValue(true, out var atomics))
                atomics = ImmutableHashSet<IModule>.Empty;
            if (!group.TryGetValue(false, out var composites))
                composites = ImmutableHashSet<IModule>.Empty;

            return atomics.Union(composites.SelectMany(GetAllAtomic));
        }
    }
}
