//-----------------------------------------------------------------------
// <copyright file="AkkaStreamsLogSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Streams.Tests;

public class AkkaStreamsLogSourceSpec : AkkaSpec
{
    public AkkaStreamsLogSourceSpec(ITestOutputHelper output) : base(output, "akka.loglevel=DEBUG")
    {
    }

    // create a custom Flow shape graph stage
    private class TestLogStage<T> : GraphStage<FlowShape<T, T>>
    {
        private readonly string _name;

        public TestLogStage(string name)
        {
            _name = name;
            Shape = new FlowShape<T, T>(In, Out);
        }

        public Inlet<T> In { get; } = new("LogStage.in");
        public Outlet<T> Out { get; } = new("LogStage.out");

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TestLogStage<T> _stage;
            
            public Logic(TestLogStage<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                var element = Grab(_stage.In);
                Log.Info($"Element: {element}");
                Push(_stage.Out, element);
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }
        }
    }
    

    [Fact]
    public async Task LogStage_should_log_elements_with_coherent_actorpath()
    {
        var probe = CreateTestProbe();
        var source = Source.From(Enumerable.Range(1, 5));
        var flow = Flow.Create<int>().Log("log")
            .To(Sink.ActorRef<int>(probe.Ref, "completed", ex => new Status.Failure(ex)));

        // create a probe and subscribe it to Debug level events
        var logProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(logProbe.Ref, typeof(Debug));
        

        source.RunWith(flow, Sys);

        await probe.ExpectMsgAsync(1);
        await probe.ExpectMsgAsync(2);
        await probe.ExpectMsgAsync(3);
        await probe.ExpectMsgAsync(4);
        await probe.ExpectMsgAsync(5);
        
        // check just the first log message
        var logMessage = logProbe.ExpectMsg<Debug>();
        logMessage.LogSource.Should().Contain("StreamSupervisor");
    }
    
    [Fact]
    public async Task CustomStage_should_log_elements_with_friendly_name()
    {
        var probe = CreateTestProbe();
        var source = Source.From(Enumerable.Range(1, 5));
        var flow = Flow.Create<int>().Via(new TestLogStage<int>("custom"))
            .To(Sink.ActorRef<int>(probe.Ref, "completed", ex => new Status.Failure(ex)));

        // create a probe and subscribe it to Debug level events
        var logProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(logProbe.Ref, typeof(Info));
        

        source.RunWith(flow, Sys);

        await probe.ExpectMsgAsync(1);
        await probe.ExpectMsgAsync(2);
        await probe.ExpectMsgAsync(3);
        await probe.ExpectMsgAsync(4);
        await probe.ExpectMsgAsync(5);
        
        // check just the first log message
        var logMessage = logProbe.ExpectMsg<Info>();
        logMessage.LogSource.Should().Contain("StreamSupervisor");
    }
}
