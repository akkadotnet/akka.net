//-----------------------------------------------------------------------
// <copyright file="ArteryCompressionCodecSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Akka.Remote.Artery;
using Akka.Remote.Artery.Compression;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Wire round-trip tests for the Artery ref/manifest compression ENCODE/DECODE hooks in
    /// <see cref="ArteryEnvelopeCodec"/> (design.md "artery-ref-manifest-compression", Decision 4 --
    /// codec compression path, OFF-THE-WIRE). A provided outbound <see cref="CompressionTable{T}"/>
    /// drives COMPRESSED tags + the header table-version bytes on encode; the inverted
    /// <see cref="DecompressionTable{T}"/> resolves them on decode; a miss falls back to LITERAL on
    /// encode and drops (returns false) on decode.
    ///
    /// <para>
    /// The hard invariant pinned here: with NO table (or an <see cref="CompressionTable{T}.Empty"/>
    /// table, the off-by-default path), the encoded frame is BYTE-IDENTICAL to a build without
    /// compression.
    /// </para>
    /// </summary>
    public class ArteryCompressionCodecSpec
    {
        private const int FrameLengthFieldLength = 4;
        private const int HeaderLength = 32;

        private const long OriginUid = 0x0102_0304_0506_0708L;
        private const int SerializerId = 17;

        private const string SenderPath = "akka://Sys@host:1/user/sender";
        private const string RecipientPath = "akka://Sys@host:1/user/recipient";
        private const string Manifest = "My.Message.Manifest, MyAssembly";

        private static CompressionTable<string> ActorRefTable(byte version = 5) =>
            new(OriginUid, version, new Dictionary<string, int> { [SenderPath] = 0, [RecipientPath] = 1 });

        private static CompressionTable<string> ManifestTable(byte version = 9) =>
            new(OriginUid, version, new Dictionary<string, int> { [Manifest] = 0 });

        // ===================== encode: hit -> COMPRESSED + version byte =====================

        [Fact(DisplayName = "Encode should emit COMPRESSED tags with 16-bit indices and stamp the header table-version bytes on a table hit")]
        public void Encode_hit_emits_compressed_tags_and_version_bytes()
        {
            var refTable = ActorRefTable(version: 5);
            var manTable = ManifestTable(version: 9);
            var payload = MakePayload(24);

            var (frame, total) = Encode(SenderPath, RecipientPath, Manifest, payload, refTable, manTable);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame, total));

            // sender/recipient share the actor-ref table (version 5); manifest uses its own (version 9).
            decoded.Header.ActorRefTableVersion.Should().Be((byte)5);
            decoded.Header.ManifestTableVersion.Should().Be((byte)9);

            decoded.SenderKind.Should().Be(ArteryTagKind.Compressed);
            decoded.SenderCompressedIndex.Should().Be(0);
            decoded.Header.SenderTag.Should().Be(ArteryEnvelopeHeader.CompressedTagMarker | 0u);

            decoded.RecipientKind.Should().Be(ArteryTagKind.Compressed);
            decoded.RecipientCompressedIndex.Should().Be(1);
            decoded.Header.RecipientTag.Should().Be(ArteryEnvelopeHeader.CompressedTagMarker | 1u);

            decoded.ManifestKind.Should().Be(ArteryTagKind.Compressed);
            decoded.ManifestCompressedIndex.Should().Be(0);

            // COMPRESSED tags carry no literal, so the payload starts right after the fixed header.
            decoded.Header.PayloadOffset.Should().Be(HeaderLength);
            decoded.Payload.ToArray().Should().Equal(payload);
        }

        [Fact(DisplayName = "Decode should resolve COMPRESSED indices back to their values via the inverted table")]
        public void Decode_resolves_compressed_indices_via_inverted_table()
        {
            var refTable = ActorRefTable(version: 5);
            var manTable = ManifestTable(version: 9);
            var payload = MakePayload(8);

            var (frame, total) = Encode(SenderPath, RecipientPath, Manifest, payload, refTable, manTable);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame, total));

            var invertedRefs = refTable.Invert();
            var invertedManifests = manTable.Invert();

            decoded.TryGetSenderPath(invertedRefs, out var sender).Should().BeTrue();
            sender.Should().Be(SenderPath);

            decoded.TryGetRecipientPath(invertedRefs, out var recipient).Should().BeTrue();
            recipient.Should().Be(RecipientPath);

            decoded.TryGetManifest(invertedManifests, out var manifest).Should().BeTrue();
            manifest.Should().Be(Manifest);
        }

        [Fact(DisplayName = "Encode should COMPRESS a hit and keep a miss LITERAL within the same envelope")]
        public void Encode_mixed_hit_and_miss()
        {
            // Recipient is in the table (index 1); sender is NOT -> sender stays LITERAL.
            var refTable = ActorRefTable(version: 5);
            const string unknownSender = "akka://Sys@host:1/user/unknown-sender";
            var payload = MakePayload(4);

            var (frame, total) = Encode(unknownSender, RecipientPath, manifest: "", payload, refTable, manifestTable: null);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame, total));

            // The version byte is still stamped because at least one actor-ref tag is COMPRESSED.
            decoded.Header.ActorRefTableVersion.Should().Be((byte)5);
            decoded.Header.ManifestTableVersion.Should().Be((byte)0, "no manifest table was provided");

            decoded.SenderKind.Should().Be(ArteryTagKind.Literal);
            decoded.TryGetSenderPath(refTable.Invert(), out var sender).Should().BeTrue();
            sender.Should().Be(unknownSender);

            decoded.RecipientKind.Should().Be(ArteryTagKind.Compressed);
            decoded.RecipientCompressedIndex.Should().Be(1);
        }

        // ===================== decode miss -> drop (false) =====================

        [Fact(DisplayName = "Decode should return false (drop) for a COMPRESSED index the table cannot resolve")]
        public void Decode_miss_returns_false()
        {
            var refTable = ActorRefTable(version: 5);
            var (frame, total) = Encode(SenderPath, RecipientPath, manifest: "", ReadOnlySpan<byte>.Empty, refTable, manifestTable: null);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame, total));

            // A stale/empty table cannot resolve the index -> false, and never throws / faults.
            decoded.TryGetSenderPath(DecompressionTable<string>.Empty, out var stale).Should().BeFalse();
            stale.Should().BeNull();

            // A null table (compression disabled / unwired) is likewise a miss, not a crash.
            decoded.TryGetRecipientPath(null, out var none).Should().BeFalse();
            none.Should().BeNull();
        }

        // ===================== off/default path is byte-identical LITERAL =====================

        [Fact(DisplayName = "Encode with no table should be byte-identical to encode with Empty tables and to a fully-literal baseline")]
        public void Default_and_empty_tables_are_byte_identical_literal()
        {
            var payload = MakePayload(40);

            var (noTable, noTableLen) = Encode(SenderPath, RecipientPath, Manifest, payload, actorRefTable: null, manifestTable: null);
            var (emptyTable, emptyLen) = Encode(SenderPath, RecipientPath, Manifest, payload,
                CompressionTable<string>.Empty, CompressionTable<string>.Empty);

            // Byte-identical: the disabled path writes exactly what a no-compression build writes.
            noTableLen.Should().Be(emptyLen);
            Slice(noTable, noTableLen).Should().Equal(Slice(emptyTable, emptyLen));

            // And it is genuinely LITERAL, with zero table-version bytes on the wire.
            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(noTable, noTableLen));
            decoded.Header.ActorRefTableVersion.Should().Be((byte)0);
            decoded.Header.ManifestTableVersion.Should().Be((byte)0);
            decoded.SenderKind.Should().Be(ArteryTagKind.Literal);
            decoded.RecipientKind.Should().Be(ArteryTagKind.Literal);
            decoded.ManifestKind.Should().Be(ArteryTagKind.Literal);

            decoded.TryGetSenderPath(out var sender).Should().BeTrue();
            sender.Should().Be(SenderPath);
            decoded.TryGetRecipientPath(out var recipient).Should().BeTrue();
            recipient.Should().Be(RecipientPath);
            decoded.TryGetManifest(out var manifest).Should().BeTrue();
            manifest.Should().Be(Manifest);
        }

        [Fact(DisplayName = "Encode with a non-empty table whose values all miss should still be byte-identical LITERAL")]
        public void All_miss_table_is_byte_identical_literal()
        {
            var payload = MakePayload(16);
            var refTable = new CompressionTable<string>(OriginUid, 7,
                new Dictionary<string, int> { ["/user/something-else"] = 0 });

            var (baseline, baseLen) = Encode(SenderPath, RecipientPath, Manifest, payload, actorRefTable: null, manifestTable: null);
            var (allMiss, missLen) = Encode(SenderPath, RecipientPath, Manifest, payload, refTable, manifestTable: null);

            missLen.Should().Be(baseLen);
            Slice(allMiss, missLen).Should().Equal(Slice(baseline, baseLen),
                "when nothing in the table matches, every tag falls back to LITERAL and the version byte stays 0");

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(allMiss, missLen));
            decoded.Header.ActorRefTableVersion.Should().Be((byte)0);
            decoded.SenderKind.Should().Be(ArteryTagKind.Literal);
        }

        // ===================== helpers =====================

        private static byte[] MakePayload(int length)
        {
            var payload = new byte[length];
            new Random(length).NextBytes(payload);
            return payload;
        }

        private static (byte[] frame, int total) Encode(
            string? senderPath, string? recipientPath, string manifest, ReadOnlySpan<byte> payload,
            CompressionTable<string>? actorRefTable, CompressionTable<string>? manifestTable)
        {
            // MaxEncodedSize assumes LITERALs (the worst case); a COMPRESSED encode writes fewer bytes,
            // so the buffer is always big enough -- slice by the returned length.
            var destination = new byte[ArteryEnvelopeCodec.MaxEncodedSize(senderPath, recipientPath, manifest, payload.Length)];
            var total = ArteryEnvelopeCodec.Encode(
                destination, OriginUid, SerializerId, senderPath, recipientPath, manifest, payload, actorRefTable, manifestTable);
            return (destination, total);
        }

        private static ReadOnlySequence<byte> EnvelopeBody(byte[] frame, int total) =>
            new(frame, FrameLengthFieldLength, total - FrameLengthFieldLength);

        private static byte[] Slice(byte[] frame, int total)
        {
            var slice = new byte[total];
            Array.Copy(frame, slice, total);
            return slice;
        }
    }
}
