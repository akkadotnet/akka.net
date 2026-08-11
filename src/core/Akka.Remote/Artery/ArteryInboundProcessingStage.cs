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
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
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
    ///
    /// <para>
    /// <b>Inbound lanes (only when <see cref="InboundLanes"/> &gt; 1 AND the connection's own
    /// preamble declares <see cref="ArteryStreamId.Ordinary"/>).</b> Lanes parallelize WITHIN one
    /// accepted connection: framing + header decode ALWAYS stay fused here (cheap, and required to
    /// even know which lane a frame belongs to), but the EXPENSIVE part of processing an ordinary
    /// message -- <see cref="Akka.Serialization.Serialization.Deserialize(System.Buffers.ReadOnlySequence{byte},int,string)"/>,
    /// <c>RemoteActorRefProvider.ResolveActorRefWithLocalAddress</c>, and the final
    /// <c>Tell</c> -- moves onto one of <see cref="InboundLanes"/> dedicated background consumer
    /// loops, each fed by its own bounded <see cref="Channel{T}"/> (capacity
    /// <see cref="InboundLaneBufferSize"/>). A message's lane is a stable hash of its recipient path
    /// (<c>Logic.LaneFor</c>) -- so all ordinary traffic to the SAME recipient stays on the SAME
    /// lane and is delivered in send order, exactly like today's single-lane pipeline.
    /// </para>
    ///
    /// <para>
    /// <b>Why control/large connections and lanes=1 never touch this machinery.</b> An
    /// <see cref="ArteryStreamId.Control"/> connection carries handshake/heartbeat/quarantine-notice/
    /// reliable-system-message traffic (see <c>ArteryRemoting.MaterializeOutboundStream</c>'s
    /// remarks: every control-classified message is enqueued via <c>EnqueueControl</c>/
    /// <c>EnqueueSystemMessage</c>, which ALWAYS materializes the CONTROL outbound stream -- never
    /// the ordinary one), so an <see cref="ArteryStreamId.Ordinary"/> connection never carries a
    /// control-classified envelope in practice. Lane routing is therefore gated on the connection's
    /// OWN preamble (known the moment <see cref="Logic.TryConsumePreamble"/> parses it), not on a
    /// per-frame runtime check -- Control and Large connections always use the exact pre-lanes
    /// decode-and-deserialize-inline path, UNCHANGED, regardless of <see cref="InboundLanes"/>. A
    /// defensive SerializerId check (<see cref="ControlMessageSerializerId"/>) still catches a
    /// control-classified frame arriving on an Ordinary connection (should never happen) and routes
    /// it through the SAME inline control path instead of a lane, so a protocol violation degrades
    /// safely rather than being silently misrouted.
    /// </para>
    ///
    /// <para>
    /// <b>ActorSelectionMessage lane key (design investigation).</b> <c>ArteryRemoting.Send</c>
    /// resolves an <c>ActorSelectionMessage</c>'s wire <c>RecipientPath</c> from the SELECTION'S
    /// ANCHOR ref, not its final target -- and <c>ActorRefFactoryShared.ActorSelection(ActorPath)</c>
    /// anchors every selection into a given remote system at that system's ROOT GUARDIAN
    /// (<c>RemoteActorRefProvider.RootGuardianAt</c>). So EVERY <c>ActorSelectionMessage</c> sent to
    /// ANY actor on a given remote system carries the exact SAME <c>RecipientPath</c> string --
    /// hashing it directly would collapse every inbound selection from that peer onto ONE lane,
    /// regardless of <see cref="InboundLanes"/>. <see cref="Logic.BuildSelectionLaneKey"/> instead
    /// parses just the OUTER <c>SelectionEnvelope</c> wire wrapper (never the wrapped application
    /// message it carries -- that stays fully deferred to the lane, like every other ordinary
    /// message) to recover the selection's OWN target path elements and hashes THOSE, so distinct
    /// selection targets land on distinct lanes. Falls back to <c>RecipientPath</c> (same-lane, but
    /// never WRONG) if the wrapper cannot be parsed.
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
        /// <param name="inboundLanes">
        /// Number of inbound lanes ordinary-stream messages are fanned out across (see the
        /// type-level "Inbound lanes" remarks). <see langword="1"/> (the default -- and the
        /// production default at <c>akka.remote.artery.advanced.inbound-lanes</c>) means the lane
        /// machinery below is NEVER materialized: every connection uses the exact pre-lanes
        /// fused decode-and-deserialize-inline path, regardless of which stream it turns out to
        /// carry. GATE: passing anything &gt; 1 REQUIRES <paramref name="inboundContext"/> and
        /// <paramref name="dispatchOrdinary"/> to be supplied.
        /// </param>
        /// <param name="inboundLaneBufferSize">
        /// Capacity of each lane's bounded <see cref="Channel{T}"/>, when <paramref name="inboundLanes"/>
        /// &gt; 1. Ignored (and may be 0) at the default <paramref name="inboundLanes"/> of 1.
        /// </param>
        /// <param name="inboundContext">
        /// Necessary only when <paramref name="inboundLanes"/> is more than 1.
        ///
        /// <para>
        /// The ordinary traffic of such a connection goes to the lanes. It does not go through
        /// <see cref="InboundHandshakeStage"/> or <see cref="InboundQuarantineCheckStage"/>. The
        /// type-level remarks tell why those two stages do nothing for such a connection.
        /// </para>
        ///
        /// <para>
        /// The lane path must do the checks of those two stages itself. It calls
        /// <see cref="IInboundContext.IsKnownOrigin"/> and
        /// <see cref="IInboundContext.IsQuarantined"/> for the checks, and
        /// <see cref="IInboundContext.SendControl"/> to send the quarantine notice.
        /// </para>
        /// </param>
        /// <param name="dispatchOrdinary">
        /// Needed ONLY when <paramref name="inboundLanes"/> &gt; 1: invoked by each lane's consumer
        /// loop, once per deserialized ordinary message, as <c>(message, senderPath, recipientPath)</c>
        /// -- mirrors <c>ArteryRemoting.DispatchOrdinaryMessage</c> exactly (the same resolve-and-Tell
        /// logic <c>ArteryRemoting.DispatchInbound</c> uses for the lanes=1/non-lane path).
        /// </param>
        /// <param name="onLanesInitialized">
        /// Test-observability hook (see <see cref="ArteryTransportSetup.OnInboundLanesInitialized"/>):
        /// invoked once, with the actual lane count, the moment this connection's lane machinery is
        /// materialized. <see langword="null"/> (the default, and the only production value) does
        /// nothing. Never invoked when <paramref name="inboundLanes"/> is 1 (lanes are never
        /// materialized then) or for a Control/Large-stream connection (lanes are Ordinary-only).
        /// </param>
        /// <param name="testState">
        /// Failure-injection blackhole state for the lanes&gt;1 lane path
        /// (<c>akka.remote.artery.advanced.test-mode</c>) -- see <see cref="TestState"/>.
        /// <see langword="null"/> (the default, and the only value when test-mode is off) disables
        /// the lane-path blackhole check entirely.
        /// </param>
        public ArteryInboundProcessingStage(
            int maxFrameLength,
            int maxLargeFrameLength,
            Akka.Serialization.Serialization serialization,
            int inboundLanes = 1,
            int inboundLaneBufferSize = 0,
            IInboundContext? inboundContext = null,
            Action<object, string?, string>? dispatchOrdinary = null,
            Action<int>? onLanesInitialized = null,
            SharedTestState? testState = null)
        {
            if (inboundLanes < 1)
                throw new ArgumentOutOfRangeException(nameof(inboundLanes), inboundLanes, "Must be >= 1.");
            if (inboundLanes > 1 && (inboundContext is null || dispatchOrdinary is null))
                throw new ArgumentException(
                    $"{nameof(inboundContext)} and {nameof(dispatchOrdinary)} are required when {nameof(inboundLanes)} > 1.");

            MaxFrameLength = maxFrameLength;
            MaxLargeFrameLength = maxLargeFrameLength;
            Serialization = serialization;
            InboundLanes = inboundLanes;
            InboundLaneBufferSize = inboundLaneBufferSize;
            InboundContext = inboundContext;
            DispatchOrdinary = dispatchOrdinary;
            OnLanesInitialized = onLanesInitialized;
            TestState = testState;

            // Resolved ONCE per accepted connection (this stage is materialized per-connection),
            // never on the per-frame hot path -- and only when lanes are actually enabled, since
            // this is pure overhead at the InboundLanes=1 default. A miss (no binding found, which
            // should never happen for these built-in types) resolves to a sentinel that can never
            // match a real wire SerializerId, so the defensive checks below simply never trigger
            // rather than throwing.
            //
            // LOAD-BEARING INVARIANT: lane mode classifies a frame as "control" by comparing its
            // wire SerializerId to ControlMessageSerializerId (see ProcessFrameLaneMode) -- this is
            // equivalent to the non-lane path's `payload is IArteryControlMessage` check (see
            // DecodeFrame) ONLY as long as (a) every IArteryControlMessage type is serialized by
            // ArteryControlMessageSerializer and (b) nothing else is ever bound to that same
            // SerializerId. Both hold today (ArteryControlMessageSerializer is the sole serializer
            // registered for all nine control message types). If that ever changes -- e.g. a new
            // IArteryControlMessage type gets its own dedicated serializer, or
            // ArteryControlMessageSerializer starts handling a non-control type too -- this
            // SerializerId-based check and ProcessFrameLaneMode's classification must be updated
            // together, or lane mode will silently stop treating IsControl the same way the
            // non-lane path does for the affected type.
            if (inboundLanes > 1)
            {
                ControlMessageSerializerId = serialization.FindSerializerForType(typeof(HandshakeReq))?.Identifier ?? int.MinValue;
                SelectionMessageSerializerId = serialization.FindSerializerForType(typeof(ActorSelectionMessage))?.Identifier ?? int.MinValue;
            }
            else
            {
                ControlMessageSerializerId = int.MinValue;
                SelectionMessageSerializerId = int.MinValue;
            }

            Shape = new FlowShape<ReadOnlySequence<byte>, IInboundEnvelope>(In, Out);
        }

        public int MaxFrameLength { get; }

        /// <summary>Frame-size limit applied ONLY to a connection whose preamble declares <see cref="ArteryStreamId.Large"/> (task 10.2).</summary>
        public int MaxLargeFrameLength { get; }

        public Akka.Serialization.Serialization Serialization { get; }

        /// <summary>See the constructor parameter of the same name.</summary>
        public int InboundLanes { get; }

        /// <summary>See the constructor parameter of the same name.</summary>
        public int InboundLaneBufferSize { get; }

        /// <summary>See the constructor parameter of the same name. <see langword="null"/> when <see cref="InboundLanes"/> is 1.</summary>
        public IInboundContext? InboundContext { get; }

        /// <summary>See the constructor parameter of the same name. <see langword="null"/> when <see cref="InboundLanes"/> is 1.</summary>
        public Action<object, string?, string>? DispatchOrdinary { get; }

        /// <summary>See the constructor parameter of the same name.</summary>
        public Action<int>? OnLanesInitialized { get; }

        /// <summary>
        /// Failure-injection blackhole state (<c>akka.remote.artery.advanced.test-mode</c>) for
        /// the lanes&gt;1 lane path -- lane-routed ordinary traffic bypasses the connection sink's
        /// <see cref="InboundTestStage"/>, so the SAME blackhole check runs inside
        /// <c>ProcessFrameLaneMode</c> instead (right after its existing
        /// <see cref="IInboundContext.IsKnownOrigin"/> gate; unknown-origin lane traffic is
        /// already dropped by that gate regardless of test-mode). <see langword="null"/> (the
        /// default, and the only value at test-mode off OR at the <see cref="InboundLanes"/>=1
        /// default, where the lane path never runs at all) disables the check -- the single
        /// nullable-field test it leaves on the lanes&gt;1 frame path is the unavoidable minimum,
        /// since a stage-internal consumer loop has no stream seam to conditionally insert a
        /// stage into.
        /// </summary>
        public SharedTestState? TestState { get; }

        /// <summary>
        /// The shared <see cref="ArteryControlMessageSerializer"/>'s SerializerId (probed via any
        /// one of the nine message types it handles) -- used ONLY as a defensive per-frame
        /// classification check when <see cref="InboundLanes"/> &gt; 1 (see the type-level "Why
        /// control/large connections and lanes=1 never touch this machinery" remarks). Never
        /// resolved (stays a sentinel that matches nothing) at the <see cref="InboundLanes"/>=1
        /// default.
        /// </summary>
        public int ControlMessageSerializerId { get; }

        /// <summary>
        /// <see cref="Akka.Actor.ActorSelectionMessage"/>'s SerializerId -- used ONLY to decide
        /// whether a frame needs the <see cref="Logic.BuildSelectionLaneKey"/> special case (see
        /// the type-level "ActorSelectionMessage lane key" remarks). Never resolved at the
        /// <see cref="InboundLanes"/>=1 default.
        /// </summary>
        public int SelectionMessageSerializerId { get; }

        public Inlet<ReadOnlySequence<byte>> In { get; } = new("ArteryInboundProcessing.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("ArteryInboundProcessing.out");

        public override FlowShape<ReadOnlySequence<byte>, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// One decoded-but-not-yet-deserialized ordinary message, handed from the fused
        /// per-connection stage to a lane's consumer loop. Carries the RAW payload bytes (a
        /// <see cref="ReadOnlySequence{T}"/> slice into a freshly-allocated, never-pooled,
        /// never-mutated <c>byte[]</c> owned by that one TCP read -- see
        /// <c>Akka.IO.TcpConnection.ReadPipeChunkAsync</c>, which copies OUT of the pipe's own
        /// pooled segments before <c>AdvanceTo</c> for exactly this reason -- so it is always safe
        /// to retain and deserialize later, on a different thread, with no risk of the underlying
        /// bytes being reused/overwritten out from under the lane consumer) plus everything else
        /// the lane needs to finish the job without going back to the connection: the serializer
        /// id/manifest to deserialize with, and the already-resolved sender/recipient wire paths.
        /// </summary>
        private readonly struct LaneWorkItem
        {
            public LaneWorkItem(ReadOnlySequence<byte> payload, int serializerId, string manifest, string? senderPath, string recipientPath)
            {
                Payload = payload;
                SerializerId = serializerId;
                Manifest = manifest;
                SenderPath = senderPath;
                RecipientPath = recipientPath;
            }

            public ReadOnlySequence<byte> Payload { get; }
            public int SerializerId { get; }
            public string Manifest { get; }
            public string? SenderPath { get; }
            public string RecipientPath { get; }
        }

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

            /// <summary>
            /// <see langword="true"/> only for a connection whose OWN preamble declared
            /// <see cref="ArteryStreamId.Ordinary"/> AND <see cref="ArteryInboundProcessingStage.InboundLanes"/>
            /// &gt; 1 -- set (once) at the end of <see cref="TryConsumePreamble"/>, never afterwards.
            /// Every other connection (Control, Large, or ANY connection when InboundLanes is 1)
            /// leaves this <see langword="false"/> for its entire lifetime, so
            /// <see cref="OnPush"/> always takes the untouched pre-lanes <see cref="DrainReadyFrames"/>
            /// path for it -- see the type-level "Why control/large connections and lanes=1 never
            /// touch this machinery" remarks.
            /// </summary>
            private bool _laneModeActive;

            /// <summary>One bounded channel per lane; created once, lazily, by <see cref="InitLanes"/>.</summary>
            private Channel<LaneWorkItem>[]? _lanes;

            /// <summary>Captured once (interpreter thread, at lane init) so lane consumer loops never touch the stage's lazily-initialized <c>Log</c> from a background thread.</summary>
            private ILoggingAdapter? _laneLog;

            // Backpressure-parking state (standard async-GraphStage pattern -- mirrors
            // Akka.Streams.Implementation.ChannelSinkLogic's TryWrite/WaitToWriteAsync idiom). At
            // most ONE item can ever be parked at a time: DrainReadyFramesLaneMode is a plain
            // synchronous loop on the interpreter thread that stops the INSTANT a lane write would
            // block, so there is never more than one in-flight async write per connection.
            private LaneWorkItem _parkedItem;
            private int _parkedLaneIndex;
            private bool _writeInFlight;
            private Action<bool>? _onLaneWriteAvailable;
            private Action<Task<bool>>? _onLaneWriteReady;

            public Logic(ArteryInboundProcessingStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);

                if (_stage.InboundLanes > 1)
                {
                    _onLaneWriteAvailable = GetAsyncCallback<bool>(OnLaneWriteAvailable);
                    _onLaneWriteReady = t =>
                    {
                        if (t.IsFaulted)
                            _onLaneWriteAvailable!(false);
                        else if (t.IsCanceled)
                            _onLaneWriteAvailable!(false);
                        else
                            _onLaneWriteAvailable!(t.Result);
                    };
                }
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

                if (_laneModeActive)
                    DrainReadyFramesLaneMode();
                else
                    DrainReadyFrames();

                DeliverOrPull();
            }

            public void OnPull()
            {
                if (_pending.Count > 0)
                {
                    Push(_stage.Out, _pending.Dequeue());
                }
                else if (IsClosed(_stage.In) && !_writeInFlight)
                {
                    CompleteStage();
                }
                // !_writeInFlight guards this exactly like DeliverOrPull's matching guard: while
                // parked on lane backpressure, do NOT request more input from the connection, even
                // if downstream re-signals demand (e.g. after consuming an earlier, defensively
                // routed control envelope) -- otherwise the frame parser's own unbounded internal
                // buffer would grow without limit while we wait for lane capacity, defeating the
                // whole point of backpressure. Always false (no-op) at InboundLanes=1.
                else if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In) && !_writeInFlight)
                {
                    Pull(_stage.In);
                }
            }

            public void OnUpstreamFinish()
            {
                if (_pending.Count == 0 && !_writeInFlight)
                    CompleteStage();

                // else: swallow the termination and let OnPull (non-lane path) / OnLaneWriteAvailable
                // (lane path) drain the remainder, completing once both are empty/clear.
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            /// <summary>
            /// Lane channels are completed on EVERY termination path (graceful completion,
            /// failure, or downstream cancellation all funnel through <see cref="PostStop"/> --
            /// the universal <see cref="GraphStageLogic"/> teardown hook), so a lane's consumer
            /// loop always eventually observes <see cref="ChannelReader{T}.WaitToReadAsync"/>
            /// returning <see langword="false"/> once it has drained whatever was already
            /// buffered, and exits on its own -- no orphaned tasks, and no message that had
            /// already been handed to a lane is ever silently dropped by teardown itself
            /// (only a message still sitting unread upstream in the TCP pipe, which was never
            /// going to be delivered anyway once the connection is gone).
            /// </summary>
            public override void PostStop()
            {
                if (_lanes is not null)
                {
                    foreach (var lane in _lanes)
                        lane.Writer.TryComplete();
                }
            }

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

                // GATE A: only an Ordinary-stream connection, and only when lanes are actually
                // configured, ever enters lane mode -- every Control/Large connection, and EVERY
                // connection at the InboundLanes=1 default, is untouched from here on (see the
                // type-level remarks).
                if (streamId == ArteryStreamId.Ordinary && _stage.InboundLanes > 1)
                {
                    _laneModeActive = true;
                    InitLanes();
                }

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

                // Parked on lane backpressure -- do NOT request more input from the connection
                // until the lane frees up room (this IS the connection-level backpressure signal:
                // stop reading more off the TCP pipe while a decoded item is stuck waiting for its
                // lane).
                if (_writeInFlight)
                    return;

                if (!IsClosed(_stage.In) && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);
            }

            #region Lane mode (InboundLanes > 1, Ordinary-stream connections only)

            private void InitLanes()
            {
                var laneCount = _stage.InboundLanes;
                _laneLog = Log;
                var lanes = new Channel<LaneWorkItem>[laneCount];

                for (var i = 0; i < laneCount; i++)
                {
                    var channel = Channel.CreateBounded<LaneWorkItem>(new BoundedChannelOptions(_stage.InboundLaneBufferSize)
                    {
                        SingleWriter = true,
                        SingleReader = true,
                        AllowSynchronousContinuations = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });
                    lanes[i] = channel;

                    var reader = channel.Reader;
                    var log = _laneLog;
                    var serialization = _stage.Serialization;
                    var dispatch = _stage.DispatchOrdinary!;
                    _ = Task.Run(() => RunLaneConsumer(reader, serialization, dispatch, log));
                }

                _lanes = lanes;

                // Test-observability only (see ArteryTransportSetup.OnInboundLanesInitialized) --
                // null (the production default) does nothing.
                _stage.OnLanesInitialized?.Invoke(laneCount);
            }

            /// <summary>
            /// One lane's long-running consumer loop (design's benchmark-locked mechanism: one
            /// bounded <see cref="Channel{T}"/> per lane, one dedicated consumer loop per lane --
            /// runs on the default .NET ThreadPool via <see cref="Task.Run(Action)"/>, no dedicated
            /// dispatcher/thread). Awaits only BETWEEN messages
            /// (<see cref="ChannelReader{T}.WaitToReadAsync"/>/<see cref="ChannelReader{T}.TryRead"/>)
            /// -- <see cref="Akka.Serialization.Serialization.Deserialize(System.Buffers.ReadOnlySequence{byte},int,string)"/>
            /// is a plain synchronous call with NO internal <see langword="await"/>, so it always
            /// runs start-to-finish on whichever thread invokes it, which is exactly what keeps
            /// <see cref="Akka.Serialization.Serialization.CurrentTransportInformation"/> (a
            /// <c>[ThreadStatic]</c>) correct here even though different loop iterations may
            /// legitimately resume on different pool threads.
            /// </summary>
            private static async Task RunLaneConsumer(
                ChannelReader<LaneWorkItem> reader,
                Akka.Serialization.Serialization serialization,
                Action<object, string?, string> dispatch,
                ILoggingAdapter log)
            {
                try
                {
                    while (await reader.WaitToReadAsync().ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var item))
                        {
                            try
                            {
                                var message = serialization.Deserialize(item.Payload, item.SerializerId, item.Manifest);
                                dispatch(message, item.SenderPath, item.RecipientPath);
                            }
                            catch (Exception ex)
                            {
                                log.Warning(
                                    ex,
                                    "Transient error deserializing/dispatching an inbound Artery lane message " +
                                    "(serializer id [{0}], manifest [{1}]); connection remains live.",
                                    item.SerializerId, item.Manifest);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Inbound Artery lane consumer loop terminated unexpectedly.");
                }
            }

            private void DrainReadyFramesLaneMode()
            {
                if (_writeInFlight)
                    return;

                while (_frameParser!.TryReadFrame(out var frameBody))
                {
                    bool handled;
                    try
                    {
                        handled = ProcessFrameLaneMode(frameBody);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Transient error decoding an inbound Artery frame; connection remains live.");
                        continue;
                    }

                    if (!handled)
                        return; // parked -- stop draining further frames until lane capacity frees up.
                }
            }

            /// <summary>
            /// Decodes (never deserializes the application payload) one frame on an Ordinary-stream,
            /// lane-enabled connection, then either (a) routes a genuine control-classified frame
            /// (should never happen here -- defensive only, see the type-level remarks) through the
            /// SAME inline path <see cref="DecodeFrame"/> uses, unchanged, or (b) hashes its
            /// recipient (or, for a selection, its target path elements) to a lane and hands it off.
            /// Returns <see langword="false"/> only when (b) had to park on a full lane channel --
            /// the caller must stop draining further frames until that clears.
            /// </summary>
            private bool ProcessFrameLaneMode(ReadOnlySequence<byte> frameBody)
            {
                var decoded = ArteryEnvelopeCodec.Decode(frameBody);

                if (!decoded.TryGetManifest(out var manifest))
                {
                    Log.Warning("Dropping inbound Artery frame: COMPRESSED manifest tag (ref/manifest compression is not implemented at G2).");
                    return true;
                }

                if (decoded.Header.SerializerId == _stage.ControlMessageSerializerId)
                {
                    // Defensive only: a control-classified frame is not expected on an Ordinary
                    // connection (see the type-level remarks) -- deserialize INLINE exactly as the
                    // non-lane path does and push it through the ordinary Out pipeline so
                    // InboundHandshakeStage/SystemMessageAckerStage/DispatchInbound still see it.
                    var controlPayload = _stage.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, manifest);
                    _pending.Enqueue(new InboundEnvelope(controlPayload, null, null, decoded.Header.OriginUid, decoded.Header.SerializerId, manifest));
                    return true;
                }

                // Mirrors InboundHandshakeStage.IsKnownOrigin gating exactly (same shared-registry
                // method, reused rather than reimplemented) -- "handshake completes before
                // user-message dispatch per connection" holds for the lane path too.
                if (!_stage.InboundContext!.IsKnownOrigin(decoded.Header.OriginUid))
                {
                    Log.Debug(
                        "Dropping inbound lane-routed Artery message from unknown origin uid [{0}] (no completed handshake for this uid yet).",
                        decoded.Header.OriginUid);
                    return true;
                }

                // The quarantine check for the lane path. It does what InboundQuarantineCheckStage
                // does, because lane traffic does not go through the sink that holds that stage.
                // The checks from InboundHandshakeStage and InboundTestStage are inlined here for
                // the same reason.
                //
                // This code deserializes the message here, which lane mode otherwise leaves to the
                // lane consumer. Two reasons make this acceptable:
                //   - Only frames from a quarantined uid get this far, so a healthy connection
                //     never runs this code.
                //   - ShouldNotifyOrigin must examine the message to decide about the notice.
                if (_stage.InboundContext!.IsQuarantined(decoded.Header.OriginUid))
                {
                    var quarantinedOrigin = _stage.InboundContext!.TryResolveOriginAddress(decoded.Header.OriginUid);
                    object quarantinedMessage;
                    try
                    {
                        quarantinedMessage = _stage.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, manifest);
                    }
                    catch (Exception ex)
                    {
                        // The code discards the frame in all conditions. A deserialization failure
                        // only prevents the notice for this one frame. The next discarded frame
                        // sends it.
                        Log.Debug(
                            ex,
                            "Dropping message (serializer id [{0}]) from [{1}#{2}] because the system is quarantined (payload not deserializable).",
                            decoded.Header.SerializerId, quarantinedOrigin, decoded.Header.OriginUid);
                        return true;
                    }

                    Log.Debug(
                        "Dropping message [{0}] from [{1}#{2}] because the system is quarantined",
                        quarantinedMessage.GetType(), quarantinedOrigin, decoded.Header.OriginUid);

                    if (InboundQuarantineCheckStage.ShouldNotifyOrigin(quarantinedMessage) && quarantinedOrigin is not null)
                    {
                        _stage.InboundContext!.SendControl(
                            quarantinedOrigin,
                            new ArteryQuarantined(_stage.InboundContext!.LocalAddress, decoded.Header.OriginUid));
                    }

                    return true;
                }

                // advanced.test-mode failure injection for the lane path (lane-routed traffic
                // bypasses the connection sink's InboundTestStage) -- the same known-origin
                // blackhole check that stage performs, keyed identically as
                // (localAddress, originAddress). Unknown origins never reach here (dropped by the
                // IsKnownOrigin gate above, test-mode or not), so the InboundTestStage's
                // pre-handshake HandshakeReq special case has no lane-path analog. See
                // TestState's property remarks for the off-mode cost rationale.
                if (_stage.TestState is { } testState)
                {
                    var origin = _stage.InboundContext!.TryResolveOriginAddress(decoded.Header.OriginUid);
                    if (origin is not null &&
                        testState.IsBlackhole(_stage.InboundContext!.LocalAddress.Address, origin))
                    {
                        Log.Debug(
                            "dropping inbound lane-routed message from [{0}] with UID [{1}] because of blackhole",
                            origin, decoded.Header.OriginUid);
                        return true;
                    }
                }

                if (!decoded.TryGetRecipientPath(out var recipientPath))
                {
                    Log.Warning(
                        "Dropping inbound ordinary-stream message (serializer id [{0}]): COMPRESSED recipient tag " +
                        "(ref compression is not implemented at G2).", decoded.Header.SerializerId);
                    return true;
                }

                if (recipientPath is null)
                {
                    Log.Warning("Dropping inbound ordinary-stream message (serializer id [{0}]) with no recipient.", decoded.Header.SerializerId);
                    return true;
                }

                var senderPath = decoded.TryGetSenderPath(out var s) ? s : null;

                // ActorSelectionMessage special case -- see the type-level "ActorSelectionMessage
                // lane key" remarks.
                var laneKey = decoded.Header.SerializerId == _stage.SelectionMessageSerializerId
                    ? BuildSelectionLaneKey(decoded.Payload) ?? recipientPath
                    : recipientPath;

                var laneIndex = LaneFor(laneKey, _stage.InboundLanes);

                return TryRouteToLane(laneIndex, new LaneWorkItem(decoded.Payload, decoded.Header.SerializerId, manifest, senderPath, recipientPath));
            }

            /// <summary>
            /// Parses ONLY the outer <c>SelectionEnvelope</c> wire wrapper (<c>MessageContainerSerializer</c>'s
            /// format) to recover an <c>ActorSelectionMessage</c>'s OWN target path elements, WITHOUT
            /// deserializing the wrapped application message it carries (that stays fully deferred to
            /// the lane, exactly like any other ordinary message) -- see the type-level
            /// "ActorSelectionMessage lane key" remarks for why <c>RecipientPath</c> alone cannot be
            /// used here. Returns <see langword="null"/> (safe fallback to <c>RecipientPath</c>-based
            /// hashing -- same-lane, never WRONG) if the wrapper has no pattern elements or fails to
            /// parse for any reason.
            /// </summary>
            private static string? BuildSelectionLaneKey(in ReadOnlySequence<byte> payload)
            {
                try
                {
                    var bytes = payload.IsSingleSegment ? payload.FirstSpan.ToArray() : payload.ToArray();
                    var envelope = Akka.Remote.Serialization.Proto.Msg.SelectionEnvelope.Parser.ParseFrom(bytes);
                    if (envelope.Pattern.Count == 0)
                        return null;

                    var sb = new StringBuilder();
                    foreach (var element in envelope.Pattern)
                        sb.Append('/').Append(element.Matcher);

                    return sb.ToString();
                }
                catch
                {
                    return null;
                }
            }

            /// <summary>
            /// Deterministic FNV-1a hash of <paramref name="key"/>, unsigned-mod'd into
            /// <c>[0, lanes)</c>. Unsigned mod (not <see cref="Math.Abs(int)"/> on a signed hash)
            /// deliberately avoids the classic <c>Math.Abs(int.MinValue)</c> overflow trap (which
            /// returns <see cref="int.MinValue"/> unchanged, still negative).
            /// </summary>
            private static int LaneFor(string key, int lanes)
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                var hash = offsetBasis;
                foreach (var c in key)
                {
                    hash ^= c;
                    hash *= prime;
                }

                return (int)(hash % (uint)lanes);
            }

            /// <summary>
            /// Attempts to hand <paramref name="item"/> to lane <paramref name="laneIndex"/>'s
            /// channel. Returns <see langword="true"/> if it was written (or safely dropped because
            /// the lane's channel is already completing -- a teardown race, not a capacity problem).
            /// Returns <see langword="false"/> if the channel was full: the item is PARKED (never
            /// dropped) and re-attempted once <see cref="ChannelWriter{T}.WaitToWriteAsync"/>'s
            /// continuation fires (<see cref="OnLaneWriteAvailable"/>) -- the caller must stop
            /// draining further frames from this connection until then, which is exactly the
            /// backpressure signal design.md calls for (never block a stage thread; never drop).
            /// </summary>
            private bool TryRouteToLane(int laneIndex, LaneWorkItem item)
            {
                var writer = _lanes![laneIndex].Writer;
                if (writer.TryWrite(item))
                    return true;

                var continuation = writer.WaitToWriteAsync();
                if (continuation.IsCompletedSuccessfully)
                {
                    var available = continuation.GetAwaiter().GetResult();
                    if (available && writer.TryWrite(item))
                        return true;

                    // Either the channel was completed underneath us (available == false -- a
                    // teardown race) or another writer already took the freed slot -- SingleWriter
                    // means the latter cannot happen here, so this is the teardown race. Drop with
                    // a debug log rather than parking on a channel that will never accept another
                    // write.
                    Log.Debug("Dropping inbound Artery ordinary message: lane [{0}]'s channel is already completing.", laneIndex);
                    return true;
                }

                _parkedLaneIndex = laneIndex;
                _parkedItem = item;
                _writeInFlight = true;
                continuation.AsTask().ContinueWith(_onLaneWriteReady!, TaskContinuationOptions.ExecuteSynchronously);
                return false;
            }

            private void OnLaneWriteAvailable(bool available)
            {
                _writeInFlight = false;
                var writer = _lanes![_parkedLaneIndex].Writer;

                if (available && writer.TryWrite(_parkedItem))
                {
                    _parkedItem = default;
                }
                else
                {
                    // Channel completed underneath us (connection tearing down) -- drop; teardown
                    // is already in progress, there is nothing further to do for this element.
                    if (!available)
                        Log.Debug("Dropping inbound Artery ordinary message: lane [{0}]'s channel completed while parked.", _parkedLaneIndex);
                    _parkedItem = default;
                }

                // Resume: drain whatever further frames are already buffered, then either push a
                // pending control envelope, pull more input, or -- if upstream had ALREADY
                // finished while we were parked -- complete the stage now (nothing else will
                // re-trigger that check for a connection carrying only ordinary/lane traffic).
                DrainReadyFramesLaneMode();
                DeliverOrPull();

                if (_pending.Count == 0 && !_writeInFlight && IsClosed(_stage.In))
                    CompleteStage();
            }

            #endregion
        }
    }
}
