//-----------------------------------------------------------------------
// <copyright file="ArteryEnvelopeCodecSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Correctness tests for <see cref="ArteryEnvelopeCodec"/> against the wire layout described in
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" / Decision 4).
    /// Covers the explicit-parts encode/decode round trip (including multi-segment decode), and
    /// every documented rejection. The V2 single-pass overload is covered separately in
    /// <see cref="ArteryEnvelopeCodecV2Spec"/> because it needs a live <c>ActorSystem</c>.
    /// </summary>
    public class ArteryEnvelopeCodecSpec
    {
        private const int FrameLengthFieldLength = 4;
        private const int HeaderLength = 32;

        // ===================== round trip: explicit parts =====================

        [Fact(DisplayName = "Should_round_trip_all_absent_tags_When_sender_recipient_and_manifest_are_all_empty")]
        public void Should_round_trip_all_absent_tags_When_sender_recipient_and_manifest_are_all_empty()
        {
            var payload = MakePayload(16, seed: 1);

            var (frame, totalLength) = EncodeToFrame(originUid: 0x1122_3344_5566_7788L, serializerId: 17,
                senderPath: null, recipientPath: null, manifest: "", payload);

            Assert.Equal(HeaderLength + payload.Length, ReadFrameLength(frame));
            Assert.Equal(FrameLengthFieldLength + HeaderLength + payload.Length, totalLength);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));

            Assert.Equal((byte)1, decoded.Header.Version);
            Assert.Equal(0x1122_3344_5566_7788L, decoded.Header.OriginUid);
            Assert.Equal(17, decoded.Header.SerializerId);
            Assert.Equal(HeaderLength, decoded.Header.PayloadOffset);

            Assert.Equal(ArteryTagKind.Absent, decoded.SenderKind);
            Assert.True(decoded.TryGetSenderPath(out var sender));
            Assert.Null(sender);

            Assert.Equal(ArteryTagKind.Absent, decoded.RecipientKind);
            Assert.True(decoded.TryGetRecipientPath(out var recipient));
            Assert.Null(recipient);

            Assert.Equal(ArteryTagKind.Absent, decoded.ManifestKind);
            Assert.True(decoded.TryGetManifest(out var manifest));
            Assert.Equal(string.Empty, manifest);

            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        [Fact(DisplayName = "Should_round_trip_all_literal_tags_When_sender_recipient_and_manifest_use_non_ascii_utf8")]
        public void Should_round_trip_all_literal_tags_When_sender_recipient_and_manifest_use_non_ascii_utf8()
        {
            const string senderPath = "akka://Sys@host:1/user/sender-Ω-送信";
            const string recipientPath = "akka://Sys@host:1/user/recipient-Ä-名前";
            const string manifest = "My.Manifest, Assembly-Ω-テスト";
            var payload = MakePayload(64, seed: 2);

            var (frame, totalLength) = EncodeToFrame(originUid: 99L, serializerId: 3,
                senderPath, recipientPath, manifest, payload);

            var expectedPayloadOffset = HeaderLength
                + LiteralWireSize(senderPath) + LiteralWireSize(recipientPath) + LiteralWireSize(manifest);
            Assert.Equal(expectedPayloadOffset + payload.Length, ReadFrameLength(frame));
            Assert.Equal(FrameLengthFieldLength + expectedPayloadOffset + payload.Length, totalLength);
            Assert.Equal(totalLength,
                ArteryEnvelopeCodec.MaxEncodedSize(senderPath, recipientPath, manifest, payload.Length));

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));

            Assert.Equal(expectedPayloadOffset, decoded.Header.PayloadOffset);

            Assert.Equal(ArteryTagKind.Literal, decoded.SenderKind);
            Assert.True(decoded.TryGetSenderPath(out var decodedSender));
            Assert.Equal(senderPath, decodedSender);

            Assert.Equal(ArteryTagKind.Literal, decoded.RecipientKind);
            Assert.True(decoded.TryGetRecipientPath(out var decodedRecipient));
            Assert.Equal(recipientPath, decodedRecipient);

            Assert.Equal(ArteryTagKind.Literal, decoded.ManifestKind);
            Assert.True(decoded.TryGetManifest(out var decodedManifest));
            Assert.Equal(manifest, decodedManifest);

            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        [Fact(DisplayName = "Should_round_trip_mixed_tags_When_only_recipient_is_a_literal")]
        public void Should_round_trip_mixed_tags_When_only_recipient_is_a_literal()
        {
            const string recipientPath = "akka://Sys@host:1/user/only-recipient";
            var payload = MakePayload(8, seed: 3);

            var (frame, _) = EncodeToFrame(originUid: 1L, serializerId: 5,
                senderPath: null, recipientPath, manifest: "", payload);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));

            Assert.Equal(ArteryTagKind.Absent, decoded.SenderKind);
            Assert.True(decoded.TryGetSenderPath(out var sender));
            Assert.Null(sender);

            Assert.Equal(ArteryTagKind.Literal, decoded.RecipientKind);
            Assert.True(decoded.TryGetRecipientPath(out var recipient));
            Assert.Equal(recipientPath, recipient);

            Assert.Equal(ArteryTagKind.Absent, decoded.ManifestKind);
            Assert.True(decoded.TryGetManifest(out var manifest));
            Assert.Equal(string.Empty, manifest);

            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        [Fact(DisplayName = "Should_round_trip_an_empty_payload_When_all_tags_are_literal")]
        public void Should_round_trip_an_empty_payload_When_all_tags_are_literal()
        {
            const string senderPath = "akka://Sys@host:1/user/sender";
            const string recipientPath = "akka://Sys@host:1/user/recipient";
            const string manifest = "some.manifest";

            var (frame, totalLength) = EncodeToFrame(originUid: 7L, serializerId: 1,
                senderPath, recipientPath, manifest, ReadOnlySpan<byte>.Empty);

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));

            Assert.Equal(0, decoded.Payload.Length);
            Assert.Equal(totalLength, FrameLengthFieldLength + decoded.Header.PayloadOffset);
        }

        [Fact(DisplayName = "Should_round_trip_a_multi_kilobyte_payload_When_encoding_and_decoding")]
        public void Should_round_trip_a_multi_kilobyte_payload_When_encoding_and_decoding()
        {
            var payload = MakePayload(6_000, seed: 4);

            var (frame, totalLength) = EncodeToFrame(originUid: 555L, serializerId: 9,
                senderPath: "/user/a", recipientPath: "/user/b", manifest: "m", payload);

            Assert.Equal(totalLength - FrameLengthFieldLength, ReadFrameLength(frame));

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));
            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        // ===================== decode from a multi-segment ReadOnlySequence<byte> =====================

        [Fact(DisplayName = "Should_decode_correctly_When_the_fixed_header_is_split_across_segments")]
        public void Should_decode_correctly_When_the_fixed_header_is_split_across_segments()
        {
            const string senderPath = "/user/sender";
            const string manifest = "manifest-x";
            var payload = MakePayload(32, seed: 5);

            var (frame, _) = EncodeToFrame(originUid: 42L, serializerId: 2,
                senderPath, recipientPath: null, manifest, payload);
            var body = EnvelopeBody(frame).ToArray();

            // Split at byte 10 -- squarely inside the 32-byte fixed header.
            var segmented = TwoSegment(body, splitAt: 10);

            var decoded = ArteryEnvelopeCodec.Decode(segmented);

            Assert.Equal(42L, decoded.Header.OriginUid);
            Assert.Equal(2, decoded.Header.SerializerId);
            Assert.True(decoded.TryGetSenderPath(out var decodedSender));
            Assert.Equal(senderPath, decodedSender);
            Assert.True(decoded.TryGetManifest(out var decodedManifest));
            Assert.Equal(manifest, decodedManifest);
            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        [Fact(DisplayName = "Should_decode_correctly_When_a_literal_is_split_across_segments")]
        public void Should_decode_correctly_When_a_literal_is_split_across_segments()
        {
            const string senderPath = "akka://Sys@host:1/user/a-fairly-long-sender-path-for-splitting";
            var payload = MakePayload(8, seed: 6);

            var (frame, _) = EncodeToFrame(originUid: 1L, serializerId: 1,
                senderPath, recipientPath: null, manifest: "", payload);
            var body = EnvelopeBody(frame).ToArray();

            // The sender literal starts right after the 32-byte header (offset 32) with a 2-byte
            // length prefix, so offset 40 is a few bytes into the literal's UTF-8 data.
            var segmented = TwoSegment(body, splitAt: 40);

            var decoded = ArteryEnvelopeCodec.Decode(segmented);

            Assert.True(decoded.TryGetSenderPath(out var decodedSender));
            Assert.Equal(senderPath, decodedSender);
            Assert.Equal(payload, decoded.Payload.ToArray());
        }

        // ===================== rejections =====================

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_the_version_is_not_1")]
        public void Should_throw_ArteryEnvelopeException_When_the_version_is_not_1()
        {
            var header = BuildRawHeader(version: 2, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 0, recipientTag: 0, manifestTag: 0, payloadOffset: HeaderLength);

            Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header)));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_a_reserved_flag_bit_is_set")]
        public void Should_throw_ArteryEnvelopeException_When_a_reserved_flag_bit_is_set()
        {
            var header = BuildRawHeader(version: 1, flags: 0b0000_0010, originUid: 0, serializerId: 0,
                senderTag: 0, recipientTag: 0, manifestTag: 0, payloadOffset: HeaderLength);

            Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header)));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_a_literal_tag_offset_is_less_than_32")]
        public void Should_throw_ArteryEnvelopeException_When_a_literal_tag_offset_is_less_than_32()
        {
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 10, recipientTag: 0, manifestTag: 0, payloadOffset: HeaderLength);

            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header));
            Assert.Throws<ArteryEnvelopeException>(() => decoded.TryGetSenderPath(out _));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_a_literal_tag_offset_points_past_the_payload_offset")]
        public void Should_throw_ArteryEnvelopeException_When_a_literal_tag_offset_points_past_the_payload_offset()
        {
            // payloadOffset = 32 (no room for any literal), but the sender tag claims a literal at
            // offset 32 -- exactly at, not before, the payload offset.
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 32, recipientTag: 0, manifestTag: 0, payloadOffset: HeaderLength);

            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header));
            Assert.Throws<ArteryEnvelopeException>(() => decoded.TryGetSenderPath(out _));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_a_literal_length_is_truncated")]
        public void Should_throw_ArteryEnvelopeException_When_a_literal_length_is_truncated()
        {
            // payloadOffset = 34: room for the sender literal's 2-byte length prefix (at [32,34))
            // but zero bytes for its declared 5-byte body before the payload starts.
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 32, recipientTag: 0, manifestTag: 0, payloadOffset: 34);
            var frame = new byte[34];
            header.CopyTo(frame, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(32, 2), 5); // declares 5 bytes; none are available

            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(frame));
            Assert.Throws<ArteryEnvelopeException>(() => decoded.TryGetSenderPath(out _));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_the_declared_payload_offset_is_beyond_the_frame_end")]
        public void Should_throw_ArteryEnvelopeException_When_the_declared_payload_offset_is_beyond_the_frame_end()
        {
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 0, recipientTag: 0, manifestTag: 0, payloadOffset: 1000);

            Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header)));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_the_declared_payload_offset_is_less_than_32")]
        public void Should_throw_ArteryEnvelopeException_When_the_declared_payload_offset_is_less_than_32()
        {
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: 0, recipientTag: 0, manifestTag: 0, payloadOffset: 10);

            Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header)));
        }

        [Fact(DisplayName = "Should_throw_ArteryEnvelopeException_When_encoding_a_literal_over_64KB")]
        public void Should_throw_ArteryEnvelopeException_When_encoding_a_literal_over_64KB()
        {
            var oversizedSenderPath = new string('a', 70_000);
            var destination = new byte[200_000];

            Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Encode(
                destination, originUid: 1L, serializerId: 1,
                senderPath: oversizedSenderPath, recipientPath: null, manifest: "", ReadOnlySpan<byte>.Empty));
        }

        // ===================== COMPRESSED tag decode =====================

        [Fact(DisplayName = "Should_surface_the_compressed_index_and_report_TryGet_false_When_a_tag_is_compressed")]
        public void Should_surface_the_compressed_index_and_report_TryGet_false_When_a_tag_is_compressed()
        {
            const uint compressedSenderTag = 0xFF00_0000u | 7u;
            var header = BuildRawHeader(version: 1, flags: 0, originUid: 0, serializerId: 0,
                senderTag: compressedSenderTag, recipientTag: 0, manifestTag: 0, payloadOffset: HeaderLength);

            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header));

            Assert.Equal(ArteryTagKind.Compressed, decoded.SenderKind);
            Assert.Equal(7, decoded.SenderCompressedIndex);
            Assert.False(decoded.TryGetSenderPath(out var path));
            Assert.Null(path);
        }

        // ===================== helpers =====================

        private static byte[] MakePayload(int length, int seed)
        {
            var payload = new byte[length];
            new Random(seed).NextBytes(payload);
            return payload;
        }

        private static (byte[] frame, int totalLength) EncodeToFrame(
            long originUid, int serializerId, string? senderPath, string? recipientPath, string manifest, ReadOnlySpan<byte> payload)
        {
            var destination = new byte[ArteryEnvelopeCodec.MaxEncodedSize(senderPath, recipientPath, manifest, payload.Length)];
            var totalLength = ArteryEnvelopeCodec.Encode(destination, originUid, serializerId, senderPath, recipientPath, manifest, payload);
            Assert.Equal(destination.Length, totalLength);
            return (destination, totalLength);
        }

        private static int ReadFrameLength(byte[] frame) => (int)BinaryPrimitives.ReadUInt32LittleEndian(frame);

        private static ReadOnlySequence<byte> EnvelopeBody(byte[] frame) =>
            new(frame, FrameLengthFieldLength, frame.Length - FrameLengthFieldLength);

        private static int LiteralWireSize(string? value) =>
            string.IsNullOrEmpty(value) ? 0 : 2 + Encoding.UTF8.GetByteCount(value);

        private static byte[] BuildRawHeader(
            byte version, byte flags, long originUid, int serializerId,
            uint senderTag, uint recipientTag, uint manifestTag, uint payloadOffset)
        {
            var header = new byte[HeaderLength];
            header[0] = version;
            header[1] = flags;
            header[2] = 0;
            header[3] = 0;
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(4), originUid);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), serializerId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), senderTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), recipientTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), manifestTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), payloadOffset);
            return header;
        }

        /// <summary>Splits <paramref name="bytes"/> into two chained segments at <paramref name="splitAt"/>, for multi-segment decode tests.</summary>
        private static ReadOnlySequence<byte> TwoSegment(byte[] bytes, int splitAt)
        {
            if (splitAt <= 0 || splitAt >= bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(splitAt));

            var first = new Segment(bytes.AsMemory(0, splitAt));
            var second = first.Append(bytes.AsMemory(splitAt));
            return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

            public Segment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = segment;
                return segment;
            }
        }
    }

    /// <summary>
    /// Covers <see cref="ArteryEnvelopeCodec"/>'s V2 single-pass encode overload, which needs a live
    /// <c>ActorSystem</c>'s <see cref="Akka.Serialization.Serialization"/> extension to resolve a
    /// serializer + manifest for a real message object.
    /// </summary>
    public class ArteryEnvelopeCodecV2Spec : AkkaSpec
    {
        public ArteryEnvelopeCodecV2Spec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "Should_round_trip_a_serialized_message_When_using_the_V2_single_pass_encode_overload")]
        public void Should_round_trip_a_serialized_message_When_using_the_V2_single_pass_encode_overload()
        {
            const string senderPath = "akka://Sys@host:1/user/sender";
            const string recipientPath = "akka://Sys@host:1/user/recipient";
            const long originUid = 0x0102_0304_0506_0708L;
            const string message = "hello artery, this is a real message payload";

            using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, originUid, senderPath, recipientPath, message);

            var frameLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(writer.WrittenSpan);
            Assert.Equal(writer.WrittenCount - 4, frameLength);

            var body = new ReadOnlySequence<byte>(writer.WrittenMemory.Slice(4));
            var decoded = ArteryEnvelopeCodec.Decode(body);

            Assert.Equal(originUid, decoded.Header.OriginUid);

            Assert.True(decoded.TryGetSenderPath(out var decodedSender));
            Assert.Equal(senderPath, decodedSender);

            Assert.True(decoded.TryGetRecipientPath(out var decodedRecipient));
            Assert.Equal(recipientPath, decodedRecipient);

            Assert.True(decoded.TryGetManifest(out var decodedManifest));

            var deserialized = Sys.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, decodedManifest);
            Assert.Equal(message, deserialized);
        }

        [Fact(DisplayName = "Should_encode_absent_sender_and_recipient_tags_When_none_are_supplied_to_the_V2_overload")]
        public void Should_encode_absent_sender_and_recipient_tags_When_none_are_supplied_to_the_V2_overload()
        {
            const string message = "no sender, no recipient";

            using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, originUid: 1L, senderPath: null, recipientPath: null, message);

            var body = new ReadOnlySequence<byte>(writer.WrittenMemory.Slice(4));
            var decoded = ArteryEnvelopeCodec.Decode(body);

            Assert.Equal(ArteryTagKind.Absent, decoded.SenderKind);
            Assert.True(decoded.TryGetSenderPath(out var sender));
            Assert.Null(sender);

            Assert.Equal(ArteryTagKind.Absent, decoded.RecipientKind);
            Assert.True(decoded.TryGetRecipientPath(out var recipient));
            Assert.Null(recipient);

            var deserialized = Sys.Serialization.Deserialize(decoded.Payload, decoded.Header.SerializerId, decoded.TryGetManifest(out var m) ? m : string.Empty);
            Assert.Equal(message, deserialized);
        }

        [Theory(DisplayName = "Should_restore_transport_context_and_release_buffer_When_encoding_succeeds_or_fails")]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void Should_restore_transport_context_and_release_buffer_When_encoding_succeeds_or_fails(int context, bool fail)
        {
            var system = (ExtendedActorSystem)Sys;
            var serializer = new ContextCheckingSerializer(system, fail);
            Sys.Serialization.AddSerializer("context-checking", serializer);
            Sys.Serialization.AddSerializationMap(typeof(ContextMessage), serializer);
            var pool = new TrackingArrayPool();
            var saved = Akka.Serialization.Serialization.CurrentTransportInformation;
            var previous = context switch
            {
                0 => null,
                1 => system.Provider.SerializationInformation,
                _ => new Information(new Address("akka", "outer", "localhost", 1234), Sys)
            };

            try
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = previous;
                if (fail)
                {
                    var exception = Assert.Throws<InvalidOperationException>(() =>
                        ArteryEnvelopeCodec.Encode(Sys.Serialization, 42L, "sender", "recipient", new ContextMessage(), pool));
                    Assert.Same(serializer.Failure, exception);
                }
                else
                {
                    using var writer = ArteryEnvelopeCodec.Encode(
                        Sys.Serialization, 42L, "sender", "recipient", new ContextMessage(), pool);
                    var expected = new byte[ArteryEnvelopeCodec.MaxEncodedSize("sender", "recipient", "ctx", 1)];
                    var length = ArteryEnvelopeCodec.Encode(expected, 42L, serializer.Identifier,
                        "sender", "recipient", "ctx", new byte[] { 0x42 });
                    Assert.Equal(expected.AsSpan(0, length).ToArray(), writer.WrittenMemory.ToArray());
                }

                Assert.Same(previous, Akka.Serialization.Serialization.CurrentTransportInformation);
                Assert.Equal(1, pool.Rents);
                Assert.Equal(pool.Rents, pool.Returns);
            }
            finally
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = saved;
            }
        }

        [Theory(DisplayName = "Should_preserve_UTF8_wire_bytes_When_metadata_contains_unicode_or_unpaired_surrogates")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Should_preserve_UTF8_wire_bytes_When_metadata_contains_unicode_or_unpaired_surrogates(int kind)
        {
            var literal = kind switch
            {
                0 => "Ω送😀",
                1 => "\ud800",
                _ => "\udc00"
            };
            var expectedBytes = kind == 0
                ? new byte[] { 0xCE, 0xA9, 0xE9, 0x80, 0x81, 0xF0, 0x9F, 0x98, 0x80 }
                : new byte[] { 0xEF, 0xBF, 0xBD };
            var serializer = new ContextCheckingSerializer((ExtendedActorSystem)Sys, false, literal);
            Sys.Serialization.AddSerializer("unicode-metadata", serializer);
            Sys.Serialization.AddSerializationMap(typeof(ContextMessage), serializer);

            using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42L, literal, literal, new ContextMessage());
            var explicitFrame = new byte[ArteryEnvelopeCodec.MaxEncodedSize(literal, literal, literal, 1)];
            var length = ArteryEnvelopeCodec.Encode(explicitFrame, 42L, serializer.Identifier,
                literal, literal, literal, new byte[] { 0x42 });
            Assert.Equal(explicitFrame.AsSpan(0, length).ToArray(), writer.WrittenSpan.ToArray());

            // Independent expected UTF-8 bytes pin replacement fallback and supplementary characters.
            // Every literal is [u16 LE byte count][bytes], immediately following the fixed header.
            var cursor = ArteryEnvelopeHeader.FrameLengthFieldLength + ArteryEnvelopeHeader.HeaderLength;
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(expectedBytes.Length, BinaryPrimitives.ReadUInt16LittleEndian(writer.WrittenSpan.Slice(cursor, 2)));
                cursor += 2;
                Assert.Equal(expectedBytes, writer.WrittenSpan.Slice(cursor, expectedBytes.Length).ToArray());
                cursor += expectedBytes.Length;
            }
            Assert.Equal(cursor + 1, writer.WrittenCount);
            Assert.Equal(0x42, writer.WrittenSpan[cursor]);
        }

        [Theory(DisplayName = "Should_enforce_literal_byte_limit_When_metadata_uses_multibyte_UTF8")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_enforce_literal_byte_limit_When_metadata_uses_multibyte_UTF8(bool oversized)
        {
            var literal = new string('Ω', 32767) + (oversized ? "Ω" : "a");
            if (oversized)
            {
                Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Encode(
                    new byte[70000], 42L, 4, literal, null, string.Empty, ReadOnlySpan<byte>.Empty));
                Assert.Throws<ArteryEnvelopeException>(() => ArteryEnvelopeCodec.Encode(
                    Sys.Serialization, 42L, literal, null, Array.Empty<byte>()));
                return;
            }

            using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42L, literal, null, Array.Empty<byte>());
            var explicitFrame = new byte[ArteryEnvelopeCodec.MaxEncodedSize(literal, null, string.Empty, 0)];
            ArteryEnvelopeCodec.Encode(explicitFrame, 42L, 4, literal, null, string.Empty, ReadOnlySpan<byte>.Empty);
            Assert.Equal(explicitFrame, writer.WrittenSpan.ToArray());
            Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(writer.WrittenSpan.Slice(36, 2)));
            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(writer.WrittenMemory.Slice(4)));
            Assert.True(decoded.TryGetSenderPath(out var sender));
            Assert.Equal(literal, sender);
        }

        private sealed class ContextMessage
        {
        }

        private sealed class ContextCheckingSerializer(ExtendedActorSystem system, bool fail, string manifest = "ctx") : SerializerV2(system)
        {
            public override int Identifier => 987654;
            public InvalidOperationException Failure { get; } = new("serializer failed after writing");

            public override string Manifest(object obj)
            {
                Assert.Same(system.Provider.SerializationInformation, Akka.Serialization.Serialization.GetCurrentTransportInformation());
                return manifest;
            }

            public override int SizeHint(object obj)
            {
                Assert.Same(system.Provider.SerializationInformation, Akka.Serialization.Serialization.GetCurrentTransportInformation());
                return 1;
            }

            public override int Serialize(object obj, IBufferWriter<byte> writer)
            {
                Assert.Same(system.Provider.SerializationInformation, Akka.Serialization.Serialization.GetCurrentTransportInformation());
                writer.GetSpan(1)[0] = 0x42;
                writer.Advance(1);
                if (fail)
                    throw Failure;
                return 1;
            }

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new ContextMessage();
        }

        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            public int Rents { get; private set; }
            public int Returns { get; private set; }

            public override byte[] Rent(int minimumLength)
            {
                Rents++;
                return new byte[minimumLength];
            }

            public override void Return(byte[] array, bool clearArray = false) => Returns++;
        }
    }
}
