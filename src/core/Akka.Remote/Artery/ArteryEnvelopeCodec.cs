//-----------------------------------------------------------------------
// <copyright file="ArteryEnvelopeCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Encodes and decodes the Artery envelope described in
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" / Decision 4):
    /// a 32-byte fixed header (<see cref="ArteryEnvelopeFixedHeader"/>) followed by an optional
    /// metadata section, then length-prefixed UTF-8 literals for any LITERAL sender/recipient/
    /// manifest tag, then the serialized payload. The whole frame on the wire is
    /// <c>[u32 LE frame length][envelope]</c>; the frame length is always back-patched from actual
    /// bytes written, never predicted from a size hint.
    ///
    /// <para>
    /// This codec is deliberately separate from <see cref="Akka.Serialization.SerializerV2"/>
    /// (Decision 4): the envelope owns remoting metadata (version, flags, origin UID, serializer id,
    /// manifest, sender, recipient, payload boundaries); <c>SerializerV2</c> owns the payload bytes.
    /// </para>
    /// </summary>
    internal static class ArteryEnvelopeCodec
    {
        /// <summary>
        /// Total encoded size for an explicit-parts encode with the given sender/recipient/manifest
        /// strings and payload length. Callers use this to rent a big-enough destination buffer
        /// before calling <see cref="Encode(Span{byte},long,int,string?,string?,string,ReadOnlySpan{byte})"/>.
        /// This is an exact size (not merely an upper bound) because, unlike the V2 single-pass
        /// overload, every input here is already a concrete string/length.
        /// </summary>
        public static int MaxEncodedSize(string? senderPath, string? recipientPath, string manifest, int payloadLength)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength), payloadLength, "Payload length must be non-negative.");

            manifest ??= string.Empty;

            return ArteryEnvelopeHeader.FrameLengthFieldLength + ArteryEnvelopeHeader.HeaderLength
                + LiteralWireSize(senderPath) + LiteralWireSize(recipientPath) + LiteralWireSize(manifest)
                + payloadLength;
        }

        /// <summary>
        /// Encodes one Artery frame -- <c>[u32 LE frame length][32B fixed header][literals][payload]</c>
        /// -- into <paramref name="destination"/>, from already-resolved parts (serializer id,
        /// manifest, and raw payload bytes are all provided by the caller; compare the V2 single-pass
        /// overload, which resolves serializer id + manifest from a message object and streams the
        /// payload directly). A null or empty <paramref name="senderPath"/> / <paramref name="recipientPath"/>
        /// encodes as the ABSENT tag; an empty <paramref name="manifest"/> likewise encodes as ABSENT.
        /// </summary>
        /// <returns>The total number of bytes written to <paramref name="destination"/> (frame-length field + envelope).</returns>
        /// <exception cref="ArteryEnvelopeException">A sender/recipient/manifest literal's UTF-8 form exceeds 64KB - 1 bytes.</exception>
        public static int Encode(
            Span<byte> destination,
            long originUid,
            int serializerId,
            string? senderPath,
            string? recipientPath,
            string manifest,
            ReadOnlySpan<byte> payload)
        {
            manifest ??= string.Empty;

            var envelope = destination.Slice(ArteryEnvelopeHeader.FrameLengthFieldLength);
            var cursor = ArteryEnvelopeHeader.HeaderLength;

            // Literal placement order (not load-bearing for decode -- tags carry absolute offsets --
            // but fixed by convention): sender, recipient, manifest.
            var senderTag = WriteLiteral(envelope, ref cursor, senderPath);
            var recipientTag = WriteLiteral(envelope, ref cursor, recipientPath);
            var manifestTag = WriteLiteral(envelope, ref cursor, manifest);

            var payloadOffset = cursor;
            payload.CopyTo(envelope.Slice(payloadOffset));

            var frameLength = payloadOffset + payload.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)frameLength);
            WriteFixedHeader(
                envelope.Slice(0, ArteryEnvelopeHeader.HeaderLength),
                serializerId, originUid, senderTag, recipientTag, manifestTag, payloadOffset);

            return ArteryEnvelopeHeader.FrameLengthFieldLength + frameLength;
        }

        /// <summary>
        /// Single-pass V2 encode: resolves the serializer + manifest for <paramref name="message"/>
        /// UPFRONT (before any payload bytes are written), writes the header + literals, then
        /// streams the payload directly into the SAME growable pooled writer via the serializer's
        /// <c>Serialize(object, IBufferWriter&lt;byte&gt;)</c> hook -- no intermediate byte[] copy for
        /// the payload -- and finally back-patches the frame-length field and the fixed header (tags,
        /// payload offset) now that the total size is known.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This mirrors the internal <c>Serialization.Serialize(object, IBufferWriter&lt;byte&gt;)</c>
        /// hook in <c>Serialization.cs</c> (~line 528): find the serializer via
        /// <c>FindSerializerV2For</c>, resolve the manifest via <c>Serialization.ManifestFor</c>, then
        /// call <c>serializer.Serialize(obj, writer)</c>. The difference here is that manifest
        /// resolution is split out and done BEFORE the header is written, because the envelope
        /// header needs the manifest tag laid down (or at least its literal reserved) ahead of the
        /// payload.
        /// </para>
        /// <para>
        /// There is no separate "classic serializer" fallback path to implement: <c>FindSerializerV2For</c>
        /// always returns a <see cref="Akka.Serialization.SerializerV2"/> -- either a native V2
        /// serializer, or a classic <see cref="Akka.Serialization.Serializer"/> transparently wrapped
        /// by <see cref="Akka.Serialization.SerializerV1Adapter"/>. The adapter's <c>Manifest(object)</c>
        /// is itself always computable upfront (it inspects <c>SerializerWithStringManifest</c> /
        /// <c>IncludeManifest</c> / <c>obj.GetType()</c> -- none of which depend on the serialized
        /// bytes), and its <c>Serialize(object, IBufferWriter&lt;byte&gt;)</c> simply copies the
        /// classic <c>ToBinary(obj)</c> result into the writer. So the single call sequence below
        /// (find serializer -&gt; resolve manifest -&gt; serialize) is uniformly both the native-V2 path
        /// AND the classic-serializer compatibility path.
        /// </para>
        /// </remarks>
        /// <param name="serialization">The actor system's <see cref="Akka.Serialization.Serialization"/> extension.</param>
        /// <param name="originUid">The sending system's UID.</param>
        /// <param name="senderPath">The sender ref's path, or <see langword="null"/>/empty for no sender.</param>
        /// <param name="recipientPath">The recipient ref's path, or <see langword="null"/>/empty for no recipient.</param>
        /// <param name="message">The message to serialize as the envelope's payload.</param>
        /// <returns>
        /// A <see cref="Akka.Serialization.PooledPayloadWriter"/> owning the encoded frame
        /// (<c>[u32 LE frame length][envelope]</c> in <see cref="Akka.Serialization.PooledPayloadWriter.WrittenSpan"/>).
        /// The caller MUST <see cref="IDisposable.Dispose"/> it to return the rented buffer -- or call
        /// <see cref="Akka.Serialization.PooledPayloadWriter.Detach"/> to hand ownership of the encoded
        /// bytes to a transport without an intermediate copy.
        /// </returns>
        /// <exception cref="ArteryEnvelopeException">A sender/recipient/manifest literal's UTF-8 form exceeds 64KB - 1 bytes.</exception>
        public static Akka.Serialization.PooledPayloadWriter Encode(
            Akka.Serialization.Serialization serialization,
            long originUid,
            string? senderPath,
            string? recipientPath,
            object message)
        {
            if (serialization is null)
                throw new ArgumentNullException(nameof(serialization));
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            return Akka.Serialization.Serialization.WithTransport(serialization.System, () =>
            {
                var serializer = serialization.FindSerializerV2For(message);
                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, message) ?? string.Empty;

                const int reservedPrefixLength = ArteryEnvelopeHeader.FrameLengthFieldLength + ArteryEnvelopeHeader.HeaderLength;
                var capacityHint = reservedPrefixLength
                    + LiteralWireSize(senderPath) + LiteralWireSize(recipientPath) + LiteralWireSize(manifest);

                var writer = new Akka.Serialization.PooledPayloadWriter(capacityHint);
                try
                {
                    // Reserve the frame-length field + fixed header; both are back-patched below
                    // once the tag offsets, payload offset, and total frame length are known.
                    writer.GetSpan(reservedPrefixLength);
                    writer.Advance(reservedPrefixLength);

                    var senderTag = WriteLiteral(writer, senderPath);
                    var recipientTag = WriteLiteral(writer, recipientPath);
                    var manifestTag = WriteLiteral(writer, manifest);

                    var payloadOffset = writer.WrittenCount - ArteryEnvelopeHeader.FrameLengthFieldLength;

                    // Single-pass: the payload streams directly into the SAME growable buffer --
                    // no intermediate byte[] copy.
                    serializer.Serialize(message, writer);

                    var frameLength = writer.WrittenCount - ArteryEnvelopeHeader.FrameLengthFieldLength;

                    BinaryPrimitives.WriteUInt32LittleEndian(
                        writer.GetPatchSpan(0, ArteryEnvelopeHeader.FrameLengthFieldLength), (uint)frameLength);
                    WriteFixedHeader(
                        writer.GetPatchSpan(ArteryEnvelopeHeader.FrameLengthFieldLength, ArteryEnvelopeHeader.HeaderLength),
                        serializer.Identifier, originUid, senderTag, recipientTag, manifestTag, payloadOffset);

                    return writer;
                }
                catch
                {
                    writer.Dispose();
                    throw;
                }
            });
        }

        /// <summary>
        /// Decodes envelope metadata from <paramref name="frameBody"/> -- the envelope WITHOUT the
        /// 4-byte frame-length prefix (that prefix belongs to the TCP framing layer, Decision 3, and
        /// is consumed by the frame parser before this codec ever sees the bytes). Only the fixed
        /// 32-byte header (plus, if present, the metadata-section length prefix) is read; the
        /// payload is exposed as a lazy <see cref="ReadOnlySequence{T}"/> slice, and sender/recipient/
        /// manifest literals are resolved lazily on demand. Decoding the fixed header itself never
        /// allocates (single-segment fast path; stackalloc copy fallback for a header split across
        /// segments).
        /// </summary>
        /// <exception cref="ArteryEnvelopeException">
        /// The frame body is shorter than the fixed header; the version is unsupported; a reserved
        /// flag bit is set; the declared payload offset is less than 32 or beyond the frame end; or
        /// (if flags bit 0 is set) the metadata section's declared length extends past the payload
        /// offset.
        /// </exception>
        public static ArteryEnvelopeDecoded Decode(ReadOnlySequence<byte> frameBody)
        {
            if (frameBody.Length < ArteryEnvelopeHeader.HeaderLength)
                throw new ArteryEnvelopeException(
                    $"Envelope frame body ({frameBody.Length} bytes) is shorter than the fixed header ({ArteryEnvelopeHeader.HeaderLength} bytes).");

            var headerSeq = frameBody.Slice(0, ArteryEnvelopeHeader.HeaderLength);
            Span<byte> tmp = stackalloc byte[ArteryEnvelopeHeader.HeaderLength];
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

            var version = h[ArteryEnvelopeHeader.VersionOffset];
            if (version != ArteryEnvelopeHeader.CurrentVersion)
                throw new ArteryEnvelopeException(
                    $"Unsupported Artery envelope version {version}; this decoder only understands version {ArteryEnvelopeHeader.CurrentVersion}.");

            var flags = h[ArteryEnvelopeHeader.FlagsOffset];
            if ((flags & ArteryEnvelopeHeader.ReservedFlagsMask) != 0)
                throw new ArteryEnvelopeException(
                    $"Envelope flags byte 0x{flags:X2} sets a reserved bit (mask 0x{ArteryEnvelopeHeader.ReservedFlagsMask:X2}); refusing to guess at unknown semantics.");

            var actorRefTableVersion = h[ArteryEnvelopeHeader.ActorRefTableVersionOffset];
            var manifestTableVersion = h[ArteryEnvelopeHeader.ManifestTableVersionOffset];
            var originUid = BinaryPrimitives.ReadInt64LittleEndian(h.Slice(ArteryEnvelopeHeader.OriginUidOffset));
            var serializerId = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(ArteryEnvelopeHeader.SerializerIdOffset));
            var senderTag = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(ArteryEnvelopeHeader.SenderTagOffset));
            var recipientTag = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(ArteryEnvelopeHeader.RecipientTagOffset));
            var manifestTag = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(ArteryEnvelopeHeader.ManifestTagOffset));
            var payloadOffset = (long)BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(ArteryEnvelopeHeader.PayloadOffsetFieldOffset));

            if (payloadOffset < ArteryEnvelopeHeader.HeaderLength)
                throw new ArteryEnvelopeException(
                    $"Declared payload offset {payloadOffset} is less than the fixed header length ({ArteryEnvelopeHeader.HeaderLength}).");
            if (payloadOffset > frameBody.Length)
                throw new ArteryEnvelopeException(
                    $"Declared payload offset {payloadOffset} is beyond the end of the frame ({frameBody.Length} bytes).");

            if ((flags & ArteryEnvelopeHeader.MetadataPresentFlag) != 0)
            {
                if (ArteryEnvelopeHeader.MetadataSectionOffset + ArteryEnvelopeHeader.MetadataLengthPrefixSize > payloadOffset)
                    throw new ArteryEnvelopeException(
                        "Metadata section flag is set but there is no room for its length prefix before the payload offset.");

                Span<byte> lenBuf = stackalloc byte[ArteryEnvelopeHeader.MetadataLengthPrefixSize];
                frameBody.Slice(ArteryEnvelopeHeader.MetadataSectionOffset, ArteryEnvelopeHeader.MetadataLengthPrefixSize).CopyTo(lenBuf);
                var metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
                var metadataEnd = (long)ArteryEnvelopeHeader.MetadataSectionOffset + ArteryEnvelopeHeader.MetadataLengthPrefixSize + metadataLength;
                if (metadataEnd > payloadOffset)
                    throw new ArteryEnvelopeException(
                        $"Metadata section (length {metadataLength}) extends past the declared payload offset ({payloadOffset}).");

                // MVP: the metadata section's contents are not yet interpreted -- only skipped over.
                // The only planned consumer is the deferred actor-ref/manifest compression scheme.
            }

            var header = new ArteryEnvelopeFixedHeader(
                version, flags, actorRefTableVersion, manifestTableVersion,
                originUid, serializerId, senderTag, recipientTag, manifestTag, payloadOffset);

            return new ArteryEnvelopeDecoded(frameBody, header);
        }

        private static void WriteFixedHeader(
            Span<byte> header,
            int serializerId,
            long originUid,
            uint senderTag,
            uint recipientTag,
            uint manifestTag,
            long payloadOffset)
        {
            header[ArteryEnvelopeHeader.VersionOffset] = ArteryEnvelopeHeader.CurrentVersion;
            header[ArteryEnvelopeHeader.FlagsOffset] = 0;
            header[ArteryEnvelopeHeader.ActorRefTableVersionOffset] = 0;
            header[ArteryEnvelopeHeader.ManifestTableVersionOffset] = 0;
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(ArteryEnvelopeHeader.OriginUidOffset), originUid);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(ArteryEnvelopeHeader.SerializerIdOffset), serializerId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ArteryEnvelopeHeader.SenderTagOffset), senderTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ArteryEnvelopeHeader.RecipientTagOffset), recipientTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ArteryEnvelopeHeader.ManifestTagOffset), manifestTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(ArteryEnvelopeHeader.PayloadOffsetFieldOffset), checked((uint)payloadOffset));
        }

        /// <summary>Writes a LITERAL tag's length-prefixed UTF-8 bytes into a fixed <see cref="Span{T}"/>, advancing <paramref name="cursor"/>.</summary>
        private static uint WriteLiteral(Span<byte> envelope, ref int cursor, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return ArteryEnvelopeHeader.AbsentTag;

            var byteCount = ValidatedUtf8ByteCount(value);
            var tagOffset = cursor;

            BinaryPrimitives.WriteUInt16LittleEndian(envelope.Slice(cursor, ArteryEnvelopeHeader.LiteralLengthPrefixSize), (ushort)byteCount);
            cursor += ArteryEnvelopeHeader.LiteralLengthPrefixSize;

            if (byteCount > 0)
            {
                // PERF: Encoding.GetBytes(string) allocates an intermediate byte[] -- netstandard2.0
                // has no Encoding.GetBytes(string, Span<byte>) overload. Literals (actor paths /
                // manifests) are the cold path at G1; ref/manifest compression (deferred) removes
                // them from the hot path entirely.
                var bytes = Encoding.UTF8.GetBytes(value);
                bytes.CopyTo(envelope.Slice(cursor, byteCount));
                cursor += byteCount;
            }

            return (uint)tagOffset;
        }

        /// <summary>Writes a LITERAL tag's length-prefixed UTF-8 bytes into a growable <see cref="Akka.Serialization.PooledPayloadWriter"/>.</summary>
        private static uint WriteLiteral(Akka.Serialization.PooledPayloadWriter writer, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return ArteryEnvelopeHeader.AbsentTag;

            var byteCount = ValidatedUtf8ByteCount(value);
            var tagOffset = writer.WrittenCount - ArteryEnvelopeHeader.FrameLengthFieldLength;

            var lengthSpan = writer.GetSpan(ArteryEnvelopeHeader.LiteralLengthPrefixSize);
            BinaryPrimitives.WriteUInt16LittleEndian(lengthSpan, (ushort)byteCount);
            writer.Advance(ArteryEnvelopeHeader.LiteralLengthPrefixSize);

            if (byteCount > 0)
            {
                // PERF: see the Span<byte> overload above -- same netstandard2.0 limitation.
                var bytes = Encoding.UTF8.GetBytes(value);
                var dataSpan = writer.GetSpan(byteCount);
                bytes.CopyTo(dataSpan);
                writer.Advance(byteCount);
            }

            return (uint)tagOffset;
        }

        private static int ValidatedUtf8ByteCount(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > ArteryEnvelopeHeader.MaxLiteralByteLength)
                throw new ArteryEnvelopeException(
                    $"Literal exceeds the maximum encodable length of {ArteryEnvelopeHeader.MaxLiteralByteLength} UTF-8 bytes (was {byteCount}).");

            return byteCount;
        }

        private static int LiteralWireSize(string? value) =>
            string.IsNullOrEmpty(value) ? 0 : ArteryEnvelopeHeader.LiteralLengthPrefixSize + Encoding.UTF8.GetByteCount(value);
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// The classification of an Artery tag (sender / recipient / manifest), per design.md's CLOSED
    /// tag scheme: tag <c>0x00000000</c> is ABSENT; a non-zero top byte is COMPRESSED; anything else
    /// is a LITERAL absolute byte offset.
    /// </summary>
    internal enum ArteryTagKind : byte
    {
        /// <summary>No sender / no recipient / empty manifest.</summary>
        Absent = 0,

        /// <summary>The tag is an absolute byte offset (from envelope offset 0) of a length-prefixed UTF-8 literal.</summary>
        Literal = 1,

        /// <summary>
        /// The tag is a compression-table index (low 16 bits). Resolving a COMPRESSED index to an
        /// actor ref / manifest string is future work (ref/manifest compression, deferred post-MVP).
        /// </summary>
        Compressed = 2
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// The result of <see cref="ArteryEnvelopeCodec.Decode"/>: the decoded fixed header plus lazy
    /// accessors for the payload slice and the sender/recipient/manifest tags. Resolving a LITERAL
    /// tag allocates a string (unavoidable on netstandard2.0 -- see the PERF note on
    /// <see cref="ArteryEnvelopeCodec"/>'s literal writers); everything else here is allocation-free.
    /// </summary>
    internal readonly struct ArteryEnvelopeDecoded
    {
        private readonly ReadOnlySequence<byte> _frameBody;

        internal ArteryEnvelopeDecoded(ReadOnlySequence<byte> frameBody, ArteryEnvelopeFixedHeader header)
        {
            _frameBody = frameBody;
            Header = header;
        }

        /// <summary>The decoded fixed 32-byte header.</summary>
        public ArteryEnvelopeFixedHeader Header { get; }

        /// <summary>
        /// The payload slice <c>[payloadOffset, frame end)</c>. An O(1) slice of the original
        /// sequence -- no copy, no deserialization -- and position-independent regardless of whether
        /// a metadata section or literal tail is present (design.md's rationale for the explicit
        /// payload-offset header field).
        /// </summary>
        public ReadOnlySequence<byte> Payload => _frameBody.Slice(Header.PayloadOffset);

        /// <summary>The classification of the sender-ref tag.</summary>
        public ArteryTagKind SenderKind => ClassifyTag(Header.SenderTag);

        /// <summary>The classification of the recipient-ref tag.</summary>
        public ArteryTagKind RecipientKind => ClassifyTag(Header.RecipientTag);

        /// <summary>The classification of the manifest tag.</summary>
        public ArteryTagKind ManifestKind => ClassifyTag(Header.ManifestTag);

        /// <summary>
        /// Resolves the sender ref's path. Returns <see langword="true"/> with <paramref name="path"/>
        /// set to <see langword="null"/> when ABSENT (no sender), or to the decoded literal string
        /// when LITERAL. Returns <see langword="false"/> when COMPRESSED -- see
        /// <see cref="SenderCompressedIndex"/>; resolving a compressed index to a ref is future work.
        /// </summary>
        public bool TryGetSenderPath(out string? path) => TryResolveTag(Header.SenderTag, out path);

        /// <summary>Resolves the recipient ref's path. See <see cref="TryGetSenderPath"/> for ABSENT/LITERAL/COMPRESSED semantics.</summary>
        public bool TryGetRecipientPath(out string? path) => TryResolveTag(Header.RecipientTag, out path);

        /// <summary>
        /// Resolves the manifest. ABSENT decodes to <see cref="string.Empty"/> (an empty manifest is
        /// itself a legitimate value, unlike sender/recipient's ABSENT meaning "no ref").
        /// </summary>
        public bool TryGetManifest(out string manifest)
        {
            if (TryResolveTag(Header.ManifestTag, out var resolved))
            {
                manifest = resolved ?? string.Empty;
                return true;
            }

            manifest = string.Empty;
            return false;
        }

        /// <summary>The compression-table index for a COMPRESSED sender tag. Only meaningful when <see cref="SenderKind"/> is <see cref="ArteryTagKind.Compressed"/>.</summary>
        public int SenderCompressedIndex => DecodeCompressedIndex(Header.SenderTag);

        /// <summary>The compression-table index for a COMPRESSED recipient tag. Only meaningful when <see cref="RecipientKind"/> is <see cref="ArteryTagKind.Compressed"/>.</summary>
        public int RecipientCompressedIndex => DecodeCompressedIndex(Header.RecipientTag);

        /// <summary>The compression-table index for a COMPRESSED manifest tag. Only meaningful when <see cref="ManifestKind"/> is <see cref="ArteryTagKind.Compressed"/>.</summary>
        public int ManifestCompressedIndex => DecodeCompressedIndex(Header.ManifestTag);

        private static ArteryTagKind ClassifyTag(uint tag)
        {
            if (tag == ArteryEnvelopeHeader.AbsentTag)
                return ArteryTagKind.Absent;

            return (tag & ArteryEnvelopeHeader.CompressedTagMask) != 0 ? ArteryTagKind.Compressed : ArteryTagKind.Literal;
        }

        private static int DecodeCompressedIndex(uint tag) => (int)(tag & ArteryEnvelopeHeader.CompressedIndexMask);

        private bool TryResolveTag(uint tag, out string? value)
        {
            switch (ClassifyTag(tag))
            {
                case ArteryTagKind.Absent:
                    value = null;
                    return true;
                case ArteryTagKind.Compressed:
                    value = null;
                    return false;
                default:
                    value = ResolveLiteral(tag);
                    return true;
            }
        }

        private string ResolveLiteral(uint tagOffsetRaw)
        {
            var offset = (long)tagOffsetRaw;

            if (offset < ArteryEnvelopeHeader.HeaderLength)
                throw new ArteryEnvelopeException(
                    $"Literal tag offset {offset} is inside the fixed header (must be >= {ArteryEnvelopeHeader.HeaderLength}).");
            if (offset + ArteryEnvelopeHeader.LiteralLengthPrefixSize > Header.PayloadOffset)
                throw new ArteryEnvelopeException(
                    $"Literal tag offset {offset} leaves no room for its length prefix before the payload offset ({Header.PayloadOffset}).");

            Span<byte> lenBuf = stackalloc byte[ArteryEnvelopeHeader.LiteralLengthPrefixSize];
            _frameBody.Slice(offset, ArteryEnvelopeHeader.LiteralLengthPrefixSize).CopyTo(lenBuf);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);

            var dataStart = offset + ArteryEnvelopeHeader.LiteralLengthPrefixSize;
            var dataEnd = dataStart + length;
            if (dataEnd > Header.PayloadOffset)
                throw new ArteryEnvelopeException(
                    $"Literal at offset {offset} (length {length}) extends past the payload offset ({Header.PayloadOffset}).");

            if (length == 0)
                return string.Empty;

            // PERF: netstandard2.0 has no Encoding.GetString(ReadOnlySpan<byte>) overload (added in
            // netstandard2.1), so literal resolution always allocates an intermediate byte[] via
            // ArrayPool. Acceptable at G1 -- literals are the cold path; ref/manifest compression
            // (deferred) replaces them with O(1) index lookups on the hot path.
            var rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                _frameBody.Slice(dataStart, length).CopyTo(rented);
                return Encoding.UTF8.GetString(rented, 0, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
