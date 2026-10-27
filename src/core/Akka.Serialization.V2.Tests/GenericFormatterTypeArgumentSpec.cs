//-----------------------------------------------------------------------
// <copyright file="GenericFormatterTypeArgumentSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GenericFormatterTypeArgumentSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("generic-formatter-type-arguments");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "Generated serializer should retain closed formatter and containing-type arguments")]
    public void Generated_serializer_should_retain_closed_formatter_and_containing_type_arguments()
    {
        var serializer = new GenericFormatterSerializer((ExtendedActorSystem)_system);
        var message = new GenericFormatterMessage(new Sku("SKU-123"), new ProductCode("P-456"));

        var bytes = serializer.ToBinary(message);
        var result = serializer.FromBinary(bytes, GenericFormatterMessage.ManifestName)
            .Should().BeOfType<GenericFormatterMessage>().Subject;

        result.Should().Be(message);
    }
}

public interface IStringValue<TSelf> where TSelf : IStringValue<TSelf>
{
    string Value { get; }

    static abstract TSelf From(string value);
}

public readonly record struct Sku(string Value) : IStringValue<Sku>
{
    public static Sku From(string value) => new(value);
}

public readonly record struct ProductCode(string Value) : IStringValue<ProductCode>
{
    public static ProductCode From(string value) => new(value);
}

public sealed class StringValueFormatter<T> : IAkkaMessagePackFormatter<T>
    where T : IStringValue<T>
{
    public void Write(ref MessagePackWriter writer, T value) => writer.Write(value.Value);

    public T Read(ref MessagePackReader reader) => T.From(reader.ReadString() ?? throw new SerializationException("Missing string value."));

    public int SizeOf(T value) => Akka.Serialization.SerializerV2.UnknownSize;
}

public static class StringValueFormatterContainer<T>
    where T : IStringValue<T>
{
    public sealed class Formatter : IAkkaMessagePackFormatter<T>
    {
        public void Write(ref MessagePackWriter writer, T value) => writer.Write(value.Value);

        public T Read(ref MessagePackReader reader) => T.From(reader.ReadString() ?? throw new SerializationException("Missing string value."));

        public int SizeOf(T value) => Akka.Serialization.SerializerV2.UnknownSize;
    }
}

public interface IGenericFormatterProtocol
{
}

[AkkaSerializable(Manifest = GenericFormatterMessage.ManifestName)]
public sealed record GenericFormatterMessage(
    [property: AkkaField(0)] Sku Sku,
    [property: AkkaField(1)] ProductCode ProductCode) : IGenericFormatterProtocol
{
    public const string ManifestName = "generic-formatter-v1";
}

[AkkaSerializer<IGenericFormatterProtocol>("generic-formatters", 120107)]
[AkkaSerializerFormatter<Sku, StringValueFormatter<Sku>>]
[AkkaSerializerFormatter<ProductCode, StringValueFormatterContainer<ProductCode>.Formatter>]
public sealed partial class GenericFormatterSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
