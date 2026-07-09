//-----------------------------------------------------------------------
// <copyright file="ArteryEncodeStageCompressionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Remote.Artery;
using Akka.Remote.Artery.Compression;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Verifies that <see cref="ArteryEncodeStage"/> SOURCES the OUTBOUND compression tables from its
    /// injected <see cref="IOutboundCompressionTables"/> and threads them into the codec (design.md
    /// "artery-ref-manifest-compression" Decision 2/4, Stage 2a item 4). With an installed table the
    /// stage emits COMPRESSED tags on the wire for hits and LITERAL for misses; with no source (or an
    /// Empty table -- the off/default path) the encoded frame is byte-identical to a no-compression
    /// build.
    /// </summary>
    public sealed class ArteryEncodeStageCompressionSpec : AkkaSpec
    {
        private const int FrameLengthFieldLength = 4;
        private const int HeaderLength = 32;
        private const long OriginUid = 0x1122_3344_5566_7788L;

        private const string RecipientPath = "akka://Sys@host:1/user/recipient";
        private const string UnknownSender = "akka://Sys@host:1/user/unknown-sender";

        private readonly ActorMaterializer _materializer;

        public ArteryEncodeStageCompressionSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = ActorMaterializer.Create(Sys);
        }

        protected override void AfterAll()
        {
            _materializer.Dispose();
            base.AfterAll();
        }

        private sealed class FixedOutboundTables : IOutboundCompressionTables
        {
            public FixedOutboundTables(CompressionTable<string> actorRefs, CompressionTable<string> manifests)
            {
                OutboundActorRefCompressionTable = actorRefs;
                OutboundManifestCompressionTable = manifests;
            }

            public CompressionTable<string> OutboundActorRefCompressionTable { get; }
            public CompressionTable<string> OutboundManifestCompressionTable { get; }
        }

        [Fact(DisplayName = "ArteryEncodeStage should emit a COMPRESSED recipient tag for a table hit and keep an unknown sender LITERAL")]
        public async Task Should_emit_compressed_for_hit_and_literal_for_miss()
        {
            var refs = new CompressionTable<string>(OriginUid, 5,
                new Dictionary<string, int> { [RecipientPath] = 0 });
            var source = new FixedOutboundTables(refs, CompressionTable<string>.Empty);

            var decoded = await EncodeAndDecode(new OutboundEnvelope("hello", UnknownSender, RecipientPath), source);

            // The recipient is in the table -> COMPRESSED index 0, and the header actor-ref version byte
            // is stamped from the table version.
            decoded.RecipientKind.Should().Be(ArteryTagKind.Compressed);
            decoded.RecipientCompressedIndex.Should().Be(0);
            decoded.Header.ActorRefTableVersion.Should().Be((byte)5);

            // The sender is NOT in the table -> falls back to LITERAL, still resolvable.
            decoded.SenderKind.Should().Be(ArteryTagKind.Literal);
            decoded.TryGetSenderPath(refs.Invert(), out var sender).Should().BeTrue();
            sender.Should().Be(UnknownSender);
        }

        [Fact(DisplayName = "ArteryEncodeStage with no compression source should be byte-identical to an Empty-table source (both LITERAL)")]
        public async Task Should_be_byte_identical_when_off()
        {
            var envelope = new OutboundEnvelope("hello", UnknownSender, RecipientPath);

            // No source at all (compression disabled / control stream) ...
            var noSource = await Encode(envelope, compression: null);
            // ... vs a source whose tables are Empty (compression enabled but nothing advertised yet).
            var emptySource = await Encode(envelope,
                new FixedOutboundTables(CompressionTable<string>.Empty, CompressionTable<string>.Empty));

            emptySource.Should().Equal(noSource, "the off/default path must be byte-identical to a no-compression encode");

            var decoded = ArteryEnvelopeCodec.Decode(EnvelopeBody(noSource));
            decoded.SenderKind.Should().Be(ArteryTagKind.Literal);
            decoded.RecipientKind.Should().Be(ArteryTagKind.Literal);
            decoded.Header.ActorRefTableVersion.Should().Be((byte)0);
            decoded.Header.ManifestTableVersion.Should().Be((byte)0);

            // Sanity: an installed table that HITS produces a DIFFERENT (shorter, compressed) frame,
            // proving the byte-identity above is a real property of the off path, not of the stage.
            var refs = new CompressionTable<string>(OriginUid, 1,
                new Dictionary<string, int> { [RecipientPath] = 0 });
            var hit = await Encode(envelope, new FixedOutboundTables(refs, CompressionTable<string>.Empty));
            hit.Should().NotEqual(noSource);
        }

        [Fact(DisplayName = "ArteryEncodeStage should NEVER compress a control message even when a hit table is installed")]
        public async Task Should_never_compress_control_messages()
        {
            // Heartbeat is an IArteryControlMessage -> IsControl == true. Even with a source installed,
            // the stage must skip compression for it (Pekko useOutboundCompression(!isArteryMessage)).
            var refs = new CompressionTable<string>(OriginUid, 5,
                new Dictionary<string, int> { [RecipientPath] = 0 });
            var source = new FixedOutboundTables(refs, CompressionTable<string>.Empty);

            var decoded = await EncodeAndDecode(new OutboundEnvelope(new ArteryHeartbeat(), null, RecipientPath), source);

            decoded.RecipientKind.Should().Be(ArteryTagKind.Literal);
            decoded.Header.ActorRefTableVersion.Should().Be((byte)0);
        }

        private async Task<ArteryEnvelopeDecoded> EncodeAndDecode(OutboundEnvelope envelope, IOutboundCompressionTables? compression)
        {
            var frame = await Encode(envelope, compression);
            return ArteryEnvelopeCodec.Decode(EnvelopeBody(frame));
        }

        private async Task<byte[]> Encode(OutboundEnvelope envelope, IOutboundCompressionTables? compression)
        {
            var stage = new ArteryEncodeStage(Sys.Serialization, OriginUid, pool: null, compression: compression);
            var result = await Source.Single<IOutboundEnvelope>(envelope)
                .Via(Flow.FromGraph(stage))
                .RunWith(Sink.First<ReadOnlySequence<byte>>(), _materializer);
            return result.ToArray();
        }

        private static ReadOnlySequence<byte> EnvelopeBody(byte[] frame) =>
            new(frame, FrameLengthFieldLength, frame.Length - FrameLengthFieldLength);
    }
}
