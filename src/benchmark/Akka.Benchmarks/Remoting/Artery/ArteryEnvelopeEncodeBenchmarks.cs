//-----------------------------------------------------------------------
// <copyright file="ArteryEnvelopeEncodeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using Akka.Benchmarks.Configurations;
using Akka.IO;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

// Alias needed: this namespace already declares its OWN benchmark-local `ArteryEnvelopeCodec`
// stand-in (see ArteryWireFormat.cs) for the Task 0 substrate benchmarks -- an unqualified
// `using Akka.Remote.Artery;` would be shadowed by that same-namespace type.
using RealArteryEnvelopeCodec = Akka.Remote.Artery.ArteryEnvelopeCodec;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// G3 opening-refactor Task 3: head-to-head of the two ways an encoded Artery frame can leave
    /// <see cref="PooledPayloadWriter"/> --
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// the OLD per-message pattern <c>ArteryRemoting.EncodeOutboundElement</c> used before this
    /// refactor: <c>writer.WrittenSpan.ToArray()</c> (an O(frame) alloc + memcpy) then
    /// <c>writer.Dispose()</c> (return the rented array to the pool immediately);
    /// </description></item>
    /// <item><description>
    /// the owner-only public handoff: <c>writer.Detach()</c> (no copy -- ownership of the SAME
    /// rented array moves to the caller) followed by <c>owner.Dispose()</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// Both benchmarks encode a REAL envelope frame (fixed header + sender/recipient/manifest
    /// literals + payload) via <see cref="RealArteryEnvelopeCodec"/>'s EXPLICIT-PARTS overload --
    /// <see cref="RealArteryEnvelopeCodec.Encode(System.Span{byte},long,int,string?,string?,string,System.ReadOnlySpan{byte})"/> --
    /// written directly into a <see cref="PooledPayloadWriter"/>'s rented span. This is
    /// deliberately NOT the V2 single-pass overload: that one needs a live <c>ActorSystem</c>'s
    /// <c>Serialization</c> extension to resolve a serializer + manifest, which would confound this
    /// benchmark's buffer-handoff comparison with serializer-lookup
    /// noise that the other Artery benchmarks (<see cref="ArteryWireFormat"/>,
    /// <see cref="ArterySingleIslandBenchmarks"/>) already cover.
    /// </para>
    /// <para>The producer-carrier pairs additionally include owner/segment construction and the
    /// ownership disposal walk. Their two-frame variants use the real lane-batching append,
    /// including the consolidated seed's normalization wrapper. They exclude TCP's separate
    /// write-buffer segment and do not establish an end-to-end transport improvement.</para>
    /// </remarks>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArteryEnvelopeEncodeBenchmarks
    {
        private const long OriginUid = 0x0102_0304_0506_0708L;
        private const int SerializerId = 17;
        private const string SenderPath = "akka://Sys@127.0.0.1:9001/user/sender-actor";
        private const string RecipientPath = "akka://Sys@127.0.0.1:9001/user/recipient-actor";
        private const string Manifest = "Akka.Benchmarks.Remoting.Artery.EncodeBenchmarkMessage";

        /// <summary>Payload sizes spanning small (control-message-scale), mid, and multi-KB messages.</summary>
        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        private byte[] _payload = null!;
        private int _capacityHint;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _payload = new byte[PayloadSize];
            new Random(42).NextBytes(_payload);

            _capacityHint = RealArteryEnvelopeCodec.MaxEncodedSize(SenderPath, RecipientPath, Manifest, PayloadSize);
        }

        /// <summary>The OLD per-message pattern: encode, copy the written span into a fresh array, dispose the writer (return its rented array).</summary>
        [Benchmark(Baseline = true)]
        public int Encode_CopyToArray_Then_Dispose()
        {
            using var writer = new PooledPayloadWriter(_capacityHint);
            EncodeInto(writer);

            var copy = writer.WrittenSpan.ToArray();
            return copy.Length;
        }

        /// <summary>The NEW pattern: encode, detach ownership of the SAME rented array (no copy), then dispose the detached owner.</summary>
        [Benchmark]
        public int Encode_Detach_Then_Dispose()
        {
            var writer = new PooledPayloadWriter(_capacityHint);
            EncodeInto(writer);

            var owner = writer.Detach();
            var length = owner.Memory.Length;
            owner.Dispose();
            return length;
        }

        /// <summary>Original producer handoff, including both detached owner and sequence segment.</summary>
        [Benchmark]
        public long Encode_SeparateOwnerAndSegment_Then_Dispose()
        {
            using var writer = new PooledPayloadWriter(_capacityHint);
            EncodeInto(writer);
            var sequence = OwnedSequenceSegment.Create(writer.Detach());
            var length = sequence.Length;
            sequence.DisposeOwnedSegments();
            return length;
        }

        /// <summary>Combined producer handoff with the same frame, pool and disposal walk.</summary>
        [Benchmark]
        public long Encode_CombinedOwnerAndSegment_Then_Dispose()
        {
            using var writer = new PooledPayloadWriter(_capacityHint);
            EncodeInto(writer);
            var sequence = writer.DetachOwnedSequence();
            var length = sequence.Length;
            sequence.DisposeOwnedSegments();
            return length;
        }

        /// <summary>Two original frames through the actual lane batch append operation.</summary>
        [Benchmark]
        public long Encode_TwoSeparateOwnerFrames_Then_BatchAndDispose()
        {
            using var first = new PooledPayloadWriter(_capacityHint);
            using var second = new PooledPayloadWriter(_capacityHint);
            EncodeInto(first);
            EncodeInto(second);
            var batch = Akka.Remote.Artery.ArteryRemoting.AppendFrameToBatch(
                OwnedSequenceSegment.Create(first.Detach()), OwnedSequenceSegment.Create(second.Detach()));
            var length = batch.Length;
            batch.DisposeOwnedSegments();
            return length;
        }

        /// <summary>Includes normalization of the consolidated seed before the first lane append.</summary>
        [Benchmark]
        public long Encode_TwoCombinedOwnerFrames_Then_BatchAndDispose()
        {
            using var first = new PooledPayloadWriter(_capacityHint);
            using var second = new PooledPayloadWriter(_capacityHint);
            EncodeInto(first);
            EncodeInto(second);
            var batch = Akka.Remote.Artery.ArteryRemoting.AppendFrameToBatch(
                first.DetachOwnedSequence(), second.DetachOwnedSequence());
            var length = batch.Length;
            batch.DisposeOwnedSegments();
            return length;
        }

        /// <summary>
        /// Writes one real Artery frame -- fixed header + sender/recipient/manifest literals +
        /// payload -- directly into <paramref name="writer"/>'s rented span via the explicit-parts
        /// <see cref="RealArteryEnvelopeCodec.Encode(System.Span{byte},long,int,string?,string?,string,System.ReadOnlySpan{byte})"/>
        /// overload (no <c>ActorSystem</c>/serializer lookup needed).
        /// </summary>
        private void EncodeInto(PooledPayloadWriter writer)
        {
            var span = writer.GetSpan(_capacityHint);
            var written = RealArteryEnvelopeCodec.Encode(span, OriginUid, SerializerId, SenderPath, RecipientPath, Manifest, _payload);
            writer.Advance(written);
        }
    }
}
