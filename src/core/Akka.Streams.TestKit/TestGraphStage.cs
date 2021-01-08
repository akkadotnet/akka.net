//-----------------------------------------------------------------------
// <copyright file="TestGraphStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    public static class GraphStageMessages
    {
        public class Push : INoSerializationVerificationNeeded
        {
            public static Push Instance { get; } = new Push();

            private Push() { }
        }

        public class UpstreamFinish : INoSerializationVerificationNeeded
        {
            public static UpstreamFinish Instance { get; } = new UpstreamFinish();

            private UpstreamFinish() { }
        }

        public class Failure : INoSerializationVerificationNeeded
        {
            public Failure(Exception ex)
            {
                Ex = ex;
            }

            public Exception Ex { get; }
        }

        public class Pull : INoSerializationVerificationNeeded
        {
            public static Pull Instance { get; } = new Pull();

            private Pull() { }
        }

        public class DownstreamFinish : INoSerializationVerificationNeeded
        {
            public static DownstreamFinish Instance { get; } = new DownstreamFinish();

            private DownstreamFinish() { }
        }
    }

    public sealed class TestSinkStage<T, TMat> : GraphStageWithMaterializedValue<SinkShape<T>, TMat>
    {
        public static TestSinkStage<T, TMat> Create(GraphStageWithMaterializedValue<SinkShape<T>, TMat> stageUnderTest,
            TestProbe probe) => new TestSinkStage<T, TMat>(stageUnderTest, probe);

        private readonly Inlet<T> _in = new Inlet<T>("testSinkStage.in");
        private readonly GraphStageWithMaterializedValue<SinkShape<T>, TMat> _stageUnderTest;
        private readonly TestProbe _probe;

        private TestSinkStage(GraphStageWithMaterializedValue<SinkShape<T>, TMat> stageUnderTest, TestProbe probe)
        {
            _stageUnderTest = stageUnderTest;
            _probe = probe;
            Shape = new SinkShape<T>(_in);
        }

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            _stageUnderTest.Shape.Inlet.Id = _in.Id;
            var logicAndMaterialized = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes);
            var logic = logicAndMaterialized.Logic;

            var inHandler = (IInHandler) logic.Handlers[_in.Id];
            logic.SetHandler(_in, onPush: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.Push.Instance);
                inHandler.OnPush();
            }, onUpstreamFinish: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.UpstreamFinish.Instance);
                inHandler.OnUpstreamFinish();
            }, onUpstreamFailure: e =>
            {
                _probe.Ref.Tell(new GraphStageMessages.Failure(e));
                inHandler.OnUpstreamFailure(e);
            });

            return logicAndMaterialized;
        }
    }

    public sealed class TestSourceStage<T, TMat> : GraphStageWithMaterializedValue<SourceShape<T>, TMat>
    {
        public static Source<T, TMat> Create(GraphStageWithMaterializedValue<SourceShape<T>, TMat> stageUnderTest,
            TestProbe probe) => Source.FromGraph(new TestSourceStage<T, TMat>(stageUnderTest, probe));

        private readonly Outlet<T> _out = new Outlet<T>("testSourceStage.out");
        private readonly GraphStageWithMaterializedValue<SourceShape<T>, TMat> _stageUnderTest;
        private readonly TestProbe _probe;

        private TestSourceStage(GraphStageWithMaterializedValue<SourceShape<T>, TMat> stageUnderTest, TestProbe probe)
        {
            _stageUnderTest = stageUnderTest;
            _probe = probe;
            Shape = new SourceShape<T>(_out);
        }

        public override SourceShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            _stageUnderTest.Shape.Outlet.Id = _out.Id;
            var logicAndMaterialized = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes);
            var logic = logicAndMaterialized.Logic;

            var outHandler = (IOutHandler) logic.Handlers[_out.Id];
            logic.SetHandler(_out, onPull: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.Pull.Instance);
                outHandler.OnPull();
            }, onDownstreamFinish: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.DownstreamFinish.Instance);
                outHandler.OnDownstreamFinish();
            });

            return logicAndMaterialized;
        }
    }
}
