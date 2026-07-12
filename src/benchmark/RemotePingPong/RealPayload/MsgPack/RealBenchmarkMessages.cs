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
using MessagePack;

namespace RemotePingPong.RealPayload.MsgPack
{
    /// <summary>
    /// Realistic benchmark message: several primitives, a string, a nested complex type
    /// (<see cref="DeviceInfo"/>), and a collection (<see cref="ReadingBatch"/>). This is the
    /// MessagePack arm's wire type -- a plain attribute-based POCO (<c>[MessagePackObject]</c>/
    /// <c>[Key(n)]</c>), NOT the V2 arm's Akka.Serialization.V2 source-generated type. The
    /// logically-identical V2/Protobuf arm types are
    /// <see cref="RemotePingPong.RealPayload.V2.RealBenchmarkMessage"/> and
    /// <see cref="RemotePingPong.RealPayload.Protobuf.RealBenchmarkMessage"/> -- all three are built
    /// from one canonical <see cref="V2.RealBenchmarkMessage"/> instance by
    /// <see cref="RemotePingPong.RealPayload.RealPayloadFactory"/>, so all three arms always carry
    /// identical logical content.
    /// </summary>
    /// <remarks>
    /// Keys are 0-based and contiguous (0-6) so MessagePack's Standard resolver serializes this type
    /// as a compact array (one slot per key) rather than falling back to a map -- the array format is
    /// what "raw serializer speed" means for this arm (see
    /// <see cref="RemotePingPong.RealPayload.MsgPack.RealBenchmarkMsgPackSerializer"/>'s remarks).
    /// </remarks>
    [MessagePackObject]
    public sealed record RealBenchmarkMessage(
        [property: Key(0)] int SequenceNumber,
        [property: Key(1)] long TimestampTicks,
        [property: Key(2)] double Value,
        [property: Key(3)] bool Flag,
        [property: Key(4)] string CorrelationId,
        [property: Key(5)] DeviceInfo Device,
        [property: Key(6)] ReadingBatch Readings);

    /// <summary>
    /// Nested complex type carried by <see cref="RealBenchmarkMessage.Device"/>. Plain records get
    /// correct structural <c>Equals</c> for free since every member is a primitive/string, so -- unlike
    /// <see cref="ReadingBatch"/> -- no hand-written equality is needed here.
    /// </summary>
    [MessagePackObject]
    public sealed record DeviceInfo(
        [property: Key(0)] string DeviceId,
        [property: Key(1)] string FirmwareVersion,
        [property: Key(2)] int Region);

    /// <summary>
    /// One element of the <see cref="ReadingBatch"/> collection.
    /// </summary>
    [MessagePackObject]
    public sealed record Reading(
        [property: Key(0)] string SensorId,
        [property: Key(1)] double Value,
        [property: Key(2)] long TimestampTicks);

    /// <summary>
    /// The message's collection field: a batch of <see cref="Reading"/> values. MessagePack's Standard
    /// resolver serializes <see cref="IReadOnlyList{T}"/> members natively (no hand-written formatter
    /// needed -- unlike <see cref="RemotePingPong.RealPayload.V2.ReadingBatchFormatter"/>, which exists
    /// only because the Akka.Serialization.V2 generator has no native collection support). This wrapper
    /// class exists solely so the round-trip equality check in EnsureRealPayloadWiring works: a bare
    /// <c>List&lt;Reading&gt;</c>/<c>Reading[]</c> field on <see cref="RealBenchmarkMessage"/> would
    /// make the record's generated <c>Equals</c> compare that field by reference, not by content
    /// (mirrors why the V2 arm's <see cref="RemotePingPong.RealPayload.V2.ReadingBatch"/> is a
    /// hand-written <see cref="IEquatable{T}"/> class rather than a bare list too).
    /// </summary>
    [MessagePackObject]
    public sealed class ReadingBatch : IEquatable<ReadingBatch>
    {
        public ReadingBatch(IReadOnlyList<Reading> items)
        {
            Items = items;
        }

        [Key(0)]
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
}
