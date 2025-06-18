//-----------------------------------------------------------------------
// <copyright file="TransformProcessorTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    internal class TransformProcessorTest : AkkaIdentityProcessorVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override IProcessor<int?,int?> CreateIdentityProcessor(int bufferSize)
        {
            return Flow.Create<int?>()
                .Via(new Stage())
                .ToProcessor()
                .WithAttributes(Attributes.CreateInputBuffer(bufferSize / 2, bufferSize))
                .Run(System.Materializer());
        }

        private sealed class Stage : GraphStage<FlowShape<int?, int?>>
        {
            private class Logic: InAndOutGraphStageLogic
            {
                private readonly Stage _parent;
                public Logic(Stage parent) : base(parent.Shape)
                {
                    _parent = parent;
                    SetHandlers(_parent.In, _parent.Out, this);
                }
                
                public override void OnPush() => Push(_parent.Out, Grab(_parent.In));
                public override void OnPull() => Pull(_parent.In);
            }

            public Stage()
            {
                Shape = new FlowShape<int?, int?>(In, Out);
            }
            
            public readonly Inlet<int?> In = new("Stage.in");
            public readonly Outlet<int?> Out = new("Stage.out");
            public override FlowShape<int?, int?> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new Logic(this);
        }
    }
}
