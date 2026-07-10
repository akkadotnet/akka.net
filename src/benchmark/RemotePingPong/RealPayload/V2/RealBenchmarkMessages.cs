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
using Akka.Actor;
using Akka.Serialization.V2;
using MessagePack;

namespace RemotePingPong.RealPayload.V2
{
    /// <summary>
    /// Protocol marker for the "real payload" (`--payload real`) benchmark message, MessagePack/V2 arm.
    /// Only used to satisfy <see cref="MessagePackSerializer{TProtocol}"/>'s generic parameter and
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
    /// (<see cref="DeviceInfo"/>), and a collection (<see cref="ReadingBatch"/>). This is the V2/
    /// MessagePack arm's wire type. The logically-identical Protobuf arm type is
    /// <see cref="RemotePingPong.RealPayload.Protobuf.RealBenchmarkMessage"/> -- both are built from
    /// one canonical instance of THIS record by <see cref="RemotePingPong.RealPayload.RealPayloadFactory"/>,
    /// so the two arms always carry identical logical content.
    /// </summary>
    [AkkaSerializable(Manifest = RealBenchmarkMessage.ManifestName)]
    public sealed record RealBenchmarkMessage(
        [property: AkkaField(1)] int SequenceNumber,
        [property: AkkaField(2)] long TimestampTicks,
        [property: AkkaField(3)] double Value,
        [property: AkkaField(4)] bool Flag,
        [property: AkkaField(5)] string CorrelationId,
        [property: AkkaField(6)] DeviceInfo Device,
        [property: AkkaField(7)] ReadingBatch Readings) : IRealBenchmarkMessage
    {
        public const string ManifestName = "real-benchmark-v1";
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
    /// One element of the <see cref="ReadingBatch"/> collection. Deliberately NOT
    /// <c>[AkkaSerializable]</c>: it is only ever written/read by <see cref="ReadingBatchFormatter"/>
    /// (see the remarks on <see cref="ReadingBatch"/> for why).
    /// </summary>
    public sealed record Reading(string SensorId, double Value, long TimestampTicks);

    /// <summary>
    /// The message's collection field: a batch of <see cref="Reading"/> values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Akka.Serialization.V2 source generator (as of this benchmark) has no native field kind for
    /// a collection -- <c>List&lt;T&gt;</c>/array-of-non-byte fields hit AKKASG003 (unsupported field
    /// type), and the <see cref="AkkaSerializerFormatterAttribute"/> escape hatch explicitly rejects
    /// generic formatter targets (AKKASG011: "Formatter targets must be plain named types"), which
    /// rules out registering a formatter for <c>List&lt;Reading&gt;</c> directly.
    /// </para>
    /// <para>
    /// <see cref="ReadingBatch"/> is a small, NON-GENERIC wrapper class around the list precisely so it
    /// CAN be registered as a formatter target -- exactly the pattern <see cref="AddressFormatter"/>
    /// uses for <c>Akka.Actor.Address</c>. This is a deliberate, supported use of the documented
    /// escape hatch to give this benchmark a genuine "list of a nested type" field despite the
    /// generator's current lack of native collection support (see
    /// <see cref="ReadingBatchFormatter"/> for the hand-written wire format).
    /// </para>
    /// </remarks>
    public sealed class ReadingBatch : IEquatable<ReadingBatch>
    {
        public ReadingBatch(IReadOnlyList<Reading> items)
        {
            Items = items;
        }

        public IReadOnlyList<Reading> Items { get; }

        public bool Equals(ReadingBatch? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return Items.SequenceEqual(other.Items);
        }

        public override bool Equals(object? obj) => Equals(obj as ReadingBatch);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var reading in Items)
                hash.Add(reading);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Hand-written <see cref="IAkkaMessagePackFormatter{T}"/> for <see cref="ReadingBatch"/> -- writes
    /// an array of small maps (one per <see cref="Reading"/>: sensor id, value, timestamp ticks). This
    /// is the only hand-rolled piece of the V2 arm; every other field on
    /// <see cref="RealBenchmarkMessage"/>/<see cref="DeviceInfo"/> is generator-emitted.
    /// </summary>
    public sealed class ReadingBatchFormatter : IAkkaMessagePackFormatter<ReadingBatch>
    {
        public void Write(ref MessagePackWriter writer, ReadingBatch value)
        {
            writer.WriteArrayHeader(value.Items.Count);
            foreach (var reading in value.Items)
            {
                writer.WriteMapHeader(3);
                writer.Write(1);
                writer.Write(reading.SensorId);
                writer.Write(2);
                writer.Write(reading.Value);
                writer.Write(3);
                writer.Write(reading.TimestampTicks);
            }
        }

        public ReadingBatch Read(ref MessagePackReader reader)
        {
            var count = reader.ReadArrayHeader();
            var items = new List<Reading>(count);
            for (var i = 0; i < count; i++)
            {
                var fieldCount = reader.ReadMapHeader();
                string? sensorId = null;
                var value = 0.0;
                var timestampTicks = 0L;
                for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                {
                    var fieldId = reader.ReadInt32();
                    switch (fieldId)
                    {
                        case 1:
                            sensorId = reader.ReadString();
                            break;
                        case 2:
                            value = reader.ReadDouble();
                            break;
                        case 3:
                            timestampTicks = reader.ReadInt64();
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                items.Add(new Reading(sensorId ?? string.Empty, value, timestampTicks));
            }

            return new ReadingBatch(items);
        }

        public int SizeOf(ReadingBatch value)
        {
            var size = MessagePackSizes.SizeOfArrayHeader(value.Items.Count);
            foreach (var reading in value.Items)
            {
                size += MessagePackSizes.SizeOfMapHeader(3);
                size += MessagePackSizes.SizeOfInt32(1) + MessagePackSizes.SizeOfString(reading.SensorId);
                size += MessagePackSizes.SizeOfInt32(2) + MessagePackSizes.SizeOfDouble(reading.Value);
                size += MessagePackSizes.SizeOfInt32(3) + MessagePackSizes.SizeOfInt64(reading.TimestampTicks);
            }

            return size;
        }
    }

    /// <summary>
    /// Source-generated V2 MessagePack serializer for <see cref="RealBenchmarkMessage"/> (the benchmark's
    /// real-payload, V2 arm). This is the hand-written "attributed half" of the partial class -- the
    /// Akka.Serialization.V2.Generators.AkkaSerializerGenerator analyzer (referenced by
    /// RemotePingPong.csproj the same way Akka.Remote.csproj references it for
    /// <c>ArteryControlMessageSerializer</c>) emits the other half
    /// (<c>RealBenchmarkSerializer.AkkaSerialization.g.cs</c>): the constructor, <c>Identifier</c>,
    /// <c>Manifest</c>/<c>Serialize</c>/<c>Deserialize</c>/<c>SizeHint</c> dispatch, and one
    /// Write/Read/SizeOf method per reachable message type.
    /// </summary>
    /// <remarks>
    /// SerializerId 987001 is arbitrary but deliberately far outside both Akka's reserved internal
    /// range (0-40, see akka.conf) and the ids used elsewhere in this repo (e.g. Artery control's 23,
    /// the V2 test suite's ~120101-120202) to avoid any collision with a real Akka.NET serializer.
    /// </remarks>
    [AkkaSerializer(Name = "real-benchmark-v2", SerializerId = 987001)]
    [AkkaSerializerFormatter(typeof(ReadingBatch), typeof(ReadingBatchFormatter))]
    internal sealed partial class RealBenchmarkSerializer : MessagePackSerializer<IRealBenchmarkMessage>
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
