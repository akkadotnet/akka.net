//-----------------------------------------------------------------------
// <copyright file="RealPayloadFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace RemotePingPong.RealPayload
{
    /// <summary>
    /// Builds the ONE canonical "real payload" message content shared by both serializer arms
    /// (`--payload real --serializer v2|protobuf`). <see cref="CreateCanonical"/> is the single
    /// source of truth: it returns a <see cref="V2.RealBenchmarkMessage"/> record (the V2 arm's own
    /// wire type - no separate DTO needed since it is a plain, attribute-only POCO), and
    /// <see cref="ToProtobuf"/> derives the Protobuf arm's message from THAT instance field-for-field.
    /// This guarantees the two arms always carry identical logical content: same primitives, same
    /// string, same nested <see cref="V2.DeviceInfo"/>/<see cref="Protobuf.DeviceInfo"/> values, and
    /// the same list of <see cref="V2.Reading"/>/<see cref="Protobuf.Reading"/> entries.
    /// </summary>
    /// <remarks>
    /// Values are fixed literals (no randomness, no wall-clock reads) so every run - and every arm -
    /// serializes byte-for-byte the same canonical content, making repeated invocations and A/B
    /// comparisons reproducible.
    /// </remarks>
    internal static class RealPayloadFactory
    {
        /// <summary>
        /// Number of <see cref="V2.Reading"/> entries in the canonical message's collection field -
        /// a realistic small batch (not a toy single-element list, not an unrealistically huge one).
        /// </summary>
        public const int ReadingCount = 8;

        // Fixed instant (2026-01-01T00:00:00Z) - deterministic, no DateTime.UtcNow reads (see
        // no-wall-clock-assertions-in-tests: benchmarks reproducibility follows the same rule).
        private const long BaseTimestampTicks = 638385696000000000L;

        public static V2.RealBenchmarkMessage CreateCanonical()
        {
            var readings = new List<V2.Reading>(ReadingCount);
            for (var i = 0; i < ReadingCount; i++)
            {
                readings.Add(new V2.Reading(
                    SensorId: $"sensor-{i:D3}",
                    Value: 20.0 + i * 0.5,
                    TimestampTicks: BaseTimestampTicks + i * System.TimeSpan.TicksPerSecond));
            }

            return new V2.RealBenchmarkMessage(
                SequenceNumber: 42,
                TimestampTicks: BaseTimestampTicks,
                Value: 3.14159265358979,
                Flag: true,
                CorrelationId: "bench-correlation-0000000001",
                Device: new V2.DeviceInfo(
                    DeviceId: "device-042",
                    FirmwareVersion: "1.4.2-rc1",
                    Region: 7),
                Readings: new V2.ReadingBatch(readings));
        }

        public static Protobuf.RealBenchmarkMessage ToProtobuf(V2.RealBenchmarkMessage message)
        {
            var proto = new Protobuf.RealBenchmarkMessage
            {
                SequenceNumber = message.SequenceNumber,
                TimestampTicks = message.TimestampTicks,
                Value = message.Value,
                Flag = message.Flag,
                CorrelationId = message.CorrelationId,
                Device = new Protobuf.DeviceInfo
                {
                    DeviceId = message.Device.DeviceId,
                    FirmwareVersion = message.Device.FirmwareVersion,
                    Region = message.Device.Region
                }
            };

            proto.Readings.AddRange(message.Readings.Items.Select(reading => new Protobuf.Reading
            {
                SensorId = reading.SensorId,
                Value = reading.Value,
                TimestampTicks = reading.TimestampTicks
            }));

            return proto;
        }
    }
}
