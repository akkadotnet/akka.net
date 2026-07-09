//-----------------------------------------------------------------------
// <copyright file="CompressionTableSpec.cs" company="Akka.NET Project">
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
    /// Unit tests for the Artery ref/manifest compression tables and the pure tag codec
    /// (<see cref="CompressionTable{T}"/>, <see cref="DecompressionTable{T}"/>,
    /// <see cref="CompressionTagCodec"/>) -- see
    /// <c>openspec/changes/artery-ref-manifest-compression/design.md</c>, Decisions 1-4. These cover
    /// the primitives in isolation; the encode/decode wire round-trip is in
    /// <see cref="ArteryCompressionCodecSpec"/>.
    /// </summary>
    public class CompressionTableSpec
    {
        private static CompressionTable<string> Table(byte version, params string[] values)
        {
            var dict = new Dictionary<string, int>();
            for (var i = 0; i < values.Length; i++)
                dict[values[i]] = i; // dense 0..N-1

            return new CompressionTable<string>(originUid: 42L, version, dict);
        }

        // ===================== CompressionTable.Compress =====================

        [Fact(DisplayName = "CompressionTable Compress should return the dense index for a known value")]
        public void Compress_returns_index_for_known_value()
        {
            var table = Table(3, "/user/a", "/user/b", "/user/c");

            table.Compress("/user/a").Should().Be(0);
            table.Compress("/user/b").Should().Be(1);
            table.Compress("/user/c").Should().Be(2);
        }

        [Fact(DisplayName = "CompressionTable Compress should return NotCompressedId (-1) on a miss")]
        public void Compress_returns_minus_one_on_miss()
        {
            var table = Table(3, "/user/a");

            table.Compress("/user/absent").Should().Be(CompressionTable<string>.NotCompressedId);
            CompressionTable<string>.NotCompressedId.Should().Be(-1);
        }

        [Fact(DisplayName = "CompressionTable Empty should compress nothing")]
        public void Empty_table_compresses_nothing()
        {
            CompressionTable<string>.Empty.Compress("/user/a").Should().Be(CompressionTable<string>.NotCompressedId);
            CompressionTable<string>.Empty.Dictionary.Count.Should().Be(0);
            CompressionTable<string>.Empty.Version.Should().Be(0);
        }

        // ===================== Invert / DecompressionTable.Get =====================

        [Fact(DisplayName = "Invert should round-trip a dense value->index table into an index->value table")]
        public void Invert_round_trips_dense_indices()
        {
            var table = Table(7, "/user/a", "/user/b", "/user/c");
            var inverted = table.Invert();

            inverted.OriginUid.Should().Be(42L);
            inverted.Version.Should().Be((byte)7);
            inverted.Length.Should().Be(3);
            inverted.Get(0).Should().Be("/user/a");
            inverted.Get(1).Should().Be("/user/b");
            inverted.Get(2).Should().Be("/user/c");
        }

        [Fact(DisplayName = "Invert of an empty table should yield an empty decompression table")]
        public void Invert_of_empty_is_empty()
        {
            var inverted = CompressionTable<string>.Empty.Invert();
            inverted.Length.Should().Be(0);
        }

        [Fact(DisplayName = "DecompressionTable Get should throw for an out-of-range index")]
        public void Get_throws_out_of_range()
        {
            var inverted = Table(1, "/user/a", "/user/b").Invert();

            Assert.Throws<ArgumentOutOfRangeException>(() => inverted.Get(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => inverted.Get(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecompressionTable<string>.Empty.Get(0));
        }

        [Fact(DisplayName = "DecompressionTable Empty and Disabled should have the expected versions")]
        public void Empty_and_disabled_decompression_tables()
        {
            DecompressionTable<string>.Empty.Version.Should().Be((byte)0);
            DecompressionTable<string>.Empty.Length.Should().Be(0);

            DecompressionTable<string>.Disabled.Version.Should().Be(DecompressionTable<string>.DisabledVersion);
            DecompressionTable<string>.DisabledVersion.Should().Be((byte)0xFF);
        }

        // ===================== version wrap (Q3) =====================

        [Theory(DisplayName = "IncrementVersion should cycle 0..127 and wrap 127->0")]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(126, 127)]
        [InlineData(127, 0)]   // wrap
        [InlineData(0xFF, 0)]  // disabled sentinel advances to the first real version
        public void IncrementVersion_wraps(int current, int expected)
        {
            CompressionTable<string>.IncrementVersion((byte)current).Should().Be((byte)expected);
        }

        [Fact(DisplayName = "Version bounds constants should be 0 and 127")]
        public void Version_bounds()
        {
            CompressionTable<string>.MinVersion.Should().Be((byte)0);
            CompressionTable<string>.MaxVersion.Should().Be((byte)127);
        }

        // ===================== CompressionTagCodec =====================

        [Fact(DisplayName = "MakeCompressedTag should set the marker top byte and carry the 16-bit index")]
        public void MakeCompressedTag_sets_marker_and_index()
        {
            var tag = CompressionTagCodec.MakeCompressedTag(7);

            (tag & ArteryEnvelopeHeader.CompressedTagMask).Should().NotBe(0u, "a COMPRESSED tag has a non-zero top byte");
            (tag & ArteryEnvelopeHeader.CompressedIndexMask).Should().Be(7u);
            tag.Should().Be(ArteryEnvelopeHeader.CompressedTagMarker | 7u);
        }

        [Theory(DisplayName = "MakeCompressedTag should accept the full 16-bit index space and reject beyond it")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(65535)]
        public void MakeCompressedTag_accepts_index_space(int idx)
        {
            var tag = CompressionTagCodec.MakeCompressedTag(idx);
            (tag & ArteryEnvelopeHeader.CompressedIndexMask).Should().Be((uint)idx);
        }

        [Theory(DisplayName = "MakeCompressedTag should reject an index outside 0..65535")]
        [InlineData(65536)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void MakeCompressedTag_rejects_out_of_range(int idx)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CompressionTagCodec.MakeCompressedTag(idx));
            CompressionTagCodec.MaxIndex.Should().Be(65535);
        }

        [Fact(DisplayName = "MakeCompressedTag should agree with the decoder's tag classification and index")]
        public void MakeCompressedTag_agrees_with_decoder()
        {
            const int idx = 1234;
            var header = BuildRawHeaderWithSenderTag(CompressionTagCodec.MakeCompressedTag(idx));

            var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(header));

            decoded.SenderKind.Should().Be(ArteryTagKind.Compressed);
            decoded.SenderCompressedIndex.Should().Be(idx);
        }

        [Fact(DisplayName = "TryBuildActorRefTag should build a COMPRESSED tag + version on a hit, and report false on a miss/empty")]
        public void TryBuildActorRefTag_hit_and_miss()
        {
            var table = Table(5, "/user/a", "/user/b");

            CompressionTagCodec.TryBuildActorRefTag(table, "/user/b", out var tag, out var version).Should().BeTrue();
            version.Should().Be((byte)5);
            (tag & ArteryEnvelopeHeader.CompressedIndexMask).Should().Be(1u);

            CompressionTagCodec.TryBuildActorRefTag(table, "/user/absent", out _, out _).Should().BeFalse();
            CompressionTagCodec.TryBuildActorRefTag(null, "/user/a", out _, out _).Should().BeFalse();
            CompressionTagCodec.TryBuildActorRefTag(CompressionTable<string>.Empty, "/user/a", out _, out _).Should().BeFalse();
            CompressionTagCodec.TryBuildActorRefTag(table, "", out _, out _).Should().BeFalse();
        }

        [Fact(DisplayName = "TryResolve should resolve a known index and report false for a null/empty/out-of-range table")]
        public void TryResolve_hit_and_miss()
        {
            var inverted = Table(2, "/user/a", "/user/b").Invert();

            CompressionTagCodec.TryResolve(inverted, 0, out var a).Should().BeTrue();
            a.Should().Be("/user/a");
            CompressionTagCodec.TryResolve(inverted, 1, out var b).Should().BeTrue();
            b.Should().Be("/user/b");

            CompressionTagCodec.TryResolve(inverted, 2, out _).Should().BeFalse("index 2 is out of range");
            CompressionTagCodec.TryResolve<string>(null, 0, out _).Should().BeFalse();
            CompressionTagCodec.TryResolve(DecompressionTable<string>.Empty, 0, out _).Should().BeFalse();
        }

        // ===================== helper =====================

        private static byte[] BuildRawHeaderWithSenderTag(uint senderTag)
        {
            var header = new byte[ArteryEnvelopeHeader.HeaderLength];
            header[ArteryEnvelopeHeader.VersionOffset] = ArteryEnvelopeHeader.CurrentVersion;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ArteryEnvelopeHeader.SenderTagOffset), senderTag);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ArteryEnvelopeHeader.PayloadOffsetFieldOffset), (uint)ArteryEnvelopeHeader.HeaderLength);
            return header;
        }
    }
}
