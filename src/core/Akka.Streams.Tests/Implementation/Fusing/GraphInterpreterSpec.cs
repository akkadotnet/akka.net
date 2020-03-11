//-----------------------------------------------------------------------
// <copyright file="GraphInterpreterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.RequestOne;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnNext;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnComplete;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class GraphInterpreterSpec : GraphInterpreterSpecKit
    {
        private readonly SimpleLinearGraphStage<int> _identity;
        private readonly Detacher<int> _detach;
        private readonly Zip<int, string> _zip;
        private readonly Broadcast<int> _broadcast;
        private readonly Merge<int> _merge;
        private readonly Balance<int> _balance;

        public GraphInterpreterSpec(ITestOutputHelper output = null) : base(output)
        {
            _identity = GraphStages.Identity<int>();
            _detach = new Detacher<int>();
            _zip = new Zip<int, string>();
            _broadcast = new Broadcast<int>(2);
            _merge = new Merge<int>(2);
            _balance = new Balance<int>(2);
        }

        [Fact]
        public void GraphInterpreter_should_implement_identity()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<int>("sink");

                builder(_identity)
                    .Connect(source, _identity.Inlet)
                    .Connect(_identity.Outlet, sink)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 1));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_chained_identity()
        {
            WithTestSetup((setup, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<int>("sink");

                // Constructing an assembly by hand and resolving ambiguities
                var assembly = new GraphAssembly(
                    stages: new IGraphStageWithMaterializedValue<Shape, object>[] {_identity, _identity},
                    originalAttributes: new[] {Attributes.None, Attributes.None},
                    inlets: new Inlet[] {_identity.Inlet, _identity.Inlet, null},
                    inletOwners: new[] {0, 1, -1},
                    outlets: new Outlet[] {null, _identity.Outlet, _identity.Outlet},
                    outletOwners: new[] {-1, 0, 1}
                    );

                setup.ManualInit(assembly);
                setup.Interpreter.AttachDownstreamBoundary(2, sink);
                setup.Interpreter.AttachUpstreamBoundary(0, source);
                setup.Interpreter.Init(null);

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 1));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_detacher_stage()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<int>("sink");

                builder(_detach)
                    .Connect(source, _detach.Shape.Inlet)
                    .Connect(_detach.Shape.Outlet, sink)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 1), new RequestOne(source));

                // Source waits
                source.OnNext(2);
                lastEvents().Should().BeEmpty();

                // "PushAndPull
                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 2), new RequestOne(source));

                // Source waits
                sink.RequestOne();
                lastEvents().Should().BeEmpty();

                // "PushAndPull
                source.OnNext(3);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 3), new RequestOne(source));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_Zip()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source1 = setup.NewUpstreamProbe<int>("source1");
                var source2 = setup.NewUpstreamProbe<string>("source2");
                var sink = setup.NewDownstreamProbe<(int, string)>("sink");

                builder(_zip)
                    .Connect(source1, _zip.In0)
                    .Connect(source2, _zip.In1)
                    .Connect(_zip.Out, sink)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source1), new RequestOne(source2));

                source1.OnNext(42);
                lastEvents().Should().BeEmpty();

                source2.OnNext("Meaning of life");
                lastEvents()
                    .Should()
                    .Equal(new OnNext(sink, (42, "Meaning of life")), new RequestOne(source1),
                        new RequestOne(source2));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_Broadcast()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink1 = setup.NewDownstreamProbe<int>("sink1");
                var sink2 = setup.NewDownstreamProbe<int>("sink2");

                builder(_broadcast)
                    .Connect(source, _broadcast.In)
                    .Connect(_broadcast.Out(0), sink1)
                    .Connect(_broadcast.Out(1), sink2)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink1.RequestOne();
                lastEvents().Should().BeEmpty();

                sink2.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink1, 1), new OnNext(sink2, 1));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_broadcast_zip()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<(int, int)>("sink");
                var zip = new Zip<int, int>();

                builder(new IGraphStageWithMaterializedValue<Shape, object>[] {zip, _broadcast})
                    .Connect(source, _broadcast.In)
                    .Connect(_broadcast.Out(0), zip.In0)
                    .Connect(_broadcast.Out(1), zip.In1)
                    .Connect(zip.Out, sink)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, (1, 1)), new RequestOne(source));

                sink.RequestOne();
                source.OnNext(2);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, (2, 2)), new RequestOne(source));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_zip_broadcast()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source1 = setup.NewUpstreamProbe<int>("source1");
                var source2 = setup.NewUpstreamProbe<int>("source2");
                var sink1 = setup.NewDownstreamProbe<(int, int)>("sink1");
                var sink2 = setup.NewDownstreamProbe<(int, int)>("sink2");
                var zip = new Zip<int, int>();
                var broadcast = new Broadcast<(int, int)>(2);

                builder(new IGraphStageWithMaterializedValue<Shape, object>[] {broadcast, zip})
                    .Connect(source1, zip.In0)
                    .Connect(source2, zip.In1)
                    .Connect(zip.Out, broadcast.In)
                    .Connect(broadcast.Out(0), sink1)
                    .Connect(broadcast.Out(1), sink2)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink1.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source1), new RequestOne(source2));

                sink2.RequestOne();

                source1.OnNext(1);
                lastEvents().Should().BeEmpty();

                source2.OnNext(2);
                lastEvents()
                    .Should()
                    .Equal(new OnNext(sink1, (1, 2)), new RequestOne(source1),
                        new RequestOne(source2), new OnNext(sink2, (1, 2)));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_merge()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source1 = setup.NewUpstreamProbe<int>("source1");
                var source2 = setup.NewUpstreamProbe<int>("source2");
                var sink = setup.NewDownstreamProbe<int>("sink");

                builder(_merge)
                    .Connect(source1, _merge.In(0))
                    .Connect(source2, _merge.In(1))
                    .Connect(_merge.Out, sink)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source1), new RequestOne(source2));

                source1.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 1), new RequestOne(source1));

                source2.OnNext(2);
                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 2), new RequestOne(source2));

                sink.RequestOne();
                lastEvents().Should().BeEmpty();

                source2.OnNext(3);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 3), new RequestOne(source2));

                sink.RequestOne();
                lastEvents().Should().BeEmpty();

                source1.OnNext(4);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 4), new RequestOne(source1));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_balance()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink1 = setup.NewDownstreamProbe<int>("sink1");
                var sink2 = setup.NewDownstreamProbe<int>("sink2");

                builder(_balance)
                    .Connect(source, _balance.In)
                    .Connect(_balance.Out(0), sink1)
                    .Connect(_balance.Out(1), sink2)
                    .Init();

                lastEvents().Should().BeEmpty();

                sink1.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                sink2.RequestOne();
                lastEvents().Should().BeEmpty();

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink1, 1), new RequestOne(source));

                source.OnNext(2);
                lastEvents().Should().BeEquivalentTo(new OnNext(sink2, 2));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_non_divergent_cycle()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<int>("sink");

                builder(new IGraphStageWithMaterializedValue<Shape, object>[] { _merge, _balance })
                    .Connect(source, _merge.In(0))
                    .Connect(_merge.Out, _balance.In)
                    .Connect(_balance.Out(0), sink)
                    .Connect(_balance.Out(1), _merge.In(1))
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new RequestOne(source), new OnNext(sink, 1));

                // Token enters merge-balance cycle and gets stuck
                source.OnNext(2);
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                // Unstuck it
                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 2));
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_divergent_cycle()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<int>("source");
                var sink = setup.NewDownstreamProbe<int>("sink");

                builder(new IGraphStageWithMaterializedValue<Shape, object>[] { _detach, _balance, _merge })
                    .Connect(source, _merge.In(0))
                    .Connect(_merge.Out, _balance.In)
                    .Connect(_balance.Out(0), sink)
                    .Connect(_balance.Out(1), _detach.Shape.Inlet)
                    .Connect(_detach.Shape.Outlet, _merge.In(1))
                    .Init();

                lastEvents().Should().BeEmpty();

                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext(1);
                lastEvents().Should().BeEquivalentTo(new RequestOne(source), new OnNext(sink, 1));

                // Token enters merge-balance cycle and spins until event limit
                // Without the limit this would spin forever (where forever = int.MaxValue iterations)
                source.OnNext(2, eventLimit: 1000);
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                // The cycle is still alive and kicking, just suspended due to the event limit
                setup.Interpreter.IsSuspended.Should().BeTrue();

                // Do to the fairness properties of both the interpreter event queue and the balance stage
                // the element will eventually leave the cycle and reaches the sink.
                // This should not hang even though we do not have an event limit set
                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, 2));

                // The cycle is now empty
                setup.Interpreter.IsSuspended.Should().BeFalse();
            });
        }

        [Fact]
        public void GraphInterpreter_should_implement_buffer()
        {
            WithTestSetup((setup, builder, lastEvents) =>
            {
                var source = setup.NewUpstreamProbe<string>("source");
                var sink = setup.NewDownstreamProbe<string>("sink");
                var buffer = new Buffer<string>(2, OverflowStrategy.Backpressure);

                builder(buffer)
                    .Connect(source, buffer.Inlet)
                    .Connect(buffer.Outlet, sink)
                    .Init();

                setup.StepAll();
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                sink.RequestOne();
                lastEvents().Should().BeEmpty();

                source.OnNext("A");
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, "A"), new RequestOne(source));

                source.OnNext("B");
                lastEvents().Should().BeEquivalentTo(new RequestOne(source));

                source.OnNext("C", eventLimit: 0);
                sink.RequestOne();
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, "B"), new RequestOne(source));

                sink.RequestOne(eventLimit: 0);
                source.OnComplete(eventLimit: 3);

                // OnComplete arrives early due to push chasing
                lastEvents().Should().BeEquivalentTo(new OnNext(sink, "C"), new OnComplete(sink));
            });
        }
    }
}
