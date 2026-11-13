//-----------------------------------------------------------------------
// <copyright file="MessageFormatterDefaultsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using MessagePack;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class MessageFormatterDefaultsSpec
{
    [Theory(DisplayName = "Should_WriteExactFormatterBytes_When_SerializerOrDeclaringMessageSelectsTheFormatter")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_WriteExactFormatterBytes_When_SerializerOrDeclaringMessageSelectsTheFormatter(bool useOverride)
    {
        var system = (ExtendedActorSystem)ActorSystem.Create("message-formatter-defaults");
        try
        {
            var message = new MessageFormatterScope(
                new FormatterValue<int>(1),
                new Dictionary<FormatterValue<int>, List<FormatterValue<int>>> { [new(2)] = [new(3)] },
                new MessageFormatterNested(new(4)),
                new FormatterValue<string>(5));

            SerializerV2 serializer = useOverride
                ? new MessageFormatterOverrideSerializer(system)
                : new MessageFormatterDefaultSerializer(system);
            object envelope = useOverride
                ? new MessageFormatterOverrideEnvelope(message)
                : new MessageFormatterDefaultEnvelope(message);

            var bytes = serializer.ToBinary(envelope);
            AssertWire(bytes, useOverride);
            serializer.SizeHint(envelope).Should().Be(bytes.Length);

            var decoded = serializer.FromBinary(bytes, serializer.Manifest(envelope));
            var decodedMessage = useOverride
                ? ((MessageFormatterOverrideEnvelope)decoded).Message
                : ((MessageFormatterDefaultEnvelope)decoded).Message;
            decodedMessage.Value.Should().Be(message.Value);
            decodedMessage.Values.Should().BeEquivalentTo(message.Values);
            decodedMessage.Nested.Should().Be(message.Nested);
            decodedMessage.Other.Should().Be(message.Other);

            if (!useOverride)
            {
                var closed = new MessageFormatterClosed<string>(new(6));
                var closedBytes = serializer.ToBinary(closed);
                closedBytes.Should().Equal(0x81, 0x01, 0x10);
                serializer.FromBinary(closedBytes, serializer.Manifest(closed)).Should().Be(closed);
            }
        }
        finally
        {
            await system.Terminate();
        }
    }

    private static void AssertWire(byte[] bytes, bool useOverride)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        reader.ReadMapHeader().Should().Be(4);
        reader.ReadInt32().Should().Be(1);
        reader.ReadInt32().Should().Be(useOverride ? 101 : 11);
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(useOverride ? 102 : 12);
        reader.ReadArrayHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(useOverride ? 103 : 13);
        reader.ReadInt32().Should().Be(3);
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        if (useOverride)
            reader.ReadInt32().Should().Be(104);
        else
            reader.ReadString().Should().Be("4");
        reader.ReadInt32().Should().Be(4);
        reader.ReadInt32().Should().Be(15);
        reader.End.Should().BeTrue();
    }

    [Theory(DisplayName = "Should_ResolveImportedMessageFormatter_When_ConsumerChoosesFallbackOrOverride")]
    [InlineData("public", false, null)]
    [InlineData("internal", false, "AKKASG054")]
    [InlineData("internal", true, null)]
    [InlineData(null, false, "AKKASG039")]
    [InlineData(null, true, null)]
    public void Should_ResolveImportedMessageFormatter_When_ConsumerChoosesFallbackOrOverride(
        string? formatterAccessibility, bool useOverride, string? expectedDiagnostic)
    {
        var declaration = formatterAccessibility == null
            ? string.Empty
            : "[AkkaMessageFormatter<Foreign, ImportedFormatter>]";
        var source = $$"""
            using Akka.Serialization.V2;
            using MessagePack;

            namespace ImportedFormatters;

            [AkkaSerializable]
            public sealed record Foreign([property: AkkaField(1)] int Value)
            {
                [AkkaField(2)] public string Secret { internal get; init; } = "";
            }

            {{formatterAccessibility ?? "public"}} sealed class ImportedFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new(reader.ReadInt32());
                public int SizeOf(Foreign value) => MessagePackSizes.SizeOfInt32(value.Value);
            }

            [AkkaSerializable]
            {{declaration}}
            public sealed record Imported([property: AkkaField(1)] Foreign Value);
            """;

        var registration = useOverride ? "[AkkaSerializerFormatter<Foreign, ConsumerFormatter>]" : string.Empty;
        var consumer = $$"""
            using Akka.Actor;
            using Akka.Serialization.V2;
            using ImportedFormatters;
            using MessagePack;

            namespace ConsumerFormatters;

            public sealed class ConsumerFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new(reader.ReadInt32());
                public int SizeOf(Foreign value) => MessagePackSizes.SizeOfInt32(value.Value);
            }

            public interface IProtocol;

            [AkkaSerializable(Manifest = "imported")]
            public sealed record Envelope([property: AkkaField(1)] Imported Message) : IProtocol;

            [AkkaSerializer<IProtocol>("consumer", 120608)]
            {{registration}}
            public sealed partial class ConsumerSerializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
            }
            """;

        var reference = GeneratorTestHarness.CompileToReference(source, "ImportedFormatters");
        var result = GeneratorTestHarness.Run(consumer, reference);
        if (expectedDiagnostic != null)
        {
            var diagnostic = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Severity == DiagnosticSeverity.Error).Subject;
            diagnostic.Id.Should().Be(expectedDiagnostic);
            diagnostic.GetMessage().Should().Contain(formatterAccessibility == null ? "Secret" : "ImportedFormatter")
                .And.Contain("ConsumerSerializer");
            diagnostic.Location.Should().NotBe(Location.None);
            result.GeneratedSources.Should().BeEmpty();
            return;
        }

        result.AllDiagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var generated = result.GeneratedSources.Values.Should().ContainSingle().Subject;
        generated.Should().Contain(useOverride ? "new global::ConsumerFormatters.ConsumerFormatter()" : "new global::ImportedFormatters.ImportedFormatter()");
        generated.Should().NotContain("WriteForeign");
    }
}

