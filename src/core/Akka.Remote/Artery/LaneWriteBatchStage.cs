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
    ///
    /// <para>
    /// <b>Reachability.</b> This stage is only materialized by
    /// <see cref="ArteryRemoting.MaterializeOrdinaryOutboundWithLanes"/>, which
    /// <c>MaterializeOutbound</c> only calls when <c>akka.remote.artery.advanced.outbound-lanes</c>
    /// is greater than 1. The shipping default is <c>outbound-lanes = 1</c> (see <c>Remote.conf</c>,
    /// which itself says to raise it only after validating), so on every default deployment this
    /// stage -- and the <c>onDropped</c> held-bytes reporting below -- is never constructed at all.
    /// </para>
    /// </summary>
    internal sealed class LaneWriteBatchStage : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
    {
        private readonly long _maxBytes;
        private readonly Action<long>? _onDropped;

        /// <param name="maxBytes">The batching weight cap -- see <see cref="ArteryRemoting.LaneWriteBatchMaxBytes"/>.</param>
        /// <param name="onDropped">
        /// Invoked from <see cref="Logic.PostStop"/> with the total byte count still retained in the
        /// batch/pending buffer when this stage stops before ever pushing it downstream (a killed or
        /// failed materialization, e.g. the connection-restart tail settling for the last time), OR
        /// discarded earlier by <see cref="Logic.OnPush"/>'s catch (an append failure -- see its
        /// remarks; the count is folded into the same PostStop-time invocation, not called twice). By
        /// this point the data is already-ENCODED, merged-lane frame bytes -- it crossed
        /// <see cref="OutboundHandshakeStage"/> and <see cref="ArteryEncodeStage"/> long ago and the
        /// MergeHub upstream has already interleaved multiple lanes' output into one sequence -- so,
        /// unlike <see cref="IOutboundContext.ReturnUndelivered"/>, there is no single
        /// <see cref="IOutboundEnvelope"/> left to hand back to an association-owned channel. The
        /// caller's own job is only to make the loss VISIBLE (publish a <see cref="Akka.Event.Dropped"/>
        /// event) instead of the silent pooled-buffer reclaim this stage used to do alone. Defaults to
        /// a no-op so existing callers (and the lane-batching unit specs, which never drive a mid-batch
        /// stop) are unaffected.
        /// </param>
        public LaneWriteBatchStage(long maxBytes, Action<long>? onDropped = null)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            _maxBytes = maxBytes;
            _onDropped = onDropped;
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

            /// <summary>
            /// Bytes discarded by <see cref="OnPush"/>'s catch (an append failure) before
            /// <see cref="PostStop"/> ever runs -- folded into <see cref="PostStop"/>'s own count so
            /// <c>onDropped</c> is still invoked exactly once, from exactly one place, and this loss
            /// is not silently lost a second time.
            /// </summary>
            private long _lostBeforeStop;

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
                    // both views; owner disposal is idempotent and covers either location. Record the
                    // combined loss BEFORE disposing (same reasoning as PostStop: once
                    // DisposeOwnedSegments runs, Length is the only thing left worth reporting) so
                    // this discard -- an "ownerless frame segment" bug in
                    // ArteryRemoting.AppendFrameToBatch, should never happen, but per onDropped's
                    // remarks the caller's job is to make the loss VISIBLE even here -- is not silent.
                    // PostStop folds this into its own count rather than invoking onDropped here
                    // directly, so the callback still fires exactly once.
                    _lostBeforeStop += frame.Length + (_hasBatch ? _batch.Length : 0);
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
                // Account for the loss BEFORE disposing: once DisposeOwnedSegments runs, the byte
                // count is the only thing left worth reporting anyway, but computing it after would
                // invite a future edit to read a disposed segment's Length. Whatever the caller's
                // onDropped does (publish a Dropped event, log, both) it must not throw -- this is
                // teardown, and PostStop has nowhere to propagate an exception to.
                var lost = _lostBeforeStop + (_hasBatch ? _batch.Length : 0) + (_hasPending ? _pending.Length : 0);

                if (_hasBatch)
                    _batch.DisposeOwnedSegments();
                if (_hasPending)
                    _pending.DisposeOwnedSegments();

                if (lost > 0)
                    _stage._onDropped?.Invoke(lost);
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
