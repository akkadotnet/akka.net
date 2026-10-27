//-----------------------------------------------------------------------
// <copyright file="GeneratorGoldenFormatterCollectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Golden-output gate for formatter resolution at collection ELEMENT, dictionary KEY and dictionary
/// VALUE position (G-1). Kept apart from <c>GeneratorGoldenOutputSpec</c> on purpose: that spec's
/// baselines were captured before this feature and must not move; this corpus adds one new
/// serializer with its own baseline.
/// </summary>
public sealed class GeneratorGoldenFormatterCollectionSpec
{
    private const string HintName = "FormatterPositionsSerializer.AkkaSerialization.g.cs";

    // Each field is a distinct emission path: a reference-type element (nullable and not), a value-type
    // key and a reference-type value, a Nullable<T> value-type element, a nested collection, and an
    // IActorRef formatter that overrides the native IActorRef encoding. The other collection shapes
    // share these paths; FormatterCollectionSpec round-trips every shape.
    internal const string Source = """
        #nullable enable
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Akka.Actor;
        using Akka.Serialization.V2;
        using MessagePack;

        namespace FormatterGolden;

        public interface IProtocol
        {
        }

        public sealed record Tag(string Value);

        public sealed class TagFormatter : IAkkaMessagePackFormatter<Tag>
        {
            public void Write(ref MessagePackWriter writer, Tag value) => writer.Write(value.Value);
            public Tag Read(ref MessagePackReader reader) => new Tag(reader.ReadString() ?? string.Empty);
            public int SizeOf(Tag value) => MessagePackSizes.SizeOfString(value.Value);
        }

        public readonly record struct Celsius(double Degrees);

        public sealed class CelsiusFormatter : IAkkaMessagePackFormatter<Celsius>
        {
            public CelsiusFormatter(ExtendedActorSystem system)
            {
            }

            public void Write(ref MessagePackWriter writer, Celsius value) => writer.Write(value.Degrees);
            public Celsius Read(ref MessagePackReader reader) => new Celsius(reader.ReadDouble());
            public int SizeOf(Celsius value) => 9;
        }

        public sealed class RefFormatter : IAkkaMessagePackFormatter<IActorRef>
        {
            public void Write(ref MessagePackWriter writer, IActorRef value) => writer.Write(value.Path.ToString());
            public IActorRef Read(ref MessagePackReader reader) => ActorRefs.Nobody;
            public int SizeOf(IActorRef value) => Akka.Serialization.SerializerV2.UnknownSize;
        }

        [AkkaSerializable(Manifest = "positions-v1")]
        public sealed record Positions(
            [property: AkkaField(1)] List<Address> ListOfAddress,
            [property: AkkaField(2)] List<Address?> NullableAddressElements,
            [property: AkkaField(3)] Dictionary<Celsius, Address> AddressByCelsiusKey,
            [property: AkkaField(4)] List<Celsius?> ListOfNullableCelsius,
            [property: AkkaField(5)] Dictionary<string, List<Tag>> MapOfTagLists,
            [property: AkkaField(6)] List<IActorRef> ListOfRef) : IProtocol;

        [AkkaSerializer<IProtocol>("formatter-positions", 150201)]
        [AkkaSerializerFormatter<Address, AddressFormatter>]
        [AkkaSerializerFormatter<Tag, TagFormatter>]
        [AkkaSerializerFormatter<Celsius, CelsiusFormatter>]
        [AkkaSerializerFormatter<IActorRef, RefFormatter>]
        public sealed partial class FormatterPositionsSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    [Fact(DisplayName = "Should_EmitByteIdenticalOutput_When_FormattersAppearInEveryCollectionPosition")]
    public void Should_EmitByteIdenticalOutput_When_FormattersAppearInEveryCollectionPosition()
    {
        var result = GeneratorTestHarness.Run(Source);

        result.GeneratorDiagnostics.Should().BeEmpty("the formatter-position corpus is designed to be diagnostics-clean");
        result.GeneratedSources.Keys.Should().BeEquivalentTo(new[] { HintName });

        var failure = GoldenBaseline.Compare(HintName, result.GeneratedSources[HintName]);
        failure.Should().BeNull();
    }

    [Fact(DisplayName = "Should_CompileCleanly_When_GeneratedFromFormatterPositionCorpus")]
    public void Should_CompileCleanly_When_GeneratedFromFormatterPositionCorpus()
    {
        var result = GeneratorTestHarness.Run(Source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompileDiagnostics
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Id != "CS1701")
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("generated code must compile with no errors and no warnings under #nullable enable");
    }
}
