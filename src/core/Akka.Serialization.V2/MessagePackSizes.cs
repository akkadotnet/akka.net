//-----------------------------------------------------------------------
// <copyright file="MessagePackSizes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using Akka.Actor;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// Exact-size MessagePack encoding math shared by <see cref="MessagePackSerializer{TProtocol}"/>,
/// the built-in foreign-type formatters (<see cref="AddressFormatter"/>, <see cref="ActorPathFormatter"/>),
/// and external hand-written <see cref="IAkkaMessagePackFormatter{T}"/> implementations.
/// </summary>
/// <remarks>
/// Every method returns the exact number of bytes the corresponding
/// <see cref="MessagePackWriter"/> operation produces, using the same encoding choices as the
/// generated serializers. Formatters should compose these helpers when implementing
/// <see cref="IAkkaMessagePackFormatter{T}.SizeOf"/> so their results honor the
/// exact-or-<see cref="Akka.Serialization.SerializerV2.UnknownSize"/> contract.
/// </remarks>
public static class MessagePackSizes
{
    /// <summary>
    /// Returns the encoded size of a MessagePack nil value.
    /// </summary>
    public static int SizeOfNil() => 1;

    /// <summary>
    /// Returns the encoded size of a boolean value.
    /// </summary>
    /// <param name="_">The value; boolean encoding is always one byte.</param>
    public static int SizeOfBoolean(bool _) => 1;

    /// <summary>
    /// Returns the encoded size of a double value.
    /// </summary>
    /// <param name="_">The value; double encoding is always nine bytes.</param>
    public static int SizeOfDouble(double _) => 9;

    /// <summary>
    /// Returns the encoded size of a 32-bit integer value.
    /// </summary>
    /// <param name="value">The value whose variable-length encoded size is being computed.</param>
    public static int SizeOfInt32(int value) => MessagePackWriter.GetEncodedLength((long)value);

    /// <summary>
    /// Returns the encoded size of a 64-bit integer value.
    /// </summary>
    /// <param name="value">The value whose variable-length encoded size is being computed.</param>
    public static int SizeOfInt64(long value) => MessagePackWriter.GetEncodedLength(value);

    /// <summary>
    /// Returns the encoded size of an enum value written using the generated int32 convention.
    /// </summary>
    /// <param name="value">The enum value converted to its underlying int.</param>
    public static int SizeOfEnum(int value) => SizeOfInt32(value);

    /// <summary>
    /// Returns the encoded size of a map header with <paramref name="count"/> entries.
    /// </summary>
    /// <param name="count">The number of map entries.</param>
    public static int SizeOfMapHeader(int count)
    {
        if (count <= 15)
            return 1;
        if (count <= ushort.MaxValue)
            return 3;
        return 5;
    }

    /// <summary>
    /// Returns the encoded size of an array header with <paramref name="count"/> elements.
    /// </summary>
    /// <param name="count">The number of array elements.</param>
    public static int SizeOfArrayHeader(int count)
    {
        if (count <= 15)
            return 1;
        if (count <= ushort.MaxValue)
            return 3;
        return 5;
    }

    /// <summary>
    /// Returns the encoded size of a string value, or the nil size when <paramref name="value"/> is null.
    /// </summary>
    /// <param name="value">The string whose UTF-8 encoded size is being computed.</param>
    public static int SizeOfString(string? value)
    {
        if (value is null)
            return SizeOfNil();

        var byteCount = Encoding.UTF8.GetByteCount(value);
        return checked(SizeOfStringHeader(byteCount) + byteCount);
    }

    /// <summary>
    /// Returns the encoded size of a binary payload, or the nil size when <paramref name="value"/> is null.
    /// </summary>
    /// <param name="value">The byte array whose bin-encoded size is being computed.</param>
    public static int SizeOfBytes(byte[]? value)
    {
        if (value is null)
            return SizeOfNil();

        return checked(SizeOfBinHeader(value.Length) + value.Length);
    }

    /// <summary>
    /// Returns the encoded size of a <see cref="Guid"/> written using the generated 16-byte bin convention.
    /// </summary>
    /// <param name="_">The value; Guid encoding is always a bin header plus sixteen bytes.</param>
    public static int SizeOfGuid(Guid _) => SizeOfBinHeader(16) + 16;

    /// <summary>
    /// Returns the encoded size of a <see cref="DateTime"/> written using the generated
    /// two-element array convention of ticks plus kind.
    /// </summary>
    /// <param name="value">The value whose encoded size is being computed.</param>
    public static int SizeOfDateTime(DateTime value)
    {
        return checked(SizeOfArrayHeader(2) + SizeOfInt64(value.Ticks) + SizeOfInt32((int)value.Kind));
    }

    /// <summary>
    /// Returns the encoded size of a <see cref="DateTimeOffset"/> written using the generated
    /// two-element array convention of ticks plus offset minutes.
    /// </summary>
    /// <param name="value">The value whose encoded size is being computed.</param>
    public static int SizeOfDateTimeOffset(DateTimeOffset value)
    {
        return checked(SizeOfArrayHeader(2) + SizeOfInt64(value.Ticks) + SizeOfInt32((int)value.Offset.TotalMinutes));
    }

    /// <summary>
    /// Returns the encoded size of a <see cref="decimal"/> written using the generated
    /// four-element array-of-int32-bits convention.
    /// </summary>
    /// <param name="value">The value whose encoded size is being computed.</param>
    public static int SizeOfDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        return checked(
            SizeOfArrayHeader(4) +
            SizeOfInt32(bits[0]) +
            SizeOfInt32(bits[1]) +
            SizeOfInt32(bits[2]) +
            SizeOfInt32(bits[3]));
    }

    /// <summary>
    /// Returns the encoded size of an <see cref="IActorRef"/> written using the generated
    /// transport-aware serialized-path string convention.
    /// </summary>
    /// <param name="actorRef">The actor reference; <see cref="ActorRefs.NoSender"/> and null encode as an empty string.</param>
    public static int SizeOfActorRef(IActorRef? actorRef)
    {
        return SizeOfString(global::Akka.Serialization.Serialization.SerializedActorPath(actorRef));
    }

    /// <summary>
    /// Returns the encoded size of a bin header for a payload of <paramref name="byteCount"/> bytes.
    /// </summary>
    /// <param name="byteCount">The binary payload length.</param>
    public static int SizeOfBinHeader(int byteCount)
    {
        if (byteCount <= byte.MaxValue)
            return 2;
        if (byteCount <= ushort.MaxValue)
            return 3;
        return 5;
    }

    private static int SizeOfStringHeader(int byteCount)
    {
        if (byteCount <= 31)
            return 1;
        if (byteCount <= byte.MaxValue)
            return 2;
        if (byteCount <= ushort.MaxValue)
            return 3;
        return 5;
    }
}
