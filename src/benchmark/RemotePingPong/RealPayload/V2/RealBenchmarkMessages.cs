//-----------------------------------------------------------------------
// <copyright file="RealBenchmarkMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Serialization.V2;

namespace RemotePingPong.RealPayload.V2
{
    /// <summary>
    /// Protocol marker for the "real payload" (`--payload real`) benchmark message, MessagePack/V2 arm.
    /// Only used to satisfy <see cref="AkkaSerializerAttribute{TProtocol}"/>'s generic parameter and
    /// <see cref="RealBenchmarkSerializer.CreateRegistration"/>'s protocol-type registration -- the
    /// harness's actual HOCON wiring binds directly on <see cref="RealBenchmarkMessage"/> (see
    /// RemotePingPong.Program.RealPayloadSerializationConfig), so this interface has no runtime
    /// significance for the classic reflection-based serializer registration this benchmark uses.
    /// </summary>
    public interface IRealBenchmarkMessage
    {
    }

    /// <summary>
    /// Realistic benchmark message: several primitives, a string, a nested complex type
    /// (<see cref="DeviceInfo"/>), and a collection (<c>IReadOnlyList&lt;Reading&gt;</c>). This is the
    /// V2/MessagePack arm's wire type. The logically-identical Protobuf arm type is
    /// <see cref="RemotePingPong.RealPayload.Protobuf.RealBenchmarkMessage"/> -- both are built from
    /// one canonical instance of THIS record by <see cref="RemotePingPong.RealPayload.RealPayloadFactory"/>,
    /// so the two arms always carry identical logical content.
    /// </summary>
    /// <remarks>
    /// The <see cref="Readings"/> collection is a native generator field kind: the source generator
    /// writes it as MessagePack array framing (array header + one field-id map per <see cref="Reading"/>),
    /// with no hand-written formatter. A custom structural <see cref="Equals(RealBenchmarkMessage?)"/>
    /// sequence-compares <see cref="Readings"/> so the harness's canonical-message round-trip check
    /// (see EnsureRealPayloadWiring) compares by value rather than by list reference.
    /// </remarks>
    [AkkaSerializable(Manifest = RealBenchmarkMessage.ManifestName)]
    public sealed record RealBenchmarkMessage(
        [property: AkkaField(1)] int SequenceNumber,
        [property: AkkaField(2)] long TimestampTicks,
        [property: AkkaField(3)] double Value,
        [property: AkkaField(4)] bool Flag,
        [property: AkkaField(5)] string CorrelationId,
        [property: AkkaField(6)] DeviceInfo Device,
        [property: AkkaField(7)] IReadOnlyList<Reading> Readings) : IRealBenchmarkMessage
    {
        public const string ManifestName = "real-benchmark-v1";

        public bool Equals(RealBenchmarkMessage? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return SequenceNumber == other.SequenceNumber
                && TimestampTicks == other.TimestampTicks
                && Value.Equals(other.Value)
                && Flag == other.Flag
                && CorrelationId == other.CorrelationId
                && Device == other.Device
                && Readings.SequenceEqual(other.Readings);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(SequenceNumber);
            hash.Add(TimestampTicks);
            hash.Add(Value);
            hash.Add(Flag);
            hash.Add(CorrelationId);
            hash.Add(Device);
            foreach (var reading in Readings)
                hash.Add(reading);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Nested complex type carried by <see cref="RealBenchmarkMessage.Device"/>. A plain
    /// <c>[AkkaSerializable]</c> nested value written inline by the generator (no manifest of its own --
    /// mirrors <c>ShippingAddress</c>/<c>WarehouseInfo</c> in Akka.Serialization.V2.Tests).
    /// </summary>
    [AkkaSerializable]
    public sealed record DeviceInfo(
        [property: AkkaField(1)] string DeviceId,
        [property: AkkaField(2)] string FirmwareVersion,
        [property: AkkaField(3)] int Region);

    /// <summary>
    /// One element of the <see cref="RealBenchmarkMessage.Readings"/> collection. A plain
    /// <c>[AkkaSerializable]</c> nested value: the generator writes each element as a field-id map
    /// (sensor id, value, timestamp ticks) inside the collection's array framing.
    /// </summary>
    [AkkaSerializable]
    public sealed record Reading(
        [property: AkkaField(1)] string SensorId,
        [property: AkkaField(2)] double Value,
        [property: AkkaField(3)] long TimestampTicks);

    /// <summary>
    /// Source-generated V2 MessagePack serializer for <see cref="RealBenchmarkMessage"/> (the benchmark's
    /// real-payload, V2 arm). This is the hand-written "attributed half" of the partial class -- the
    /// Akka.Serialization.V2.Generators.AkkaSerializerGenerator analyzer (referenced by
    /// RemotePingPong.csproj the same way Akka.Remote.csproj references it for
    /// <c>ArteryControlMessageSerializer</c>) emits the other half
    /// (<c>RealBenchmarkSerializer.AkkaSerialization.g.cs</c>): the constructor, <c>Identifier</c>,
    /// <c>Manifest</c>/<c>Serialize</c>/<c>Deserialize</c>/<c>SizeHint</c> dispatch, and one
    /// Write/Read/SizeOf method per reachable message type -- including the native
    /// <c>IReadOnlyList&lt;Reading&gt;</c> collection field on <see cref="RealBenchmarkMessage"/>.
    /// </summary>
    /// <remarks>
    /// SerializerId 987001 is arbitrary but deliberately far outside both Akka's reserved internal
    /// range (0-40, see akka.conf) and the ids used elsewhere in this repo (e.g. Artery control's 23,
    /// the V2 test suite's ~120101-120202) to avoid any collision with a real Akka.NET serializer.
    /// </remarks>
    [AkkaSerializer<IRealBenchmarkMessage>(Name = "real-benchmark-v2", SerializerId = 987001)]
    internal sealed partial class RealBenchmarkSerializer : AkkaSerializer
    {
        /// <summary>
        /// Generated by <c>Akka.Serialization.V2.Generators.AkkaSerializerGenerator</c>. Not used by
        /// this benchmark's classic HOCON-based registration (see
        /// RemotePingPong.Program.RealPayloadSerializationConfig, which binds
        /// <see cref="RealBenchmarkMessage"/> directly by type name), but exercises the generator's
        /// programmatic-registration surface for good measure.
        /// </summary>
        public static partial SerializerRegistration CreateRegistration();
    }
}
