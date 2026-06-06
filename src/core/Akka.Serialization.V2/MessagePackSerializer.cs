//-----------------------------------------------------------------------
// <copyright file="MessagePackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using Akka.Actor;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// Base class for source-generated MessagePack serializers scoped to a protocol marker type.
/// </summary>
public abstract class MessagePackSerializer<TProtocol> : global::Akka.Serialization.SerializerV2
{
    protected MessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    protected global::Akka.Actor.IActorRef? ReadActorRef(ref MessagePackReader reader)
    {
        var path = reader.ReadString();
        return string.IsNullOrEmpty(path) ? ActorRefs.NoSender : system.Provider.ResolveActorRef(path);
    }

    protected static void WriteActorRef(ref MessagePackWriter writer, global::Akka.Actor.IActorRef? actorRef)
    {
        writer.Write(global::Akka.Serialization.Serialization.SerializedActorPath(actorRef));
    }

    protected static DateTime ReadDateTime(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTime array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var kind = (DateTimeKind)reader.ReadInt32();
        return new DateTime(ticks, kind);
    }

    protected static void WriteDateTime(ref MessagePackWriter writer, DateTime value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Kind);
    }

    protected static DateTimeOffset ReadDateTimeOffset(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTimeOffset array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt32();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    protected static void WriteDateTimeOffset(ref MessagePackWriter writer, DateTimeOffset value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Offset.TotalMinutes);
    }

    protected static decimal ReadDecimal(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 4)
            throw new MessagePackSerializationException($"Expected decimal array with 4 elements, got {arrayLength}.");

        var lo = reader.ReadInt32();
        var mid = reader.ReadInt32();
        var hi = reader.ReadInt32();
        var flags = reader.ReadInt32();
        return new decimal(new[] { lo, mid, hi, flags });
    }

    protected static void WriteDecimal(ref MessagePackWriter writer, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        writer.WriteArrayHeader(4);
        writer.Write(bits[0]);
        writer.Write(bits[1]);
        writer.Write(bits[2]);
        writer.Write(bits[3]);
    }

    protected static Guid ReadGuid(ref MessagePackReader reader)
    {
        var bytes = reader.ReadBytes();
        if (bytes == null || bytes.Value.Length != 16)
            throw new MessagePackSerializationException($"Expected 16 bytes for Guid, got {bytes?.Length ?? 0}.");

        Span<byte> span = stackalloc byte[16];
        bytes.Value.CopyTo(span);
        return new Guid(span);
    }

    protected static void WriteGuid(ref MessagePackWriter writer, Guid value)
    {
        writer.WriteBinHeader(16);
        value.TryWriteBytes(writer.GetSpan(16));
        writer.Advance(16);
    }
}