internal readonly record struct FormatterValue<T>(int Value);

internal sealed class MessageOffsetFormatter<T> : IAkkaMessagePackFormatter<FormatterValue<T>>
{
    public void Write(ref MessagePackWriter writer, FormatterValue<T> value) => writer.Write(value.Value + 10);
    public FormatterValue<T> Read(ref MessagePackReader reader) => new(reader.ReadInt32() - 10);
    public int SizeOf(FormatterValue<T> value) => MessagePackSizes.SizeOfInt32(value.Value + 10);
}

internal sealed class MessageTextFormatter<T> : IAkkaMessagePackFormatter<FormatterValue<T>>
{
    public void Write(ref MessagePackWriter writer, FormatterValue<T> value) => writer.Write(value.Value.ToString(CultureInfo.InvariantCulture));
    public FormatterValue<T> Read(ref MessagePackReader reader) => new(int.Parse(reader.ReadString()!, CultureInfo.InvariantCulture));
    public int SizeOf(FormatterValue<T> value) => MessagePackSizes.SizeOfString(value.Value.ToString(CultureInfo.InvariantCulture));
}

internal sealed class MessageOverrideFormatter : IAkkaMessagePackFormatter<FormatterValue<int>>
{
    public void Write(ref MessagePackWriter writer, FormatterValue<int> value) => writer.Write(value.Value + 100);
    public FormatterValue<int> Read(ref MessagePackReader reader) => new(reader.ReadInt32() - 100);
    public int SizeOf(FormatterValue<int> value) => MessagePackSizes.SizeOfInt32(value.Value + 100);
}

[AkkaSerializable]
[AkkaMessageFormatter<FormatterValue<int>, MessageOffsetFormatter<int>>]
[AkkaMessageFormatter<FormatterValue<string>, MessageOffsetFormatter<string>>]
internal sealed record MessageFormatterScope(
    [property: AkkaField(1)] FormatterValue<int> Value,
    [property: AkkaField(2)] Dictionary<FormatterValue<int>, List<FormatterValue<int>>> Values,
    [property: AkkaField(3)] MessageFormatterNested Nested,
    [property: AkkaField(4)] FormatterValue<string> Other);

[AkkaSerializable]
[AkkaMessageFormatter<FormatterValue<int>, MessageTextFormatter<int>>]
internal readonly record struct MessageFormatterNested([property: AkkaField(1)] FormatterValue<int> Value);

[AkkaSerializable]
[AkkaMessageFormatter<FormatterValue<string>, MessageOffsetFormatter<string>>]
internal sealed record MessageFormatterClosed<T>([property: AkkaField(1)] FormatterValue<T> Value);

internal interface IMessageFormatterDefault;

[AkkaSerializable(Manifest = "defaults")]
internal sealed record MessageFormatterDefaultEnvelope([property: AkkaField(1)] MessageFormatterScope Message) : IMessageFormatterDefault;

[AkkaSerializer<IMessageFormatterDefault>("message-defaults", 120609)]
[AkkaSerializerIncludes<MessageFormatterClosed<string>>(Manifest = "closed")]
internal sealed partial class MessageFormatterDefaultSerializer : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

internal interface IMessageFormatterOverride;

[AkkaSerializable(Manifest = "override")]
internal sealed record MessageFormatterOverrideEnvelope([property: AkkaField(1)] MessageFormatterScope Message) : IMessageFormatterOverride;

[AkkaSerializer<IMessageFormatterOverride>("message-override", 120610)]
[AkkaSerializerFormatter<FormatterValue<int>, MessageOverrideFormatter>]
internal sealed partial class MessageFormatterOverrideSerializer : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
