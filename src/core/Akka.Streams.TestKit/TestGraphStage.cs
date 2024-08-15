// -----------------------------------------------------------------------
//  <copyright file="TestGraphStage.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;

namespace Akka.Streams.TestKit;

public static class GraphStageMessages
{
    public class Push : INoSerializationVerificationNeeded
    {
        private Push()
        {
        }

        public static Push Instance { get; } = new();
    }

    public class UpstreamFinish : INoSerializationVerificationNeeded
    {
        private UpstreamFinish()
        {
        }

        public static UpstreamFinish Instance { get; } = new();
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
        private Pull()
        {
        }

        public static Pull Instance { get; } = new();
    }

    public class DownstreamFinish : INoSerializationVerificationNeeded
    {
        private DownstreamFinish()
        {
        }

        public static DownstreamFinish Instance { get; } = new();
    }
}

public sealed class TestSinkStage<T, TMat> : GraphStageWithMaterializedValue<SinkShape<T>, TMat>
{
    private readonly Inlet<T> _in = new("testSinkStage.in");
    private readonly TestProbe _probe;
    private readonly GraphStageWithMaterializedValue<SinkShape<T>, TMat> _stageUnderTest;

    private TestSinkStage(GraphStageWithMaterializedValue<SinkShape<T>, TMat> stageUnderTest, TestProbe probe)
    {
        _stageUnderTest = stageUnderTest;
        _probe = probe;
        Shape = new SinkShape<T>(_in);
    }

    public override SinkShape<T> Shape { get; }

    public static TestSinkStage<T, TMat> Create(GraphStageWithMaterializedValue<SinkShape<T>, TMat> stageUnderTest,
        TestProbe probe)
    {
        return new TestSinkStage<T, TMat>(stageUnderTest, probe);
    }

    public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
    {
        _stageUnderTest.Shape.Inlet.Id = _in.Id;
        var logicAndMaterialized = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes);
        var logic = logicAndMaterialized.Logic;

        var inHandler = (IInHandler)logic.Handlers[_in.Id];
        logic.SetHandler(_in, () =>
        {
            _probe.Ref.Tell(GraphStageMessages.Push.Instance);
            inHandler.OnPush();
        }, () =>
        {
            _probe.Ref.Tell(GraphStageMessages.UpstreamFinish.Instance);
            inHandler.OnUpstreamFinish();
        }, e =>
        {
            _probe.Ref.Tell(new GraphStageMessages.Failure(e));
            inHandler.OnUpstreamFailure(e);
        });

        return logicAndMaterialized;
    }
}

public sealed class TestSourceStage<T, TMat> : GraphStageWithMaterializedValue<SourceShape<T>, TMat>
{
    private readonly Outlet<T> _out = new("testSourceStage.out");
    private readonly TestProbe _probe;
    private readonly GraphStageWithMaterializedValue<SourceShape<T>, TMat> _stageUnderTest;

    private TestSourceStage(GraphStageWithMaterializedValue<SourceShape<T>, TMat> stageUnderTest, TestProbe probe)
    {
        _stageUnderTest = stageUnderTest;
        _probe = probe;
        Shape = new SourceShape<T>(_out);
    }

    public override SourceShape<T> Shape { get; }

    public static Source<T, TMat> Create(GraphStageWithMaterializedValue<SourceShape<T>, TMat> stageUnderTest,
        TestProbe probe)
    {
        return Source.FromGraph(new TestSourceStage<T, TMat>(stageUnderTest, probe));
    }

    public override ILogicAndMaterializedValue<TMat> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
    {
        _stageUnderTest.Shape.Outlet.Id = _out.Id;
        var logicAndMaterialized = _stageUnderTest.CreateLogicAndMaterializedValue(inheritedAttributes);
        var logic = logicAndMaterialized.Logic;

        var outHandler = (IOutHandler)logic.Handlers[_out.Id];
        logic.SetHandler(_out, () =>
        {
            _probe.Ref.Tell(GraphStageMessages.Pull.Instance);
            outHandler.OnPull();
        }, cause =>
        {
            _probe.Ref.Tell(GraphStageMessages.DownstreamFinish.Instance);
            outHandler.OnDownstreamFinish(cause);
        });

        return logicAndMaterialized;
    }
}