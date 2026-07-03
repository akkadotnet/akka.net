//-----------------------------------------------------------------------
// <copyright file="ArteryWireFormat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Decoded Artery-style inbound envelope produced by the header-decode step of the
    /// Task 0 substrate benchmarks (see <c>openspec/changes/artery-tcp-remoting/tasks.md</c>).
    ///
    /// <para>
    /// One instance is allocated per message in <em>every</em> benchmark configuration
    /// (actor-only, single-island, lanes) so allocation comparisons stay apples-to-apples.
    /// The production implementation pools envelopes; this harness deliberately does not,
    /// because the pooling win is identical across the configs being compared.
    /// </para>
    /// </summary>
    public sealed class InboundFrame
    {
        public byte Version;
        public byte Flags;
        public long OriginUid;
        public int SerializerId;
        public int SenderId;
        public int RecipientId;
        public int ManifestId;
        public ReadOnlySequence<byte> Payload;

        /// <summary>Deserialize-knob result; written to defeat dead-code elimination.</summary>
        public ulong Checksum;
    }

    /// <summary>
    /// Fixed-offset binary envelope codec matching the working-draft wire layout in
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>: a 28-byte fixed header
    /// (version, flags, table versions, origin UID i64 LE, serializer id i32 LE, and
    /// three 32-bit compressed-or-literal tags) preceded by a 4-byte little-endian
    /// frame length. The harness always writes compressed tags — the warm hot path.
    /// </summary>
    public static class ArteryEnvelopeCodec
    {
        public const int FrameLengthFieldLength = 4;
        public const int HeaderLength = 28;

        /// <summary>Compressed-tag discriminator per design.md: top byte != 0 → compressed.</summary>
        private const uint CompressedTagMarker = 0xFF000000u;
        private const uint TagValueMask = 0x0000FFFFu;

        public static uint CompressedTag(int index) => CompressedTagMarker | (uint)index;

        private static int DecodeTag(uint tag) => (int)(tag & TagValueMask);

        /// <summary>
        /// Encodes one frame — <c>[4B LE length][28B header][payload]</c> — into
        /// <paramref name="destination"/>. The length field excludes its own four bytes,
        /// matching <see cref="Akka.Streams.Dsl.Framing.LengthField(int,int,int,Akka.Streams.ByteOrder)"/>
        /// semantics (frame size = parsed length + field length).
        /// </summary>
        public static void EncodeFrame(Span<byte> destination, long originUid, int serializerId,
            int senderId, int recipientId, int manifestId, ReadOnlySpan<byte> payload)
        {
            var frameLength = HeaderLength + payload.Length;
            BinaryPrimitives.WriteInt32LittleEndian(destination, frameLength);

            var h = destination.Slice(FrameLengthFieldLength);
            h[0] = 1;                                                    // version
            h[1] = 0;                                                    // flags
            h[2] = 0;                                                    // actorRef compression-table version
            h[3] = 0;                                                    // manifest compression-table version
            BinaryPrimitives.WriteInt64LittleEndian(h.Slice(4), originUid);
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(12), serializerId);
            BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(16), CompressedTag(senderId));
            BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(20), CompressedTag(recipientId));
            BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(24), CompressedTag(manifestId));
            payload.CopyTo(h.Slice(HeaderLength));
        }

        /// <summary>Total encoded size of a frame carrying <paramref name="payloadSize"/> payload bytes.</summary>
        public static int FrameTotalLength(int payloadSize) =>
            FrameLengthFieldLength + HeaderLength + payloadSize;

        /// <summary>
        /// Decodes envelope metadata from a full frame (including the 4-byte length prefix,
        /// as emitted by <c>Framing.LengthField</c>) WITHOUT touching the payload — the
        /// structural decode-metadata-before-payload order from design.md. This runs on the
        /// serial decode island, so it must stay O(1)/sub-microsecond: fixed-offset reads,
        /// single-segment fast path, stackalloc copy only when a frame spans chunks.
        /// </summary>
        public static InboundFrame Decode(ReadOnlySequence<byte> frame)
        {
            var headerSeq = frame.Slice(FrameLengthFieldLength, HeaderLength);
            Span<byte> tmp = stackalloc byte[HeaderLength];
            scoped ReadOnlySpan<byte> h;
            if (headerSeq.IsSingleSegment)
            {
                h = headerSeq.First.Span;
            }
            else
            {
                headerSeq.CopyTo(tmp);
                h = tmp;
            }

            return new InboundFrame
            {
                Version = h[0],
                Flags = h[1],
                OriginUid = BinaryPrimitives.ReadInt64LittleEndian(h.Slice(4)),
                SerializerId = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(12)),
                SenderId = DecodeTag(BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(16))),
                RecipientId = DecodeTag(BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(20))),
                ManifestId = DecodeTag(BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(24))),
                Payload = frame.Slice(FrameLengthFieldLength + HeaderLength)
            };
        }
    }

    /// <summary>
    /// The tunable deserialize CPU knob from tasks.md Task 0: stands in for payload
    /// deserialization by FNV-1a-hashing approximately <c>bytesToHash</c> bytes of the
    /// payload (looping over it when <c>bytesToHash</c> exceeds the payload length).
    /// Calibrated ns-costs per knob value come from <c>DeserializeKnobCalibrationBenchmarks</c>.
    /// </summary>
    public static class DeserializeKnob
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Run(in ReadOnlySequence<byte> payload, int bytesToHash)
        {
            var payloadLength = (int)payload.Length;
            var passes = (bytesToHash + payloadLength - 1) / payloadLength;
            var hash = FnvOffsetBasis;

            if (payload.IsSingleSegment)
            {
                var span = payload.First.Span;
                for (var p = 0; p < passes; p++)
                {
                    for (var i = 0; i < span.Length; i++)
                        hash = (hash ^ span[i]) * FnvPrime;
                }

                return hash;
            }

            for (var p = 0; p < passes; p++)
            {
                foreach (var memory in payload)
                {
                    var span = memory.Span;
                    for (var i = 0; i < span.Length; i++)
                        hash = (hash ^ span[i]) * FnvPrime;
                }
            }

            return hash;
        }
    }
}
