//-----------------------------------------------------------------------
// <copyright file="AddressFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Runtime.Serialization;
using Akka.Actor;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// Built-in <see cref="IAkkaMessagePackFormatter{T}"/> for <see cref="Address"/>.
/// </summary>
/// <remarks>
/// The wire format is byte-identical to Akka.Remote's hand-rolled Artery control-message
/// serializer (<c>ArteryControlMessageSerializer.WriteAddress</c> / <c>ReadAddress</c> /
/// <c>SizeOfAddress</c>): a 4-element array of <c>[Protocol, System, Host-or-nil, Port-or-nil]</c>.
/// Registering this formatter via <see cref="AkkaSerializerFormatterAttribute"/> lets
/// <see cref="Address"/> fields be used on any generated serializer, including ones that must
/// interoperate with that hand-rolled format.
/// </remarks>
public sealed class AddressFormatter : IAkkaMessagePackFormatter<Address>
{
    /// <inheritdoc />
    public void Write(ref MessagePackWriter writer, Address value)
    {
        writer.WriteArrayHeader(4);
        writer.Write(value.Protocol);
        writer.Write(value.System);

        if (value.Host is { } host)
            writer.Write(host);
        else
            writer.WriteNil();

        if (value.Port is { } port)
            writer.Write(port);
        else
            writer.WriteNil();
    }

    /// <inheritdoc />
    public Address Read(ref MessagePackReader reader)
    {
        var length = reader.ReadArrayHeader();
        if (length != 4)
            throw new SerializationException($"Expected a 4-element address array, got {length}.");

        var protocol = reader.ReadString() ?? throw new SerializationException("Missing address protocol.");
        var system = reader.ReadString() ?? throw new SerializationException("Missing address system name.");
        var host = reader.TryReadNil() ? null : reader.ReadString();
        var port = reader.TryReadNil() ? (int?)null : reader.ReadInt32();

        return new Address(protocol, system, host, port);
    }

    /// <inheritdoc />
    public int SizeOf(Address value)
    {
        return MessagePackSizes.SizeOfArrayHeader(4) +
            MessagePackSizes.SizeOfString(value.Protocol) +
            MessagePackSizes.SizeOfString(value.System) +
            (value.Host is { } host ? MessagePackSizes.SizeOfString(host) : MessagePackSizes.SizeOfNil()) +
            (value.Port is { } port ? MessagePackSizes.SizeOfInt32(port) : MessagePackSizes.SizeOfNil());
    }
}
