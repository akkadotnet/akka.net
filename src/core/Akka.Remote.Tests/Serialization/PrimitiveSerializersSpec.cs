//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class PrimitiveSerializersSpec : AkkaSpec
    {
        // Hex captured from the pre-V2 (SerializerWithStringManifest) implementation using ToBinary.
        // Columns: value, wire bytes, value read back from those bytes.
        // The lone surrogate cannot round-trip: UTF-8 encoding replaces it with U+FFFD.
        public static IEnumerable<object[]> GoldenCorpus()
        {
            yield return new object[] { "", "", "" };
            yield return new object[] { "hello", "68656C6C6F", "hello" };
            yield return new object[]
            {
                "árvíztűrőütvefúrógép",
                "C3A17276C3AD7A74C5B172C591C3BC74766566C3BA72C3B367C3A970",
                "árvíztűrőütvefúrógép"
            };
            yield return new object[]
            {
                "日本語テキスト", "E697A5E69CACE8AA9EE38386E382ADE382B9E38388", "日本語テキスト"
            };
            yield return new object[] { "a😀b𝄞c", "61F09F988062F09D849E63", "a😀b𝄞c" };
            yield return new object[] { "\uD800", "EFBFBD", "�" };
            yield return new object[] { "a\u0000b", "610062", "a\u0000b" };
            yield return new object[] { 0, "00000000", 0 };
            yield return new object[] { 1, "01000000", 1 };
            yield return new object[] { -1, "FFFFFFFF", -1 };
            yield return new object[] { int.MinValue, "00000080", int.MinValue };
            yield return new object[] { int.MaxValue, "FFFFFF7F", int.MaxValue };
            yield return new object[] { 0L, "0000000000000000", 0L };
            yield return new object[] { 1L, "0100000000000000", 1L };
            yield return new object[] { -1L, "FFFFFFFFFFFFFFFF", -1L };
            yield return new object[] { long.MinValue, "0000000000000080", long.MinValue };
            yield return new object[] { long.MaxValue, "FFFFFFFFFFFFFF7F", long.MaxValue };
        }

        // Every manifest spelling the serializer reads, paired with a value and its legacy bytes.
        public static IEnumerable<object[]> ManifestSpellings()
        {
            foreach (var m in new[]
                     {
                         PrimitiveSerializers.StringManifest, PrimitiveSerializers.StringManifestNetCore,
                         PrimitiveSerializers.StringManifestNetFx
                     })
                yield return new object[] { m, "C3A17276C3AD7A74C5B1", "árvíztű" };

            foreach (var m in new[]
                     {
                         PrimitiveSerializers.Int32Manifest, PrimitiveSerializers.Int32ManifestNetCore,
                         PrimitiveSerializers.Int32ManifestNetFx
                     })
                yield return new object[] { m, "00000080", int.MinValue };

            foreach (var m in new[]
                     {
                         PrimitiveSerializers.Int64Manifest, PrimitiveSerializers.Int64ManifestNetCore,
                         PrimitiveSerializers.Int64ManifestNetFx
                     })
                yield return new object[] { m, "FFFFFFFFFFFFFF7F", long.MaxValue };
        }

        // 156,000 bytes of UTF-8 (96,000 chars): 1, 2, 3 and 4-byte characters repeated.
        private const string LargeUnit = "abcdé日😀";
        private const int LargeRepeats = 12_000;
        private const int LargeByteLength = 156_000;
        private const string LargeSha256 = "D9F04E2C1D4490684CF2E03178713895CF33C1C69170CA2D8D5D823630AC7A45";

        private static string LargeString() => string.Concat(Enumerable.Repeat(LargeUnit, LargeRepeats));

        public PrimitiveSerializersSpec(ITestOutputHelper output) : base(output, RemoteConfigFactory.Default())
        {
        }

        private PrimitiveSerializers NewSerializer(string settings) =>
            new((ExtendedActorSystem)Sys, ConfigurationFactory.ParseString(settings));

        [Theory(DisplayName = "Should_RoundTrip_When_SerializingInt32")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MinValue + 1)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MaxValue - 1)]
        public void Can_serialize_Int32(int value)
        {
            AssertEqual(value);
        }

        [Theory(DisplayName = "Should_RoundTrip_When_SerializingInt64")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MinValue + 1L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MaxValue - 1L)]
        public void Can_serialize_Int64(long value)
        {
            AssertEqual(value);
        }

        [Theory(DisplayName = "Should_RoundTrip_When_SerializingString")]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("árvíztűrőütvefúrógép")]
        public void Can_serialize_String(string value)
        {
            AssertEqual(value);
        }

        [Theory(DisplayName = "Should_WriteLegacyBytes_When_SerializingIntoWriter")]
        [MemberData(nameof(GoldenCorpus))]
        public void Serialize_writes_the_legacy_bytes(object value, string expectedHex, object _)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(value);
            var writer = new ArrayBufferWriter<byte>();

            var written = serializer.Serialize(value, writer);

            Convert.ToHexString(writer.WrittenSpan).Should().Be(expectedHex);
            written.Should().Be(writer.WrittenCount);
            serializer.SizeHint(value).Should().Be(writer.WrittenCount);
        }

        [Theory(DisplayName = "Should_WriteLegacyBytes_When_CallingToBinary")]
        [MemberData(nameof(GoldenCorpus))]
        public void ToBinary_writes_the_legacy_bytes(object value, string expectedHex, object _)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(value);

            Convert.ToHexString(serializer.ToBinary(value)).Should().Be(expectedHex);
        }

        [Fact(DisplayName = "Should_WriteLegacyBytes_When_SerializingLargeString")]
        public void Large_string_matches_the_legacy_bytes()
        {
            var value = LargeString();
            var serializer = Sys.Serialization.FindSerializerV2For(value);
            var writer = new ArrayBufferWriter<byte>();

            var written = serializer.Serialize(value, writer);
            var viaToBinary = serializer.ToBinary(value);

            written.Should().Be(LargeByteLength);
            serializer.SizeHint(value).Should().Be(LargeByteLength);
            Convert.ToHexString(SHA256.HashData(writer.WrittenSpan)).Should().Be(LargeSha256);
            viaToBinary.Length.Should().Be(LargeByteLength);
            Convert.ToHexString(SHA256.HashData(viaToBinary)).Should().Be(LargeSha256);

            serializer.Deserialize(new ReadOnlySequence<byte>(viaToBinary), PrimitiveSerializers.StringManifest)
                .Should().Be(value);
        }

        [Theory(DisplayName = "Should_ReadLegacyBytes_When_GivenAnyManifestSpelling")]
        [MemberData(nameof(ManifestSpellings))]
        public void Reads_legacy_bytes_for_every_manifest_spelling(string manifest, string hex, object expected)
        {
            var serializer = Sys.Serialization.FindSerializerV2For("x");
            var bytes = Convert.FromHexString(hex);

            serializer.FromBinary(bytes, manifest).Should().Be(expected);
            serializer.Deserialize(new ReadOnlySequence<byte>(bytes), manifest).Should().Be(expected);
        }

        [Theory(DisplayName = "Should_ReadLegacyBytes_When_ReadingGoldenCorpus")]
        [MemberData(nameof(GoldenCorpus))]
        public void Reads_the_golden_corpus(object value, string hex, object expected)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(value);
            var bytes = Convert.FromHexString(hex);
            var manifest = serializer.Manifest(value);

            serializer.FromBinary(bytes, manifest).Should().Be(expected);
            serializer.Deserialize(new ReadOnlySequence<byte>(bytes), manifest).Should().Be(expected);
        }

        [Fact(DisplayName = "Should_ThrowArgumentException_When_ManifestIsUnknown")]
        public void Unknown_manifest_throws()
        {
            var serializer = Sys.Serialization.FindSerializerV2For("x");
            var bytes = new byte[] { 1, 2, 3, 4 };

            var fromBinary = () => serializer.FromBinary(bytes, "bogus");
            var deserialize = () => serializer.Deserialize(new ReadOnlySequence<byte>(bytes), "bogus");

            fromBinary.Should().Throw<ArgumentException>()
                .WithMessage("Unimplemented deserialization of message with manifest [bogus] in *");
            deserialize.Should().Throw<ArgumentException>()
                .WithMessage("Unimplemented deserialization of message with manifest [bogus] in *");
        }

        [Fact(DisplayName = "Should_DecodeString_When_SplitAtEveryByteOffset")]
        public void Strings_decode_across_every_split_point()
        {
            var serializer = Sys.Serialization.FindSerializerV2For("x");
            foreach (var text in new[] { "", "hello", "árvíztűrőütvefúrógép", "日本語テキスト", "a😀b𝄞c", "a\u0000b" })
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                for (var split = 0; split <= bytes.Length; split++)
                {
                    var sequence = Segments(bytes, split);
                    serializer.Deserialize(sequence, PrimitiveSerializers.StringManifest)
                        .Should().Be(text, "[{0}] split at byte {1}", text, split);
                }

                // one byte per segment: splits every multi-byte character
                serializer.Deserialize(Segments(bytes, Enumerable.Range(1, bytes.Length).ToArray()),
                    PrimitiveSerializers.StringManifest).Should().Be(text);
            }
        }

        [Fact(DisplayName = "Should_DecodeLikeSingleSegment_When_Utf8IsMalformedAndSplit")]
        public void Malformed_utf8_decodes_the_same_across_segments()
        {
            var serializer = Sys.Serialization.FindSerializerV2For("x");
            // lone continuation byte, truncated 3-byte and 4-byte sequences, overlong lead byte
            var bytes = new byte[] { 0x61, 0x80, 0xE6, 0x97, 0x62, 0xF0, 0x9F, 0x98, 0xC0, 0x80, 0x63, 0xE6, 0x97 };
            var expected = Encoding.UTF8.GetString(bytes);

            for (var split = 0; split <= bytes.Length; split++)
            {
                serializer.Deserialize(Segments(bytes, split), PrimitiveSerializers.StringManifest)
                    .Should().Be(expected, "split at byte {0}", split);
            }
        }

        [Fact(DisplayName = "Should_DecodeLargeString_When_SplitIntoManySegments")]
        public void Large_string_decodes_across_segments()
        {
            var serializer = Sys.Serialization.FindSerializerV2For("x");
            var value = LargeString();
            var bytes = Encoding.UTF8.GetBytes(value);
            // 7 bytes per segment never lines up with the 13-byte unit, so characters straddle segments
            var splits = Enumerable.Range(1, (bytes.Length - 1) / 7).Select(i => i * 7).ToArray();

            serializer.Deserialize(Segments(bytes, splits), PrimitiveSerializers.StringManifest)
                .Should().Be(value);
        }

        [Fact(DisplayName = "Should_ReadInt32_When_SplitAtEveryByteOffset")]
        public void Int32_reads_across_every_split_point()
        {
            var serializer = Sys.Serialization.FindSerializerV2For(1);
            foreach (var value in new[] { 0, 1, -1, int.MinValue, int.MaxValue, 0x01020304 })
            {
                var bytes = serializer.ToBinary(value);
                for (var split = 0; split <= bytes.Length; split++)
                {
                    serializer.Deserialize(Segments(bytes, split), PrimitiveSerializers.Int32Manifest)
                        .Should().Be(value, "[{0}] split at byte {1}", value, split);
                }

                serializer.Deserialize(Segments(bytes, 1, 2, 3), PrimitiveSerializers.Int32Manifest).Should().Be(value);
            }
        }

        [Fact(DisplayName = "Should_ReadInt64_When_SplitAtEveryByteOffset")]
        public void Int64_reads_across_every_split_point()
        {
            var serializer = Sys.Serialization.FindSerializerV2For(1L);
            foreach (var value in new[] { 0L, 1L, -1L, long.MinValue, long.MaxValue, 0x0102030405060708L })
            {
                var bytes = serializer.ToBinary(value);
                for (var split = 0; split <= bytes.Length; split++)
                {
                    serializer.Deserialize(Segments(bytes, split), PrimitiveSerializers.Int64Manifest)
                        .Should().Be(value, "[{0}] split at byte {1}", value, split);
                }

                serializer.Deserialize(Segments(bytes, 1, 2, 3, 4, 5, 6, 7), PrimitiveSerializers.Int64Manifest)
                    .Should().Be(value);
            }
        }

        [Fact(DisplayName = "Should_ReadFirstBytesOnly_When_IntInputHasTrailingBytes")]
        public void Int_reads_ignore_trailing_bytes_like_the_legacy_reader()
        {
            var serializer = Sys.Serialization.FindSerializerV2For(1);

            serializer.FromBinary(new byte[] { 1, 0, 0, 0, 99, 99 }, PrimitiveSerializers.Int32Manifest)
                .Should().Be(1);
            serializer.FromBinary(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 99 }, PrimitiveSerializers.Int64Manifest)
                .Should().Be(2L);
            serializer.Deserialize(Segments(new byte[] { 1, 0, 0, 0, 99 }, 2), PrimitiveSerializers.Int32Manifest)
                .Should().Be(1);
        }

        [Theory(DisplayName = "Should_Throw_When_Int32InputIsTooShort")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Short_int32_input_throws(int length)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(1);
            var bytes = new byte[length];

            AssertThrowsTooShort(serializer, bytes, PrimitiveSerializers.Int32Manifest);
        }

        [Theory(DisplayName = "Should_Throw_When_Int64InputIsTooShort")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(7)]
        public void Short_int64_input_throws(int length)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(1L);
            var bytes = new byte[length];

            AssertThrowsTooShort(serializer, bytes, PrimitiveSerializers.Int64Manifest);
        }

        private static void AssertThrowsTooShort(SerializerV2 serializer, byte[] bytes, string manifest)
        {
            var viaBytes = () => serializer.FromBinary(bytes, manifest);
            viaBytes.Should().Throw<ArgumentException>();

            var viaSequence = () => serializer.Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
            viaSequence.Should().Throw<ArgumentException>();

            // too short in total, even though the data spans segments
            for (var split = 0; split <= bytes.Length; split++)
            {
                var sequence = Segments(bytes, split);
                var viaSegments = () => serializer.Deserialize(sequence, manifest);
                viaSegments.Should().Throw<ArgumentException>();
            }
        }

        [Theory(DisplayName = "Should_ReportExactSize_When_WriterReturnsExactOrOversizedSpans")]
        [MemberData(nameof(GoldenCorpus))]
        public void Serialize_works_with_any_conforming_writer(object value, string expectedHex, object _)
        {
            var serializer = Sys.Serialization.FindSerializerV2For(value);
            var expected = Convert.FromHexString(expectedHex);

            foreach (var extra in new[] { 0, 1, 64 })
            {
                var writer = new FreshSpanWriter(extra);
                writer.GetSpan(1)[0] = 0xEE; // existing content in front of ours
                writer.Advance(1);

                var written = serializer.Serialize(value, writer);

                written.Should().Be(expected.Length);
                serializer.SizeHint(value).Should().Be(written);
                writer.Written.Should().Equal(new byte[] { 0xEE }.Concat(expected));
            }
        }

        [Fact(DisplayName = "Should_ResolveNativeV2_When_FindingSerializerForString")]
        public void String_resolves_to_the_serializer_itself()
        {
            AssertResolvesNatively("x");
        }

        [Fact(DisplayName = "Should_ResolveNativeV2_When_FindingSerializerForInt32")]
        public void Int32_resolves_to_the_serializer_itself()
        {
            AssertResolvesNatively(42);
        }

        [Fact(DisplayName = "Should_ResolveNativeV2_When_FindingSerializerForInt64")]
        public void Int64_resolves_to_the_serializer_itself()
        {
            AssertResolvesNatively(42L);
        }

        private void AssertResolvesNatively(object value)
        {
            var v2 = Sys.Serialization.FindSerializerV2For(value);

            v2.Should().BeOfType<PrimitiveSerializers>();
            v2.Identifier.Should().Be(17);
            Sys.Serialization.FindSerializerFor(value).Should().BeSameAs(v2);
        }

        [Theory(DisplayName = "Should_ReturnShortManifests_When_LegacyBehaviorIsOff")]
        [InlineData("x", "S")]
        [InlineData(1, "I")]
        [InlineData(1L, "L")]
        public void Manifest_is_short_when_legacy_behavior_is_off(object value, string expected)
        {
            NewSerializer("use-legacy-behavior = off").Manifest(value).Should().Be(expected);
        }

        [Theory(DisplayName = "Should_ReturnTypeNames_When_LegacyBehaviorIsOn")]
        [InlineData("x", "System.String, System.Private.CoreLib")]
        [InlineData(1, "System.Int32, System.Private.CoreLib")]
        [InlineData(1L, "System.Int64, System.Private.CoreLib")]
        public void Manifest_is_the_type_name_when_legacy_behavior_is_on(object value, string expected)
        {
            var manifest = NewSerializer("use-legacy-behavior = on").Manifest(value);

            manifest.Should().Be(expected);
            manifest.Should().Be(value.GetType().TypeQualifiedName());
        }

        [Fact(DisplayName = "Should_ThrowArgumentException_When_ManifestForUnsupportedType")]
        public void Manifest_for_unsupported_type_throws_unless_legacy()
        {
            var off = () => NewSerializer("use-legacy-behavior = off").Manifest(1.5);
            off.Should().Throw<ArgumentException>()
                .WithMessage("Cannot serialize object of type [System.Double] in [Akka.Remote.Serialization.PrimitiveSerializers]");

            // legacy mode never rejected a type at manifest time
            NewSerializer("use-legacy-behavior = on").Manifest(1.5).Should().Be(typeof(double).TypeQualifiedName());
        }

        [Fact(DisplayName = "Should_ThrowArgumentException_When_SerializingUnsupportedType")]
        public void Unsupported_type_throws()
        {
            var serializer = NewSerializer("use-legacy-behavior = off");
            const string message = "Cannot serialize object of type [System.Double]";

            var toBinary = () => serializer.ToBinary(1.5);
            var serialize = () => serializer.Serialize(1.5, new ArrayBufferWriter<byte>());
            var sizeHint = () => serializer.SizeHint(1.5);

            toBinary.Should().Throw<ArgumentException>().WithMessage(message);
            serialize.Should().Throw<ArgumentException>().WithMessage(message);
            sizeHint.Should().Throw<ArgumentException>().WithMessage(message);
        }

        [Fact(DisplayName = "Should_ThrowConfigurationException_When_ConfigIsNull")]
        public void Null_config_throws()
        {
            var create = () => new PrimitiveSerializers((ExtendedActorSystem)Sys, null!);

            create.Should().Throw<ConfigurationException>().WithMessage("configuration is null");
        }

        private T AssertAndReturn<T>(T message) where T : notnull
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<PrimitiveSerializers>();
            var serializedBytes = serializer.ToBinary(message);
            var manifest = serializer.Manifest(message);
            return (T)serializer.FromBinary(serializedBytes, manifest);
        }

        private T AssertCrossPlatformAndReturn<T>(T message) where T : notnull
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<PrimitiveSerializers>();
            var serializedBytes = serializer.ToBinary(message);
            // GetType() will make sure that each namespace is compatible with the serializer
            // as the test is run on each platform.
            return (T)serializer.FromBinary(serializedBytes, message.GetType());
        }

        private void AssertEqual<T>(T message) where T : notnull
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
            deserialized = AssertCrossPlatformAndReturn(message);
            Assert.Equal(message, deserialized);
        }

        /// <summary>
        /// Builds a multi-segment sequence by cutting <paramref name="data"/> at each of the (ascending)
        /// <paramref name="splits"/> offsets. A split at 0 or at the end makes an empty segment.
        /// </summary>
        private static ReadOnlySequence<byte> Segments(byte[] data, params int[] splits)
        {
            var bounds = new List<int> { 0 };
            bounds.AddRange(splits);
            bounds.Add(data.Length);

            TestSegment? first = null;
            TestSegment? last = null;
            for (var i = 0; i < bounds.Count - 1; i++)
            {
                var memory = new ReadOnlyMemory<byte>(data, bounds[i], bounds[i + 1] - bounds[i]);
                last = last == null ? first = new TestSegment(memory) : last.Append(memory);
            }

            return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
        }

        private sealed class TestSegment : ReadOnlySequenceSegment<byte>
        {
            public TestSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public TestSegment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new TestSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }

        /// <summary>
        /// A writer that hands out a new, garbage-filled buffer on every request, exactly <c>hint + extra</c> long,
        /// so the serializer cannot get by on stale bytes or on more room than it asked for.
        /// </summary>
        private sealed class FreshSpanWriter : IBufferWriter<byte>
        {
            private readonly int _extra;
            private readonly List<byte> _written = new();
            private byte[] _current = Array.Empty<byte>();

            public FreshSpanWriter(int extra)
            {
                _extra = extra;
            }

            public IReadOnlyList<byte> Written => _written;

            public void Advance(int count)
            {
                if (count < 0 || count > _current.Length)
                    throw new ArgumentOutOfRangeException(nameof(count));

                _written.AddRange(_current.AsSpan(0, count).ToArray());
                _current = Array.Empty<byte>();
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                _current = new byte[Math.Max(sizeHint, 1) + _extra];
                _current.AsSpan().Fill(0xAA);
                return _current;
            }

            public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
        }
    }
}
