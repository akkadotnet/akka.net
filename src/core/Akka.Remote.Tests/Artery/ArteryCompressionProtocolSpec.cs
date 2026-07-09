//-----------------------------------------------------------------------
// <copyright file="ArteryCompressionProtocolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Artery.Compression;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// MessagePack round-trip tests for the four compression table-advertisement control messages
    /// (design.md "artery-ref-manifest-compression", Stage 2a) through
    /// <see cref="ArteryControlMessageSerializer"/> -- exercising the two escape-hatch formatters the
    /// V2 generator has no native kind for: the <c>byte</c> table version
    /// (<see cref="CompressionTableVersionFormatter"/>) and the ordered string-list table
    /// (<see cref="CompressionAdvertisementTableFormatter"/>). Also pins the outbound-table build from
    /// the ordered advertisement list (<see cref="CompressionTable{T}.FromAdvertisement"/>).
    /// </summary>
    public sealed class ArteryCompressionProtocolSpec : IAsyncLifetime
    {
        private ActorSystem _system = null!;
        private ArteryControlMessageSerializer _serializer = null!;

        public ValueTask InitializeAsync()
        {
            _system = ActorSystem.Create("artery-compression-protocol-spec");
            _serializer = new ArteryControlMessageSerializer((ExtendedActorSystem)_system);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await _system.Terminate();

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip an ActorRefCompressionAdvertisement (byte version + ordered string table)")]
        public void Should_round_trip_ActorRefCompressionAdvertisement()
        {
            var from = new UniqueAddress(new Address("akka", "sys-b", "host-b", 2552), 123456789L);
            var table = new CompressionAdvertisementTable(new[]
            {
                "akka://sys-a@host-a:2551/user/one",
                "akka://sys-a@host-a:2551/user/two",
                "akka://sys-a@host-a:2551/user/three",
            });
            var adv = new ActorRefCompressionAdvertisement(from, 987654321L, 5, table);

            var round = RoundTrip(adv);

            round.Should().Be(adv);
            round.Table.Should().Equal(adv.Table);
            round.TableVersion.Should().Be((byte)5);
            round.OriginUid.Should().Be(987654321L);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip a ClassManifestCompressionAdvertisement")]
        public void Should_round_trip_ClassManifestCompressionAdvertisement()
        {
            var from = new UniqueAddress(new Address("akka", "sys-b", "host-b", 2552), -1L);
            var table = new CompressionAdvertisementTable(new[] { "My.Manifest.One, Asm", "My.Manifest.Two, Asm" });
            var adv = new ClassManifestCompressionAdvertisement(from, 42L, 127, table);

            RoundTrip(adv).Should().Be(adv);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip an empty-table advertisement")]
        public void Should_round_trip_empty_table_advertisement()
        {
            var from = new UniqueAddress(new Address("akka", "sys-b", "host-b", 2552), 7L);
            var adv = new ActorRefCompressionAdvertisement(from, 8L, 0, CompressionAdvertisementTable.Empty);

            var round = RoundTrip(adv);
            round.Should().Be(adv);
            round.Table.Should().BeEmpty();
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip an ActorRefCompressionAdvertisementAck")]
        public void Should_round_trip_ActorRefCompressionAdvertisementAck()
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 555L);
            var ack = new ActorRefCompressionAdvertisementAck(from, 5);

            RoundTrip(ack).Should().Be(ack);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip a ClassManifestCompressionAdvertisementAck")]
        public void Should_round_trip_ClassManifestCompressionAdvertisementAck()
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 555L);
            var ack = new ClassManifestCompressionAdvertisementAck(from, 127);

            RoundTrip(ack).Should().Be(ack);
        }

        [Fact(DisplayName = "The four advertisement/ack messages should have distinct manifests")]
        public void Advertisement_manifests_are_distinct()
        {
            var from = new UniqueAddress(new Address("akka", "s", "h", 1), 1L);
            var table = new CompressionAdvertisementTable(new[] { "x" });

            var manifests = new HashSet<string>
            {
                _serializer.Manifest(new ActorRefCompressionAdvertisement(from, 1L, 1, table)),
                _serializer.Manifest(new ActorRefCompressionAdvertisementAck(from, 1)),
                _serializer.Manifest(new ClassManifestCompressionAdvertisement(from, 1L, 1, table)),
                _serializer.Manifest(new ClassManifestCompressionAdvertisementAck(from, 1)),
            };

            manifests.Should().HaveCount(4, "each advertisement/ack message needs its own manifest for dispatch");
        }

        [Fact(DisplayName = "CompressionTable.FromAdvertisement should map the ordered list so position == index")]
        public void FromAdvertisement_maps_position_to_index()
        {
            var ordered = new[] { "first", "second", "third" };
            var table = CompressionTable<string>.FromAdvertisement(originUid: 99L, version: 3, ordered);

            table.OriginUid.Should().Be(99L);
            table.Version.Should().Be((byte)3);
            table.Compress("first").Should().Be(0);
            table.Compress("second").Should().Be(1);
            table.Compress("third").Should().Be(2);
            table.Compress("missing").Should().Be(CompressionTable<string>.NotCompressedId);

            // The inverse resolves indices back to their values (the receiver/decode direction).
            var inverted = table.Invert();
            inverted.Get(0).Should().Be("first");
            inverted.Get(2).Should().Be("third");
        }

        private TMessage RoundTrip<TMessage>(TMessage message)
            where TMessage : IArteryControlMessage
        {
            var bytes = _serializer.ToBinary(message);
            var manifest = _serializer.Manifest(message);
            return (TMessage)_serializer.FromBinary(bytes, manifest);
        }
    }
}
