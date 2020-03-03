//-----------------------------------------------------------------------
// <copyright file="ChasingEventsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class ChasingEventsSpec : AkkaSpec
    {
        public ChasingEventsSpec(ITestOutputHelper output = null) : base(output)
        {
            Materializer = Sys.Materializer(ActorMaterializerSettings.Create(Sys).WithFuzzingMode(false));
        }

        public ActorMaterializer Materializer { get; }


        [Fact]
        public void Event_chasing_must_propagate_cancel_if_enqueued_immediately_after_pull()
        {
            var upstream = this.CreatePublisherProbe<int>();

            Source.FromPublisher(upstream)
                .Via(new CancelInChasePull())
                .RunWith(Sink.Ignore<int>(), Materializer);

            upstream.SendNext(0);
            upstream.ExpectCancellation();
        }

        [Fact]
        public void Event_chasing_must_propagate_complete_if_enqueued_immediately_after_push()
        {
            var downstream = this.CreateSubscriberProbe<int>();

            Source.From(Enumerable.Range(1, 10))
                .Via(new CompleteInChasePush())
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.RequestNext(1);
            downstream.ExpectComplete();
        }
        
        [Fact]
        public void Event_chasing_must_propagate_failure_if_enqueued_immediately_after_push()
        {
            var downstream = this.CreateSubscriberProbe<int>();

            Source.From(Enumerable.Range(1, 10))
                .Via(new FailureInChasedPush())
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            downstream.RequestNext(1);
            downstream.ExpectError();
        }

        private sealed class CancelInChasePull : GraphStage<FlowShape<int, int>>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(CancelInChasePull stage) : base(stage.Shape)
                {
                    var first = true;

                    SetHandler(stage.In, onPush: () => Push(stage.Out, Grab(stage.In)));
                    SetHandler(stage.Out, onPull: () =>
                    {
                        Pull(stage.In);
                        if(!first)
                            Cancel(stage.In);
                        first = false;
                    });
                }
            }

            public CancelInChasePull()
            {
                Shape = new FlowShape<int, int>(In, Out);
            }

            public Inlet<int> In { get; } = new Inlet<int>("Propagate.in");

            public Outlet<int> Out { get; } = new Outlet<int>("Propagate.out");

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class CompleteInChasePush : GraphStage<FlowShape<int, int>>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(CompleteInChasePush stage) : base(stage.Shape)
                {
                    SetHandler(stage.In, onPush: () =>
                    {
                        Push(stage.Out, Grab(stage.In));
                        Complete(stage.Out);
                    });
                    SetHandler(stage.Out, onPull: () => Pull(stage.In));
                }
            }

            public CompleteInChasePush()
            {
                Shape = new FlowShape<int, int>(In, Out);
            }

            public Inlet<int> In { get; } = new Inlet<int>("Propagate.in");

            public Outlet<int> Out { get; } = new Outlet<int>("Propagate.out");

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class FailureInChasedPush : GraphStage<FlowShape<int, int>>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(FailureInChasedPush stage) : base(stage.Shape)
                {
                    SetHandler(stage.In, onPush: () =>
                    {
                        Push(stage.Out, Grab(stage.In));
                        Fail(stage.Out, new TestException("test failure"));
                    });
                    SetHandler(stage.Out, onPull: () => Pull(stage.In));
                }
            }

            public FailureInChasedPush()
            {
                Shape = new FlowShape<int, int>(In, Out);
            }

            public Inlet<int> In { get; } = new Inlet<int>("Propagate.in");

            public Outlet<int> Out { get; } = new Outlet<int>("Propagate.out");

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class ChasableSink : GraphStage<SinkShape<int>>
        {
            private sealed class Logic : GraphStageLogic
            {
                private readonly ChasableSink _stage;

                public Logic(ChasableSink stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.In, onPush: ()=> Pull(stage.In));
                }

                public override void PreStart() => Pull(_stage.In);
            }

            public ChasableSink()
            {
                Shape = new SinkShape<int>(In);
            }

            public Inlet<int> In { get; } = new Inlet<int>("Chaseable.in");

            public override SinkShape<int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}
