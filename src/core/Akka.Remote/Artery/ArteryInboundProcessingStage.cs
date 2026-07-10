//-----------------------------------------------------------------------
// <copyright file="ArteryInboundProcessingStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Per-connection inbound processing for one accepted Artery TCP (ordinary-stream) connection:
    /// parses the 5-byte connection preamble (<see cref="ArteryConnectionHeader"/>) exactly once,
    /// then incrementally frames (<see cref="ArteryFrameParser"/>), decodes
    /// (<see cref="ArteryEnvelopeCodec"/>), and deserializes the payload of every subsequent frame,
    /// wrapping each in an <see cref="IInboundEnvelope"/> -- a control envelope
    /// (<see cref="IInboundEnvelope.IsControl"/> true, <see cref="IInboundEnvelope.RecipientPath"/>
    /// <see langword="null"/>) for a handshake message, or an ordinary envelope (recipient/sender
    /// paths resolved) for a user message -- see design.md "G2 staging" and "Decode order
    /// (structural, not an optimization)".
    ///
    /// <para>
    /// <b>Why one combined stage instead of separate preamble/framing/decode stages.</b> All three
    /// steps share one piece of state -- "has the preamble been consumed yet" -- and none of them
    /// need an <c>.Async()</c> boundary between them (design.md Decision 2 rule 1: keep framing+decode
    /// in a single fused island on the hot path). Splitting them into multiple
    /// <see cref="GraphStage{TShape}"/>s would add nothing but ceremony at G2. Only classification
    /// (control vs. ordinary) happens here; <see cref="InboundHandshakeStage"/> itself is NOT
    /// reimplemented or forked -- it is composed downstream, unmodified, over this stage's
    /// <see cref="IInboundEnvelope"/> output.
    /// </para>
    ///
    /// <para>
    /// <b>Accepted connection preambles.</b> All three stream ids --
    /// <see cref="ArteryStreamId.Ordinary"/>, <see cref="ArteryStreamId.Control"/>, and (task
    /// 10.2) <see cref="ArteryStreamId.Large"/> -- are accepted; routing downstream is by the
    /// decoded envelope's <see cref="IInboundEnvelope.IsControl"/> flag (message type), not by
    /// which physical connection carried it (every preamble feeds the identical inbound shape:
    /// framing -&gt; decode -&gt; deserialize -&gt; <see cref="InboundHandshakeStage"/> -&gt;
    /// dispatch). The one thing that DOES vary by preamble is the frame-size limit the parser
    /// enforces: a <see cref="ArteryStreamId.Large"/> connection uses <see cref="MaxLargeFrameLength"/>
    /// instead of <see cref="MaxFrameLength"/> -- see <see cref="Logic.TryConsumePreamble"/>, which
    /// defers constructing the frame parser until the preamble reveals which one applies.
    /// </para>
    ///
    /// <para>
    /// <b>Per-frame error isolation.</b> A malformed ENVELOPE or an underlying serializer exception
    /// for a single frame is logged and that frame is dropped; the connection remains live (mirrors
    /// classic remoting's <c>EndpointReader</c> "Transient error ... association remains live" handling
    /// of deserialization failures). A framing-level problem (e.g. an oversized declared frame length --
    /// <see cref="ArteryFramingException"/> from <see cref="ArteryFrameParser.TryReadFrame"/>) is NOT
    /// caught here and is left to fail the stage/connection, since it indicates the peer is not
    /// speaking the protocol correctly.
    /// </para>
    /// </summary>
    internal sealed class ArteryInboundProcessingStage : GraphStage<FlowShape<ReadOnlySequence<byte>, IInboundEnvelope>>
    {
        /// <param name="maxFrameLength">
        /// Frame-size limit for connections whose preamble declares <see cref="ArteryStreamId.Ordinary"/>
        /// or <see cref="ArteryStreamId.Control"/> (they share one limit, matching Pekko's
        /// <c>maximum-frame-size</c>).
        /// </param>
        /// <param name="maxLargeFrameLength">
        /// Frame-size limit for a connection whose preamble declares <see cref="ArteryStreamId.Large"/>
        /// (task 10.2) -- matches Pekko's <c>maximum-large-frame-size</c>.
        /// </param>
        /// <param name="serialization">The receiving actor system's <see cref="Akka.Serialization.Serialization"/> extension.</param>
        public ArteryInboundProcessingStage(int maxFrameLength, int maxLargeFrameLength, Akka.Serialization.Serialization serialization)
        {
            MaxFrameLength = maxFrameLength;
            MaxLargeFrameLength = maxLargeFrameLength;
            Serialization = serialization;
            Shape = new FlowShape<ReadOnlySequence<byte>, IInboundEnvelope>(In, Out);
        }

        public int MaxFrameLength { get; }

        /// <summary>Frame-size limit applied ONLY to a connection whose preamble declares <see cref="ArteryStreamId.Large"/> (task 10.2).</summary>
        public int MaxLargeFrameLength { get; }

        public Akka.Serialization.Serialization Serialization { get; }

        public Inlet<ReadOnlySequence<byte>> In { get; } = new("ArteryInboundProcessing.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("ArteryInboundProcessing.out");

        public override FlowShape<ReadOnlySequence<byte>, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryInboundProcessingStage _stage;
            private readonly Queue<IInboundEnvelope> _pending = new();

            private readonly byte[] _preambleBuffer = new byte[ArteryConnectionHeader.Length];
            private int _preambleFilled;
            private bool _preambleParsed;

            /// <summary>
            /// Deliberately NOT constructed until <see cref="TryConsumePreamble"/> has parsed the
            /// preamble (task 10.2): which of <see cref="ArteryInboundProcessingStage.MaxFrameLength"/>/
            /// <see cref="ArteryInboundProcessingStage.MaxLargeFrameLength"/> applies is only known
            /// once the connection's declared stream id is known. Always non-null by the time
            /// <see cref="AppendToParser"/>/<see cref="DrainReadyFrames"/> run -- <see cref="OnPush"/>
            /// always calls <see cref="TryConsumePreamble"/> first (and returns early if it hasn't
            /// finished) before either method is ever reached.
            /// </summary>
            private ArteryFrameParser? _frameParser;

            public Logic(ArteryInboundProcessingStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var chunk = Grab(_stage.In);

                if (!_preambleParsed)
                {
                    if (!TryConsumePreamble(ref chunk))
                        return; // either more input needed (already re-pulled), or the stage failed.
                }

                AppendToParser(chunk);
                DrainReadyFrames();
                DeliverOrPull();
            }

            public void OnPull()
            {
                if (_pending.Count > 0)
                {
                    Push(_stage.Out, _pending.Dequeue());
                }
                else if (IsClosed(_stage.In))
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(_stage.In))
                {
                    Pull(_stage.In);
                }
            }

            public void OnUpstreamFinish()
            {
                if (_pending.Count == 0)
                    CompleteStage();

                // else: swallow the termination and let OnPull drain `_pending`, completing once empty.
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            /// <summary>
            /// Consumes as much of the connection preamble as <paramref name="chunk"/> can supply.
            /// Returns <see langword="false"/> if the caller should stop processing this push (either
            /// because more input is needed -- already re-pulled -- or because the stage just failed
            /// on an unsupported stream id). Returns <see langword="true"/> once the preamble is fully
            /// consumed, with <paramref name="chunk"/> narrowed to only the bytes AFTER it.
            /// </summary>
            private bool TryConsumePreamble(ref ReadOnlySequence<byte> chunk)
            {
                var needed = ArteryConnectionHeader.Length - _preambleFilled;
                var take = (int)Math.Min(needed, chunk.Length);
                if (take > 0)
                {
                    chunk.Slice(0, take).CopyTo(_preambleBuffer.AsSpan(_preambleFilled));
                    _preambleFilled += take;
                    chunk = chunk.Slice(take);
                }

                if (_preambleFilled < ArteryConnectionHeader.Length)
                {
                    if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                        Pull(_stage.In);
                    return false;
                }

                ArteryConnectionHeader.TryParse(new ReadOnlySequence<byte>(_preambleBuffer), out var streamId, out _);

                if (streamId != ArteryStreamId.Ordinary && streamId != ArteryStreamId.Control && streamId != ArteryStreamId.Large)
                {
                    // Defensive only -- ArteryConnectionHeader.TryParse already throws
                    // ArteryFramingException for any byte value outside {1, 2, 3}, so every
                    // ArteryStreamId value it CAN return is accepted above. Kept as a backstop in
                    // case a future stream id is added to the enum without updating this stage.
                    Log.Warning("Dropping inbound Artery connection: preamble declared unsupported stream id [{0}].", streamId);
                    FailStage(new ArteryFramingException($"Unsupported Artery connection stream id [{streamId}]."));
                    return false;
                }

                // The frame-size limit depends on which stream this connection carries (task
                // 10.2) -- construct the parser only now that the preamble has revealed it.
                _frameParser = streamId == ArteryStreamId.Large
                    ? new ArteryFrameParser(_stage.MaxLargeFrameLength)
                    : new ArteryFrameParser(_stage.MaxFrameLength);

                _preambleParsed = true;
                return true;
            }

            private void AppendToParser(ReadOnlySequence<byte> data)
            {
                if (data.IsEmpty)
                    return;

                // Non-null by construction here -- see _frameParser's remarks: OnPush always
                // resolves the preamble (and thus constructs the parser) before ever reaching this
                // call.
                var frameParser = _frameParser!;

                if (data.IsSingleSegment)
                {
                    frameParser.Append(data.First);
                    return;
                }

                foreach (var segment in data)
                    frameParser.Append(segment);
            }

            private void DrainReadyFrames()
            {
                while (_frameParser!.TryReadFrame(out var frameBody))
                {
                    IInboundEnvelope? element;
                    try
                    {
                        element = DecodeFrame(frameBody);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Transient error decoding an inbound Artery frame; connection remains live.");
                        continue;
                    }

                    if (element is not null)
                        _pending.Enqueue(element);
                }
            }

            private IInboundEnvelope? DecodeFrame(ReadOnlySequence<byte> frameBody)
            {
                var decoded = ArteryEnvelopeCodec.Decode(frameBody);

                if (!decoded.TryGetManifest(out var manifest))
                {
                    Log.Warning("Dropping inbound Artery frame: COMPRESSED manifest tag (ref/manifest compression is not implemented at G2).");
                    return null;
                }

                var payload = _stage.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, manifest);

                if (payload is IArteryControlMessage)
                    return new InboundEnvelope(payload, null, null, decoded.Header.OriginUid, decoded.Header.SerializerId, manifest);

                if (!decoded.TryGetRecipientPath(out var recipientPath))
                {
                    Log.Warning(
                        "Dropping inbound ordinary-stream message of type [{0}]: COMPRESSED recipient tag " +
                        "(ref compression is not implemented at G2).", payload.GetType());
                    return null;
                }

                if (recipientPath is null)
                {
                    Log.Warning(
                        "Dropping inbound ordinary-stream message of type [{0}] with no recipient.", payload.GetType());
                    return null;
                }

                var senderPath = decoded.TryGetSenderPath(out var s) ? s : null;
                return new InboundEnvelope(payload, senderPath, recipientPath, decoded.Header.OriginUid, decoded.Header.SerializerId, manifest);
            }

            private void DeliverOrPull()
            {
                if (_pending.Count > 0)
                {
                    if (IsAvailable(_stage.Out))
                        Push(_stage.Out, _pending.Dequeue());

                    return;
                }

                if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }
        }
    }
}
