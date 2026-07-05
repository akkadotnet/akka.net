//-----------------------------------------------------------------------
// <copyright file="ArteryControlMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Round-trip tests for <see cref="ArteryControlMessageSerializer"/>, the hand-rolled V2
    /// MessagePack serializer for <see cref="HandshakeReq"/> / <see cref="HandshakeRsp"/> — see
    /// the task report for why this is hand-rolled rather than source-generated (the generator
    /// requires every nested field type to carry <c>[AkkaSerializable]</c>, which
    /// <see cref="Address"/>, a core <c>Akka.Actor</c> type, does not and should not for this
    /// change).
    ///
    /// <para>
    /// This serializer is NOT registered in <c>Remote.conf</c> by this change (that file is owned
    /// by a parallel task). The HOCON fragment needed to wire it up is reproduced in the task
    /// report; here it is registered ad hoc, per-test, via <see cref="SerializationSetup"/>.
    /// </para>
    /// </summary>
    public sealed class ArteryControlMessageSerializerSpec : IAsyncLifetime
    {
        private ActorSystem _system = null!;
        private ArteryControlMessageSerializer _serializer = null!;

        public ValueTask InitializeAsync()
        {
            _system = ActorSystem.Create("artery-control-message-serializer-spec");
            _serializer = new ArteryControlMessageSerializer((ExtendedActorSystem)_system);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await _system.Terminate();

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip HandshakeReq")]
        public void Should_round_trip_HandshakeReq()
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 123456789L);
            var to = new Address("akka", "sys-b", "host-b", 2552);
            var req = new HandshakeReq(from, to);

            RoundTrip(req).Should().Be(req);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip HandshakeRsp")]
        public void Should_round_trip_HandshakeRsp()
        {
            var from = new UniqueAddress(new Address("akka", "sys-b", "host-b", 2552), -987654321L);
            var rsp = new HandshakeRsp(from);

            RoundTrip(rsp).Should().Be(rsp);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip an Address with no host/port (e.g. a not-yet-bound local address)")]
        public void Should_round_trip_hostless_address()
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", null, null), 7L);
            var to = new Address("akka", "sys-b", null, null);
            var req = new HandshakeReq(from, to);

            RoundTrip(req).Should().Be(req);
        }

        [Theory(DisplayName = "ArteryControlMessageSerializer should round-trip negative, zero, and boundary uid values")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        public void Should_round_trip_extreme_uid_values(long uid)
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), uid);
            var rsp = new HandshakeRsp(from);

            RoundTrip(rsp).Should().Be(rsp);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip ArteryHeartbeat (task group 6, task 6.4)")]
        public void Should_round_trip_ArteryHeartbeat()
        {
            RoundTrip(new ArteryHeartbeat()).Should().Be(new ArteryHeartbeat());
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip ArteryHeartbeatRsp (task group 6, task 6.4)")]
        public void Should_round_trip_ArteryHeartbeatRsp()
        {
            RoundTrip(new ArteryHeartbeatRsp()).Should().Be(new ArteryHeartbeatRsp());
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip ArteryQuarantined (task group 6, task 6.5)")]
        public void Should_round_trip_ArteryQuarantined()
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 123456789L);
            var quarantined = new ArteryQuarantined(from, QuarantinedUid: 987654321L);

            RoundTrip(quarantined).Should().Be(quarantined);
        }

        [Theory(DisplayName = "ArteryControlMessageSerializer should round-trip ArteryQuarantined across negative, zero, and boundary uid values")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        public void Should_round_trip_ArteryQuarantined_extreme_uid_values(long quarantinedUid)
        {
            var from = new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 42L);
            var quarantined = new ArteryQuarantined(from, quarantinedUid);

            RoundTrip(quarantined).Should().Be(quarantined);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should report bytes-written matching the buffer")]
        public void Should_report_bytes_written()
        {
            var req = new HandshakeReq(
                new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 42L),
                new Address("akka", "sys-b", "host-b", 2552));

            var buffer = new ArrayBufferWriter<byte>();
            var written = _serializer.Serialize(req, buffer);

            written.Should().Be(buffer.WrittenCount);
            written.Should().BeGreaterThan(0);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should report exact size hints for all message types")]
        public void Should_report_exact_size_hints()
        {
            var req = new HandshakeReq(
                new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 42L),
                new Address("akka", "sys-b", "host-b", 2552));
            var rsp = new HandshakeRsp(new UniqueAddress(new Address("akka", "sys-b", "host-b", 2552), -5L));
            var heartbeat = new ArteryHeartbeat();
            var heartbeatRsp = new ArteryHeartbeatRsp();
            var quarantined = new ArteryQuarantined(new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 42L), 99L);

            _serializer.SizeHint(req).Should().Be(_serializer.ToBinary(req).Length);
            _serializer.SizeHint(rsp).Should().Be(_serializer.ToBinary(rsp).Length);
            _serializer.SizeHint(heartbeat).Should().Be(_serializer.ToBinary(heartbeat).Length);
            _serializer.SizeHint(heartbeatRsp).Should().Be(_serializer.ToBinary(heartbeatRsp).Length);
            _serializer.SizeHint(quarantined).Should().Be(_serializer.ToBinary(quarantined).Length);
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should dispatch by manifest and reject an unknown manifest")]
        public void Should_use_manifest_dispatch()
        {
            var req = new HandshakeReq(
                new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 1L),
                new Address("akka", "sys-b", "host-b", 2552));

            _serializer.Manifest(req).Should().Be(ArteryControlMessageSerializer.HandshakeReqManifest);
            _serializer.Manifest(new ArteryHeartbeat()).Should().Be(ArteryControlMessageSerializer.HeartbeatManifest);
            _serializer.Manifest(new ArteryHeartbeatRsp()).Should().Be(ArteryControlMessageSerializer.HeartbeatRspManifest);
            _serializer.Manifest(new ArteryQuarantined(new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 1L), 2L))
                .Should().Be(ArteryControlMessageSerializer.QuarantinedManifest);

            var bytes = _serializer.ToBinary(req);
            Action deserializeUnknown = () => _serializer.FromBinary(bytes, "unknown-manifest");
            deserializeUnknown.Should().Throw<SerializationException>();
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip through the Serialization extension when registered via config")]
        public async Task Should_round_trip_through_Serialization_extension()
        {
            var setup = ActorSystemSetup.Create(SerializationSetup.Create(extendedSystem =>
                ImmutableHashSet.Create(SerializerDetails.Create(
                    "artery-control",
                    new ArteryControlMessageSerializer(extendedSystem),
                    ImmutableHashSet.Create<Type>(typeof(HandshakeReq), typeof(HandshakeRsp))))));

            var system = ActorSystem.Create("artery-control-message-serializer-registration-spec", setup);
            try
            {
                var req = new HandshakeReq(
                    new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 555L),
                    new Address("akka", "sys-b", "host-b", 2552));

                var serializer = system.Serialization.FindSerializerFor(req);
                serializer.Should().BeOfType<ArteryControlMessageSerializer>();

                var bytes = system.Serialization.Serialize(req);
                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, req);
                var deserialized = system.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

                deserialized.Should().Be(req);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "ArteryControlMessageSerializer should round-trip through the Serialization extension when registered via HOCON (the fragment this change is NOT wiring into Remote.conf)")]
        public async Task Should_round_trip_through_Serialization_extension_via_HOCON_config()
        {
            // Same fragment reproduced in the task report - Remote.conf itself is owned by a
            // parallel task, so it is not edited here; this proves the fragment is correct.
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                  serializers {
                    artery-control = ""Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote""
                  }
                  serialization-bindings {
                    ""Akka.Remote.Artery.HandshakeReq, Akka.Remote"" = artery-control
                    ""Akka.Remote.Artery.HandshakeRsp, Akka.Remote"" = artery-control
                  }
                  serialization-identifiers {
                    ""Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote"" = 23
                  }
                }
            ").WithFallback(ConfigurationFactory.Default());

            var system = ActorSystem.Create("artery-control-message-serializer-hocon-spec", config);
            try
            {
                var req = new HandshakeReq(
                    new UniqueAddress(new Address("akka", "sys-a", "host-a", 2551), 555L),
                    new Address("akka", "sys-b", "host-b", 2552));

                var serializer = system.Serialization.FindSerializerFor(req);
                serializer.Should().BeOfType<ArteryControlMessageSerializer>();
                serializer.Identifier.Should().Be(23);

                var bytes = system.Serialization.Serialize(req);
                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, req);
                var deserialized = system.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

                deserialized.Should().Be(req);
            }
            finally
            {
                await system.Terminate();
            }
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
