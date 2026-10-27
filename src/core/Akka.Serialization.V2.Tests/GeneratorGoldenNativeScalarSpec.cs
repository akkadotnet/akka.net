//-----------------------------------------------------------------------
// <copyright file="GeneratorGoldenNativeScalarSpec.cs" company="Akka.NET Project">
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
/// Golden-output gate for the native <c>TimeSpan</c>, <c>float</c>, <c>short</c>, <c>byte</c>,
/// <c>sbyte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c> and <c>char</c> support (G-3). Kept apart from
/// <c>GeneratorGoldenOutputSpec</c> on purpose: that spec's baselines predate this feature and must not
/// move; this corpus adds one new serializer with its own baseline.
/// </summary>
public sealed class GeneratorGoldenNativeScalarSpec
{
    private const string HintName = "ScalarGoldenSerializer.AkkaSerialization.g.cs";

    // Every new scalar as a required field, as a Nullable field, and in the three collection positions
    // (element, dictionary key, dictionary value), with a Nullable element in one of them.
    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using Akka.Serialization.V2;

        namespace ScalarGolden;

        public interface IProtocol
        {
        }

        [AkkaSerializable(Manifest = "required-v1")]
        public sealed record Required(
            [property: AkkaField(1)] TimeSpan Span,
            [property: AkkaField(2)] float Single,
            [property: AkkaField(3)] short Int16,
            [property: AkkaField(4)] byte Byte,
            [property: AkkaField(5)] sbyte SByte,
            [property: AkkaField(6)] ushort UInt16,
            [property: AkkaField(7)] uint UInt32,
            [property: AkkaField(8)] ulong UInt64,
            [property: AkkaField(9)] char Char) : IProtocol;

        [AkkaSerializable(Manifest = "optional-v1")]
        public sealed record Optional(
            [property: AkkaField(1)] TimeSpan? Span,
            [property: AkkaField(2)] float? Single,
            [property: AkkaField(3)] short? Int16,
            [property: AkkaField(4)] byte? Byte,
            [property: AkkaField(5)] sbyte? SByte,
            [property: AkkaField(6)] ushort? UInt16,
            [property: AkkaField(7)] uint? UInt32,
            [property: AkkaField(8)] ulong? UInt64,
            [property: AkkaField(9)] char? Char) : IProtocol;

        [AkkaSerializable(Manifest = "positions-v1")]
        public sealed record Positions(
            [property: AkkaField(1)] List<TimeSpan> Spans,
            [property: AkkaField(2)] List<short?> NullableShorts,
            [property: AkkaField(3)] Dictionary<char, ulong> ByChar,
            [property: AkkaField(4)] Dictionary<TimeSpan, float?> NullableFloatBySpan) : IProtocol;

        [AkkaSerializer<IProtocol>("scalar-golden", 150202)]
        public sealed partial class ScalarGoldenSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    [Fact(DisplayName = "Should_EmitByteIdenticalOutput_When_NativeScalarsAreFieldsAndCollectionMembers")]
    public void Should_EmitByteIdenticalOutput_When_NativeScalarsAreFieldsAndCollectionMembers()
    {
        var result = GeneratorTestHarness.Run(Source);

        result.GeneratorDiagnostics.Should().BeEmpty("the scalar corpus is designed to be diagnostics-clean");
        result.GeneratedSources.Keys.Should().BeEquivalentTo(new[] { HintName });

        var failure = GoldenBaseline.Compare(HintName, result.GeneratedSources[HintName]);
        failure.Should().BeNull();
    }

    [Fact(DisplayName = "Should_CompileCleanly_When_GeneratedFromNativeScalarCorpus")]
    public void Should_CompileCleanly_When_GeneratedFromNativeScalarCorpus()
    {
        var result = GeneratorTestHarness.Run(Source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompileDiagnostics
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Id != "CS1701")
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("generated code must compile with no errors and no warnings under #nullable enable");
    }
}
