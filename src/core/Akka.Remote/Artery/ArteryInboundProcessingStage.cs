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
using Akka.Remote.Artery.Compression;
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
    /// <b>Accepted connection preambles.</b> As of task group 6 ("Control Stream"), both
    /// <see cref="ArteryStreamId.Ordinary"/> and <see cref="ArteryStreamId.Control"/> connections
    /// are accepted -- routing downstream is by the decoded envelope's <see cref="IInboundEnvelope.IsControl"/>
    /// flag (message type), not by which physical connection carried it (both preambles feed the
    /// identical inbound shape: framing -&gt; decode -&gt; deserialize -&gt; <see cref="InboundHandshakeStage"/>
    /// -&gt; dispatch). A connection whose preamble declares <see cref="ArteryStreamId.Large"/> is
    /// logged and the connection is dropped (stage failure) -- the large stream lands at G7.
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
        public ArteryInboundProcessingStage(
            int maxFrameLength,
            Akka.Serialization.Serialization serialization,
            IInboundCompressions? compressions = null)
        {
            MaxFrameLength = maxFrameLength;
            Serialization = serialization;
            // DEFAULT: the disabled path. NoInboundCompressions never resolves a COMPRESSED tag, so an
            // unwired stage keeps dropping COMPRESSED tags with a warning exactly as before -- the live
            // per-origin InboundCompressions is threaded in by a later stage, not here.
            Compressions = compressions ?? NoInboundCompressions.Instance;
            Shape = new FlowShape<ReadOnlySequence<byte>, IInboundEnvelope>(In, Out);
        }

        public int MaxFrameLength { get; }
        public Akka.Serialization.Serialization Serialization { get; }

        /// <summary>
        /// Receiver-side compression coordinator that resolves COMPRESSED sender/recipient/manifest
        /// tags. Defaults to <see cref="NoInboundCompressions.Instance"/> (the off-by-default path,
        /// which resolves nothing and so drops every COMPRESSED tag with a warning).
        /// </summary>
        public IInboundCompressions Compressions { get; }

        public Inlet<ReadOnlySequence<byte>> In { get; } = new("ArteryInboundProcessing.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("ArteryInboundProcessing.out");

        public override FlowShape<ReadOnlySequence<byte>, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryInboundProcessingStage _stage;
            private readonly ArteryFrameParser _frameParser;
            private readonly Queue<IInboundEnvelope> _pending = new();

            private readonly byte[] _preambleBuffer = new byte[ArteryConnectionHeader.Length];
            private int _preambleFilled;
            private bool _preambleParsed;

            public Logic(ArteryInboundProcessingStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _frameParser = new ArteryFrameParser(stage.MaxFrameLength);
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

                if (streamId != ArteryStreamId.Ordinary && streamId != ArteryStreamId.Control)
                {
                    Log.Warning(
                        "Dropping inbound Artery connection: preamble declared stream id [{0}], but only " +
                        "Ordinary and Control are implemented at task group 6 (large lands at G7).", streamId);
                    FailStage(new ArteryFramingException(
                        $"Unsupported Artery connection stream id [{streamId}] (only Ordinary/Control are accepted)."));
                    return false;
                }

                _preambleParsed = true;
                return true;
            }

            private void AppendToParser(ReadOnlySequence<byte> data)
            {
                if (data.IsEmpty)
                    return;

                if (data.IsSingleSegment)
                {
                    _frameParser.Append(data.First);
                    return;
                }

                foreach (var segment in data)
                    _frameParser.Append(segment);
            }

            private void DrainReadyFrames()
            {
                while (_frameParser.TryReadFrame(out var frameBody))
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
                var originUid = decoded.Header.OriginUid;

                if (!TryResolveManifest(decoded, originUid, out var manifest))
                {
                    // MISS on a COMPRESSED manifest (unknown/stale table, or compression disabled):
                    // drop with a warning, don't fault the stream (design.md Decision 4).
                    Log.Warning(
                        "Dropping inbound Artery frame: unresolved COMPRESSED manifest tag from origin [{0}] " +
                        "(table version [{1}], index [{2}]).",
                        originUid, decoded.Header.ManifestTableVersion, decoded.ManifestCompressedIndex);
                    return null;
                }

                var payload = _stage.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, manifest);

                if (payload is IArteryControlMessage)
                    return new InboundEnvelope(payload, null, null, originUid, decoded.Header.SerializerId, manifest);

                if (!TryResolveRecipient(decoded, originUid, out var recipientPath))
                {
                    Log.Warning(
                        "Dropping inbound ordinary-stream message of type [{0}]: unresolved COMPRESSED recipient tag " +
                        "from origin [{1}] (table version [{2}], index [{3}]).",
                        payload.GetType(), originUid, decoded.Header.ActorRefTableVersion, decoded.RecipientCompressedIndex);
                    return null;
                }

                if (recipientPath is null)
                {
                    Log.Warning(
                        "Dropping inbound ordinary-stream message of type [{0}] with no recipient.", payload.GetType());
                    return null;
                }

                // A COMPRESSED sender that can't be resolved is NOT fatal -- the sender is optional, so
                // (as before) the message is delivered with a null sender rather than dropped.
                var senderPath = TryResolveSender(decoded, originUid, out var s) ? s : null;
                return new InboundEnvelope(payload, senderPath, recipientPath, originUid, decoded.Header.SerializerId, manifest);
            }

            private bool TryResolveManifest(in ArteryEnvelopeDecoded decoded, long originUid, out string manifest)
            {
                if (decoded.ManifestKind == ArteryTagKind.Compressed)
                    return _stage.Compressions.TryDecompressClassManifest(
                        originUid, decoded.Header.ManifestTableVersion, decoded.ManifestCompressedIndex, out manifest);

                return decoded.TryGetManifest(out manifest);
            }

            private bool TryResolveRecipient(in ArteryEnvelopeDecoded decoded, long originUid, out string? recipientPath)
            {
                if (decoded.RecipientKind == ArteryTagKind.Compressed)
                {
                    var resolved = _stage.Compressions.TryDecompressActorRef(
                        originUid, decoded.Header.ActorRefTableVersion, decoded.RecipientCompressedIndex, out var path);
                    recipientPath = resolved ? path : null;
                    return resolved;
                }

                return decoded.TryGetRecipientPath(out recipientPath);
            }

            private bool TryResolveSender(in ArteryEnvelopeDecoded decoded, long originUid, out string? senderPath)
            {
                if (decoded.SenderKind == ArteryTagKind.Compressed)
                {
                    var resolved = _stage.Compressions.TryDecompressActorRef(
                        originUid, decoded.Header.ActorRefTableVersion, decoded.SenderCompressedIndex, out var path);
                    senderPath = resolved ? path : null;
                    return resolved;
                }

                return decoded.TryGetSenderPath(out senderPath);
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
