//-----------------------------------------------------------------------
// <copyright file="LaneWriteBatchStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// Batches encoded lane frames while downstream is backpressuring and explicitly returns every
    /// retained pooled owner when the stage stops before a batch can be pushed.
    /// </summary>
    internal sealed class LaneWriteBatchStage : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
    {
        private readonly long _maxBytes;

        public LaneWriteBatchStage(long maxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            _maxBytes = maxBytes;
            Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(In, Out);
        }

        private Inlet<ReadOnlySequence<byte>> In { get; } = new("LaneWriteBatch.in");
        private Outlet<ReadOnlySequence<byte>> Out { get; } = new("LaneWriteBatch.out");

        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly LaneWriteBatchStage _stage;
            private ReadOnlySequence<byte> _batch;
            private ReadOnlySequence<byte> _pending;
            private long _batchBytes;
            private bool _hasBatch;
            private bool _hasPending;
            private bool _upstreamFinished;

            public Logic(LaneWriteBatchStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart() => Pull(_stage.In);

            public void OnPush()
            {
                var frame = Grab(_stage.In);
                try
                {
                    if (!_hasBatch)
                        Seed(frame);
                    else if (_batchBytes + frame.Length <= _stage._maxBytes)
                    {
                        _batch = ArteryRemoting.AppendFrameToBatch(_batch, frame);
                        _batchBytes += frame.Length;
                    }
                    else
                    {
                        _pending = frame;
                        _hasPending = true;
                    }
                }
                catch
                {
                    // Append may have transferred some owners into _batch before failing. Dispose
                    // both views; owner disposal is idempotent and covers either location.
                    frame.DisposeOwnedSegments();
                    _batch.DisposeOwnedSegments();
                    _hasBatch = false;
                    throw;
                }

                if (IsAvailable(_stage.Out))
                    PushBatch();

                PullIfNeeded();
            }

            public void OnPull()
            {
                if (_hasBatch)
                    PushBatch();
                else if (_upstreamFinished)
                    CompleteStage();
                else
                    PullIfNeeded();
            }

            public void OnUpstreamFinish()
            {
                _upstreamFinished = true;
                if (!_hasBatch)
                    CompleteStage();
                else if (IsAvailable(_stage.Out))
                    PushBatch();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            public override void PostStop()
            {
                if (_hasBatch)
                    _batch.DisposeOwnedSegments();
                if (_hasPending)
                    _pending.DisposeOwnedSegments();
            }

            private void Seed(ReadOnlySequence<byte> frame)
            {
                _batch = frame;
                _batchBytes = frame.Length;
                _hasBatch = true;
            }

            private void PushBatch()
            {
                var output = _batch;
                _hasBatch = false;
                _batch = default;
                _batchBytes = 0;

                if (_hasPending)
                {
                    var pending = _pending;
                    _hasPending = false;
                    _pending = default;
                    Seed(pending);
                }

                // Ownership moves downstream before Push. PostStop therefore never disposes an
                // element already handed to the TCP connection stage.
                Push(_stage.Out, output);

                if (_upstreamFinished && !_hasBatch)
                    CompleteStage();
            }

            private void PullIfNeeded()
            {
                if (!_upstreamFinished && !_hasPending && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }
        }
    }
}
