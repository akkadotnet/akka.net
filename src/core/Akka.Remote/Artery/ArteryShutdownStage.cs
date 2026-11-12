// -----------------------------------------------------------------------
// <copyright file="ArteryShutdownStage.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>Appends one data-stream marker after its backlog; control markers use their FIFO queue.</summary>
    internal sealed class ArteryShutdownStage : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
    {
        private readonly ArteryShutdownDrain _drain;
        private readonly Association _association;
        private readonly ArteryStreamId _role;
        private readonly Func<ArteryDrainRequest, ReadOnlySequence<byte>> _encode;
        private readonly Inlet<ReadOnlySequence<byte>> _in = new("ArteryShutdown.in");
        private readonly Outlet<ReadOnlySequence<byte>> _out = new("ArteryShutdown.out");

        internal ArteryShutdownStage(ArteryShutdownDrain drain, Association association, ArteryStreamId role,
            Func<ArteryDrainRequest, ReadOnlySequence<byte>> encode)
        {
            _drain = drain;
            _association = association;
            _role = role;
            _encode = encode;
            Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(_in, _out);
        }

        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryShutdownStage _stage;
            private readonly ArteryShutdownDrain.Target _target;
            private ReadOnlySequence<byte> _tail;
            private bool _hasTail;

            internal Logic(ArteryShutdownStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _target = stage._drain.Register(stage._association, stage._role);
                SetHandler(stage._in, this);
                SetHandler(stage._out, this);
            }

            public void OnPush() => Push(_stage._out, Grab(_stage._in));
            public void OnPull()
            {
                if (_hasTail)
                    EmitTail();
                else
                    Pull(_stage._in);
            }

            public void OnUpstreamFinish()
            {
                if (_stage._role == ArteryStreamId.Control || _target.Request is not { } request)
                {
                    CompleteStage();
                    return;
                }
                _tail = _stage._encode(request);
                _hasTail = true;
                if (IsAvailable(_stage._out))
                    EmitTail();
            }

            private void EmitTail()
            {
                var tail = _tail;
                _tail = default;
                _hasTail = false;
                Push(_stage._out, tail);
                CompleteStage();
            }

            public void OnUpstreamFailure(Exception exception) => FailStage(exception);
            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
            public override void PostStop()
            {
                _tail.DisposeOwnedSegments();
                _stage._drain.Remove(_target);
            }
        }
    }
}
