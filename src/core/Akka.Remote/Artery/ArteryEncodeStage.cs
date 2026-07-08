//-----------------------------------------------------------------------
// <copyright file="ArteryEncodeStage.cs" company="Akka.NET Project">
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
    /// INTERNAL API.
    ///
    /// Encodes every outbound <see cref="IOutboundEnvelope"/> to its wire frame -- replacing
    /// <c>ArteryRemoting.EncodeOutboundElement</c>'s <c>.Select(...)</c> step, which did
    /// <c>writer.WrittenSpan.ToArray()</c> (an O(frame) alloc + memcpy on EVERY single message)
    /// before disposing the encode writer. This stage instead calls
    /// <see cref="Akka.Serialization.PooledPayloadWriter.Detach"/>: the encoded, POOLED buffer's
    /// ownership moves directly into an <see cref="OwnedSequenceSegment"/> and rides -- zero-copy --
    /// inside the <see cref="ReadOnlySequence{T}"/> this stage pushes.
    ///
    /// <para>
    /// <b>Ownership model (modernize-akka-io-tcp design.md, Decision 8).</b> This stage never
    /// disposes anything itself, on any path -- push, upstream finish, upstream failure, or
    /// downstream finish. It hands the owner off synchronously on every <c>OnPush</c> and is
    /// done with it. Disposal happens exactly once, downstream, by whichever party ends up
    /// responsible for the segment: write-coalescing (<c>Akka.Streams.Implementation.IO.TcpStages.TcpStreamLogic</c>)
    /// buffers it until flushed and disposes it in its own <c>PostStop</c> if the buffer is ever
    /// torn down unflushed, and <c>TcpConnection</c> disposes it once the bytes are proven copied
    /// into the OS write pipe. There is no generation lag and no inference here -- the owner's
    /// lifetime is fully determined by the single segment it rides in.
    /// </para>
    ///
    /// <para>
    /// <b>Why this stage itself needs no backstop.</b> The outbound stream from this stage's
    /// <c>Out</c> down to <c>OutgoingConnection</c>/coalescing is one fused island (encode -&gt;
    /// preamble-concat -&gt; killswitch -&gt; coalescing, no <c>.Async()</c> boundary in between), so
    /// <c>OnPush</c> hands the owner into coalescing's write buffer synchronously, on the same
    /// call -- this stage never holds an owner across handler calls, so there is nothing left for a
    /// teardown path to dispose. A backstop here would in fact be actively wrong: disposal is
    /// single-threaded by design (no <see cref="System.Threading.Interlocked"/>/<c>Volatile</c>
    /// anywhere in this transfer chain), and adding one would risk a second, concurrent dispose
    /// racing whichever downstream party (coalescing's <c>PostStop</c> or <c>TcpConnection</c>) is
    /// disposing the SAME owner on its own thread.
    /// </para>
    /// </summary>
    internal sealed class ArteryEncodeStage : GraphStage<FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>>>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryEncodeStage"/> class.
        /// </summary>
        /// <param name="serialization">The sending actor system's <see cref="Akka.Serialization.Serialization"/> extension.</param>
        /// <param name="originUid">The sending system's UID, stamped into every encoded envelope's fixed header.</param>
        /// <param name="pool">
        /// The <see cref="ArrayPool{T}"/> every encode call rents its buffer from (and downstream
        /// later returns it to). <see langword="null"/> (the default -- always the production value)
        /// means <see cref="ArrayPool{T}.Shared"/>. A non-null value here exists ONLY so a test can
        /// substitute a pool that scribbles over a returned array -- the "poison pool" regression
        /// suite -- turning a lifetime-safety bug into a loud, deterministic assertion failure.
        /// </param>
        public ArteryEncodeStage(Akka.Serialization.Serialization serialization, long originUid, ArrayPool<byte>? pool = null)
        {
            Serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
            OriginUid = originUid;
            Pool = pool;
            Shape = new FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>>(In, Out);
        }

        public Akka.Serialization.Serialization Serialization { get; }
        public long OriginUid { get; }
        public ArrayPool<byte>? Pool { get; }

        public Inlet<IOutboundEnvelope> In { get; } = new("ArteryEncode.in");
        public Outlet<ReadOnlySequence<byte>> Out { get; } = new("ArteryEncode.out");

        public override FlowShape<IOutboundEnvelope, ReadOnlySequence<byte>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryEncodeStage _stage;

            public Logic(ArteryEncodeStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var elem = Grab(_stage.In);

                // Encode-failure behavior mirrors the pre-refactor EncodeOutboundElement exactly:
                // no per-message try/catch here -- ArteryEnvelopeCodec.Encode's OWN internal
                // try/catch already disposes its writer on failure before rethrowing, and an
                // exception escaping OnPush fails this stage (matching the old .Select(...)
                // behavior of failing the whole outbound stream on an encode error).
                var writer = ArteryEnvelopeCodec.Encode(
                    _stage.Serialization, _stage.OriginUid, elem.SenderPath, elem.RecipientPath, elem.Message, _stage.Pool);

                // Detach moves ownership of the encoded, pooled buffer to `owner` -- no
                // WrittenSpan.ToArray() copy, and deliberately NO writer.Dispose() here (Detach
                // already spent the writer; see PooledPayloadWriter's ownership-rules remarks).
                // OwnedSequenceSegment.Create wraps it in a segment-backed sequence so the owner
                // rides -- zero-copy -- straight into coalescing's write buffer.
                var owner = writer.Detach();
                Push(_stage.Out, OwnedSequenceSegment.Create(owner));
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
        }
    }
}
