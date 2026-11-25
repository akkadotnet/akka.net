//-----------------------------------------------------------------------
// <copyright file="ArteryBatchEncodeStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Encode stage of the ORDINARY outbound stream that can hand several frames downstream as one
    /// element. While downstream is busy it keeps pulling upstream and holds up to
    /// <c>maxFrames</c> envelopes, still unencoded. On the next downstream pull it encodes held
    /// envelopes in FIFO order into one pooled buffer, stopping once <c>maxBytes</c> is
    /// reached, and pushes that. When downstream is already waiting, the single envelope that
    /// arrives is encoded and pushed at once, exactly as <see cref="ArteryEncodeStage"/> does, so
    /// batches only form while downstream backpressures and no latency is added.
    ///
    /// <para>
    /// Every envelope still passes, one at a time, through every stage upstream of this one (the
    /// kill switch, the test stage and <see cref="OutboundHandshakeStage"/>); this stage never reads
    /// the lane channel itself. It sits upstream of the inner connection restart, so envelopes it
    /// holds while a connection is re-established are pushed, in order, once the new connection
    /// pulls. If the stage stops, fails or is cancelled while holding envelopes, PostStop hands them,
    /// in order, to <c>onStopHolding</c>; the transport carries them over to the next
    /// materialization of the lane, which sends them before anything still in the channel.
    /// </para>
    /// </summary>
    internal sealed class ArteryBatchEncodeStage : GraphStage<FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>>>
    {
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly long _originUid;
        private readonly int _maxFrames;
        private readonly long _maxBytes;
        private readonly ArrayPool<byte>? _pool;
        private readonly Action<Queue<IOutboundEnvelope>> _onStopHolding;
        private readonly Akka.Actor.Address _localAddress;

        /// <param name="serialization">The sending actor system's serialization extension.</param>
        /// <param name="originUid">The sending system's UID, stamped into every encoded envelope's fixed header.</param>
        /// <param name="maxFrames">Most envelopes held, and so most frames in one pushed element.</param>
        /// <param name="maxBytes">No further frame is added to a pushed element once it holds this many bytes.</param>
        /// <param name="pool">Encode buffer pool; <see langword="null"/> means <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="onStopHolding">Invoked once from PostStop with the envelopes still held, oldest first, if any.</param>
        /// <param name="localAddress">The address a sender given as a path is formatted with.</param>
        public ArteryBatchEncodeStage(
            Akka.Serialization.Serialization serialization,
            long originUid,
            int maxFrames,
            long maxBytes,
            ArrayPool<byte>? pool,
            Action<Queue<IOutboundEnvelope>> onStopHolding,
            Akka.Actor.Address localAddress)
        {
            _serialization = serialization;
            _originUid = originUid;
            _maxFrames = maxFrames;
            _maxBytes = maxBytes;
            _pool = pool;
            _onStopHolding = onStopHolding;
            _localAddress = localAddress ?? throw new ArgumentNullException(nameof(localAddress));
            Shape = new FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>>(In, Out);
        }

        private Inlet<IOutboundEnvelope> In { get; } = new("ArteryBatchEncode.in");
        private Outlet<ReadOnlySequence<byte>> Out { get; } = new("ArteryBatchEncode.out");

        public override FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryBatchEncodeStage _stage;
            private readonly Queue<IOutboundEnvelope> _held;
            private Exception? _failure;

            // Created once: a lambda capturing the exception in PushBatch's catch would be allocated on
            // every PushBatch call, because the compiler creates its closure when the method is entered.
            private Action<Exception>? _failLater;

            public Logic(ArteryBatchEncodeStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _held = new Queue<IOutboundEnvelope>(stage._maxFrames);
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart()
            {
                _failLater = GetAsyncCallback<Exception>(FailStage);
                Pull(_stage.In);
            }

            public void OnPush()
            {
                _held.Enqueue(Grab(_stage.In));
                if (_failure is not null)
                    return;

                if (IsAvailable(_stage.Out))
                    PushBatch();
                else
                    PullIfRoom();
            }

            public void OnPull()
            {
                if (_held.Count > 0 && _failure is null)
                    PushBatch();
            }

            public void OnUpstreamFinish()
            {
                // Held envelopes are flushed by the following pulls; PushBatch completes the stage.
                if (_held.Count == 0 && _failure is null)
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            public override void PostStop()
            {
                if (_held.Count > 0)
                    _stage._onStopHolding(_held);
            }

            private void PushBatch()
            {
                // Every frame of the batch is encoded into one pooled buffer, so a batch allocates one
                // segment rather than one per frame.
                var encoded = 0;
                try
                {
                    do
                    {
                        Encode(_held.Dequeue(), append: encoded > 0);
                        encoded++;
                    }
                    while (_writer!.WrittenCount < _stage._maxBytes && _held.Count > 0);
                }
                catch (Exception e)
                {
                    // As in ArteryEncodeStage, an encode failure fails the stream and loses only the
                    // failing envelope: the frames encoded before it still go downstream, ahead of
                    // the failure, and PostStop carries over the envelopes after it. The stage fails
                    // on its interpreter's next turn, not here, because an envelope it already
                    // pulled may still be on its way and would be lost; OnPush holds it meanwhile.
                    // The failed encode has already cut the writer back to the frames before it.
                    if (encoded > 0)
                        Push(_stage.Out, _writer!.DetachSequence());
                    _failure = e;
                    _failLater!(e);
                    return;
                }

                // Ownership of the encoded buffer moves downstream with the pushed sequence.
                Push(_stage.Out, _writer.DetachSequence());

                if (!IsClosed(_stage.In))
                    PullIfRoom();
                else if (_held.Count == 0)
                    CompleteStage();
            }

            private void PullIfRoom()
            {
                if (_held.Count < _stage._maxFrames && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            // One writer for the life of the stage: a batch's first encode re-arms it, the rest append to it,
            // and the batch detaches its buffer into one segment that owns it.
            private Akka.Serialization.PooledPayloadWriter? _writer;

            private void Encode(IOutboundEnvelope envelope, bool append)
            {
                _writer = ArteryEnvelopeCodec.Encode(
                    _stage._serialization, _stage._originUid, envelope.SenderPath, envelope.RecipientPath,
                    envelope.Message, _stage._pool, envelope.SenderId, envelope.RecipientId, envelope.RecipientPathUtf8,
                    _writer, envelope.SenderActorPath, _stage._localAddress, append);
            }
        }
    }
}
