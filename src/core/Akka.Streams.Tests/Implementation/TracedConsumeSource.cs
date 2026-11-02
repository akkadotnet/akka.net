//-----------------------------------------------------------------------
// <copyright file="TracedConsumeSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// A test source that pushes a single element while a producer "consume" span is the current
    /// <see cref="Activity"/>, arming the outgoing connection's SlotContext from that span. This
    /// mirrors an actor-callback source such as Akka.Streams.Kafka, which pushes from a consumer
    /// callback while a consume span is live — the scenario that motivated issues #8241 / #8243.
    /// </summary>
    internal sealed class TracedConsumeSource : GraphStage<SourceShape<int>>
    {
        private readonly ActivitySource _producer;
        private readonly int _value;
        private readonly TaskCompletionSource<string> _traceId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TracedConsumeSource(ActivitySource producer, int value)
        {
            _producer = producer;
            _value = value;
            Shape = new SourceShape<int>(Out);
        }

        public Outlet<int> Out { get; } = new("TracedConsumeSource.out");
        public override SourceShape<int> Shape { get; }

        /// <summary>The W3C trace id of the "consume" span the element was pushed under.</summary>
        public Task<string> TraceId => _traceId.Task;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly TracedConsumeSource _stage;
            private bool _pushed;

            public Logic(TracedConsumeSource stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                if (_pushed)
                {
                    CompleteStage();
                    return;
                }

                _pushed = true;
                using var span = _stage._producer.StartActivity("consume", ActivityKind.Consumer);
                _stage._traceId.TrySetResult(span?.TraceId.ToString());
                Push(_stage.Out, _stage._value);
            }
        }
    }
}
