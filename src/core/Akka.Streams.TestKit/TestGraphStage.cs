using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    public static class GraphStageMessages
    {
        public class Push
        {
            public static Push Instance { get; } = new Push();

            private Push() { }
        }

        public class UpstreamFinish
        {
            public static UpstreamFinish Instance { get; } = new UpstreamFinish();

            private UpstreamFinish() { }
        }

        public class Failure
        {
            public Failure(Exception ex)
            {
                Ex = ex;
            }

            public Exception Ex { get; }
        }

        public class Pull
        {
            public static Pull Instance { get; } = new Pull();

            private Pull() { }
        }

        public class DownstreamFinish
        {
            public static DownstreamFinish Instance { get; } = new DownstreamFinish();

            private DownstreamFinish() { }
        }
    }

    public sealed class TestSinkStage<T, M> : GraphStageWithMaterializedValue<SinkShape<T>, M>
    {
        public static TestSinkStage<T, M> Create(GraphStageWithMaterializedValue<SinkShape<T>, M> stageUnderTest,
            TestProbe probe) => new TestSinkStage<T, M>(stageUnderTest, probe);

        private readonly Inlet<T> _in = new Inlet<T>("testSinkStage.in");
        private readonly GraphStageWithMaterializedValue<SinkShape<T>, M> _stageUnderTest;
        private readonly TestProbe _probe;

        private TestSinkStage(GraphStageWithMaterializedValue<SinkShape<T>, M> stageUnderTest, TestProbe probe)
        {
            _stageUnderTest = stageUnderTest;
            _probe = probe;
            Shape = new SinkShape<T>(_in);
        }

        public override SinkShape<T> Shape { get; }

        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out M materialized)
        {
            _stageUnderTest.Shape.Inlet.Id = _in.Id;
            var logic = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes, out materialized);

            var inHandler = logic.Handlers[_in.Id] as InHandler;
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

            return logic;
        }
    }

    public sealed class TestSourceStage<T, M> : GraphStageWithMaterializedValue<SourceShape<T>, M>
    {
        public static Source<T, M> Create(GraphStageWithMaterializedValue<SourceShape<T>, M> stageUnderTest,
            TestProbe probe) => Source.FromGraph(new TestSourceStage<T, M>(stageUnderTest, probe));

        private readonly Outlet<T> _out = new Outlet<T>("testSourceStage.out");
        private readonly GraphStageWithMaterializedValue<SourceShape<T>, M> _stageUnderTest;
        private readonly TestProbe _probe;

        private TestSourceStage(GraphStageWithMaterializedValue<SourceShape<T>, M> stageUnderTest, TestProbe probe)
        {
            _stageUnderTest = stageUnderTest;
            _probe = probe;
            Shape = new SourceShape<T>(_out);
        }

        public override SourceShape<T> Shape { get; }

        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out M materialized)
        {
            _stageUnderTest.Shape.Outlet.Id = _out.Id;
            var logic = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes, out materialized);

            var outHandler = logic.Handlers[_out.Id] as OutHandler;
            logic.SetHandler(_out, onPull: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.Pull.Instance);
                outHandler.OnPull();
            }, onDownstreamFinish: () =>
            {
                _probe.Ref.Tell(GraphStageMessages.DownstreamFinish.Instance);
                outHandler.OnDownstreamFinish();
            });

            return logic;
        }
    }
}