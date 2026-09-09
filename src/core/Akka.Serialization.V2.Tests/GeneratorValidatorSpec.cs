//-----------------------------------------------------------------------
// <copyright file="GeneratorValidatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Exercises the generator's PURE validation entry points -- <see cref="AkkaSerializerGenerator.EvaluateGate"/>,
/// <see cref="AkkaSerializerGenerator.Validate"/>, and <see cref="AkkaSerializerGenerator.ValidateProtocolCoverage"/>
/// -- directly on models, with no <see cref="SourceProductionContext"/>, driver, or generator run at
/// all. Message models come from <see cref="AkkaSerializerGenerator.ParseMessageForTests"/> (the same
/// test-only extraction entry point <see cref="GeneratorMessageModelSnapshotSpec"/> uses) against a
/// <see cref="Compilation"/> built by <see cref="GeneratorTestHarness"/>; <see cref="AkkaSerializerGenerator.SerializerInfo"/>
/// values are hand-built (there is no test-only serializer-extraction entry point, and none of these
/// scenarios need a real <c>[AkkaSerializer]</c> declaration). Every assertion below is on the
/// returned <see cref="AkkaSerializerGenerator.DiagnosticSpec"/> values themselves -- key and message
/// arguments -- never on rendered message text, which the driver-based
/// <see cref="AkkaSerializerGeneratorDiagnosticsSpec"/> already covers end to end.
/// </summary>
public sealed class GeneratorValidatorSpec
{
    private const string ProtocolMetadataName = "ValidatorSample.IProtocol";

    [Fact(DisplayName = "Validate should report no diagnostics for a well-formed top-level message")]
    public void Validate_should_report_no_diagnostics_for_a_valid_pair()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var outer = ParseMessage(compilation, "ValidatorSample.Outer");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(outer));

        diagnostics.Should().BeEmpty();
    }

    [Fact(DisplayName = "Validate should report AKKASG005 when two fields of a message share an index")]
    public void Validate_should_report_AKKASG005_when_fields_share_index()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "dup-index-v1")]
            public sealed record DupIndex([property: AkkaField(1)] string A, [property: AkkaField(1)] string B) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var dupIndex = ParseMessage(compilation, "ValidatorSample.DupIndex");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(dupIndex));

        diagnostics.Should().Contain(new AkkaSerializerGenerator.DiagnosticSpec(
            AkkaSerializerGenerator.DiagnosticKey.DuplicateFieldIndex,
            new AkkaSerializerGenerator.LocationKey(dupIndex.Key, string.Empty),
            "ValidatorSample.DupIndex", "1"));
    }

    [Fact(DisplayName = "Validate should report AKKASG006 when a top-level message has no manifest")]
    public void Validate_should_report_AKKASG006_when_manifest_missing()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record NoManifest([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var noManifest = ParseMessage(compilation, "ValidatorSample.NoManifest");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(noManifest));

        diagnostics.Should().Contain(new AkkaSerializerGenerator.DiagnosticSpec(
            AkkaSerializerGenerator.DiagnosticKey.MissingManifest,
            new AkkaSerializerGenerator.LocationKey(noManifest.Key, string.Empty),
            "ValidatorSample.NoManifest"));
    }

    [Fact(DisplayName = "Validate should report AKKASG012 when two top-level messages of the same serializer duplicate a manifest")]
    public void Validate_should_report_AKKASG012_when_manifests_duplicate()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageA([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageB([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var messageA = ParseMessage(compilation, "ValidatorSample.MessageA");
        var messageB = ParseMessage(compilation, "ValidatorSample.MessageB");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(messageA, messageB));

        diagnostics.Should().Contain(new AkkaSerializerGenerator.DiagnosticSpec(
            AkkaSerializerGenerator.DiagnosticKey.DuplicateManifest,
            new AkkaSerializerGenerator.LocationKey(serializer.Key, string.Empty),
            "TestSerializer", "shared-v1", "ValidatorSample.MessageA, ValidatorSample.MessageB"));
    }

    [Fact(DisplayName = "Validate should report AKKASG015 when a union member is not serializable")]
    public void Validate_should_report_AKKASG015_when_union_member_not_serializable()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            public sealed record NotSerializable(string Value) : IEvent;

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaUnion(typeof(NotSerializable))] IEvent Event) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var outer = ParseMessage(compilation, "ValidatorSample.Outer");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(outer));

        diagnostics.Should().Contain(new AkkaSerializerGenerator.DiagnosticSpec(
            AkkaSerializerGenerator.DiagnosticKey.UnionMemberNotSerializable,
            new AkkaSerializerGenerator.LocationKey(outer.Key, "Event"),
            "ValidatorSample.NotSerializable", "Event", "ValidatorSample.Outer"));
    }

    [Fact(DisplayName = "Validate should report AKKASG023 when a closed generic field type is not registered")]
    public void Validate_should_report_AKKASG023_when_closed_generic_field_not_registered()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload);

            [AkkaSerializable]
            public sealed record Payload([property: AkkaField(1)] string Value);

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] Wrapper<Payload> Inner) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));

        // Deliberately NOT including Wrapper<T>'s or Payload's models: the pipeline never needs
        // Wrapper<Payload> to be separately extracted to notice it is unregistered -- Outer's own
        // field mapping already carries IsGenericConstruction=true (from Wrapper<T>'s own
        // [AkkaSerializable]) purely from extracting Outer.
        var outer = ParseMessage(compilation, "ValidatorSample.Outer");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(outer));

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Key == AkkaSerializerGenerator.DiagnosticKey.UnregisteredClosedGenericField &&
            diagnostic.MessageArgs[0] == "Inner" &&
            diagnostic.MessageArgs[1] == "ValidatorSample.Outer" &&
            diagnostic.MessageArgs[3] == "TestSerializer");
    }

    [Fact(DisplayName = "Validate should not report an error when a closed generic registration is orphaned (AKKASG034 retired, Decision 18)")]
    public void Validate_should_not_report_an_error_when_registration_is_orphaned()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var compilation = Compile(source);

        // Hand-built rather than parsed: AkkaSerializerGenerator.ParseMessageForTests treats ANY
        // generic symbol (INamedTypeSymbol.IsGenericType is true for a CLOSED construction too, not
        // only an open definition) as a generic-definition placeholder, so it cannot produce a real
        // closed-construction model like this one -- exactly the shape ExtractClosedGenericRegistrations
        // builds via the (non-test-exposed) ExtractMessageCore in the real pipeline. Building it by
        // hand is simpler and more direct than constructing the Wrapper<int> symbol through Roslyn.
        var wrapperIntKey = new AkkaSerializerGenerator.TypeKey(
            "ValidatorSample.Wrapper`1",
            ImmutableArray.Create(new AkkaSerializerGenerator.TypeKey("System.Int32", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "int")),
            "global::ValidatorSample.Wrapper<int>");

        var wrapperInt = new AkkaSerializerGenerator.MessageInfo(
            simpleName: "Wrapper",
            key: wrapperIntKey,
            manifest: "wrapper-int-v1",
            fields: ImmutableArray.Create(
                new AkkaSerializerGenerator.FieldInfo(1, "Id", "string", new AkkaSerializerGenerator.TypeMapping(AkkaSerializerGenerator.FieldKind.String), isNullable: false),
                new AkkaSerializerGenerator.FieldInfo(2, "Payload", "int", new AkkaSerializerGenerator.TypeMapping(AkkaSerializerGenerator.FieldKind.Int32), isNullable: false)),
            protocols: ImmutableArray<string>.Empty, // does not implement IProtocol
            allowEmpty: false,
            invalidFields: ImmutableArray<AkkaSerializerGenerator.InvalidFieldInfo>.Empty,
            constructionPlan: AkkaSerializerGenerator.ConstructionPlan.Empty,
            isGenericDefinition: false,
            definitionFullName: "global::ValidatorSample.Wrapper<T>");

        var registration = new AkkaSerializerGenerator.ClosedGenericRegistrationInfo(wrapperInt.Key, manifest: "wrapper-int-v1", allowEmpty: false);
        var serializer = BuildSerializerInfo(
            ProtocolFullName(compilation),
            closedGenericRegistrations: ImmutableArray.Create(registration),
            closedGenericSchemas: ImmutableArray.Create(wrapperInt));

        // Outer neither references Wrapper<int> nor is related to it -- the registration is
        // reachable from nothing. Before Decision 18 this "orphaned" condition was AKKASG034, and it
        // suppressed emission of the whole serializer. Decision 18 retires that check: a registration
        // on the serializer ADOPTS the type unconditionally, so Wrapper<int> is now a top-level
        // message of its own, with no error at all.
        var outer = ParseMessage(compilation, "ValidatorSample.Outer");

        var diagnostics = AkkaSerializerGenerator.Validate(serializer, ImmutableArray.Create(outer));

        diagnostics.Should().BeEmpty();

        var resolved = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(outer));
        resolved.IsEmittable.Should().BeTrue();
        resolved.TopLevelMessages.Members.Should().Contain(member => member.Key.Equals(wrapperInt.Key));
    }

    [Fact(DisplayName = "ValidateProtocolCoverage should report AKKASG029 when a protocol message forgets [AkkaSerializable]")]
    public void ValidateProtocolCoverage_should_report_AKKASG029_for_unmarked_protocol_message()
    {
        const string source = """
            #nullable enable
            namespace ValidatorSample;

            public interface IProtocol
            {
            }

            public sealed record Unmarked(string Value) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));

        var facts = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilation, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializer), CancellationToken.None);
        var diagnostics = AkkaSerializerGenerator.ValidateProtocolCoverage(serializer, facts);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Key == AkkaSerializerGenerator.DiagnosticKey.ProtocolMessageNotSerializable &&
            diagnostic.MessageArgs[0] == "ValidatorSample.Unmarked" &&
            diagnostic.MessageArgs[1] == "ValidatorSample.IProtocol" &&
            diagnostic.MessageArgs[2] == "TestSerializer");
    }

    [Fact(DisplayName = "EvaluateGate should report AKKASG032 and gate out a non-partial serializer before any message validation runs")]
    public void EvaluateGate_should_report_AKKASG032_for_non_partial_serializer()
    {
        var serializer = BuildSerializerInfo(protocolTypeKey: default, isPartial: false);

        var gate = AkkaSerializerGenerator.EvaluateGate(
            serializer,
            ImmutableDictionary<int, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<AkkaSerializerGenerator.MessageInfo>.Empty);

        gate.IsEmittable.Should().BeFalse();
        gate.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Key == AkkaSerializerGenerator.DiagnosticKey.InvalidSerializerShape &&
            diagnostic.MessageArgs[0] == "TestSerializer");
    }

    private static Compilation Compile(string source)
    {
        return GeneratorTestHarness.Run(source).OutputCompilation;
    }

    private static AkkaSerializerGenerator.TypeKey ProtocolFullName(Compilation compilation)
    {
        var symbol = compilation.GetTypeByMetadataName(ProtocolMetadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{ProtocolMetadataName}' in the harness compilation.");
        return AkkaSerializerGenerator.TypeKey.FromSymbol(symbol);
    }

    private static AkkaSerializerGenerator.MessageInfo ParseMessage(Compilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{metadataName}' in the harness compilation.");
        return AkkaSerializerGenerator.ParseMessageForTests(symbol, compilation)
            ?? throw new InvalidOperationException($"'{metadataName}' was not recognized as [AkkaSerializable].");
    }

    private static AkkaSerializerGenerator.SerializerInfo BuildSerializerInfo(
        AkkaSerializerGenerator.TypeKey protocolTypeKey,
        bool protocolTypeIsInterface = true,
        ImmutableArray<AkkaSerializerGenerator.ClosedGenericRegistrationInfo> closedGenericRegistrations = default,
        ImmutableArray<AkkaSerializerGenerator.MessageInfo> closedGenericSchemas = default,
        ImmutableArray<AkkaSerializerGenerator.FormatterInfo> formatters = default,
        bool isPartial = true,
        bool isGeneric = false,
        bool derivesFromAkkaSerializerBase = true)
    {
        return new AkkaSerializerGenerator.SerializerInfo(
            ns: "ValidatorSample",
            className: "TestSerializer",
            key: new AkkaSerializerGenerator.TypeKey("ValidatorSample.TestSerializer", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::ValidatorSample.TestSerializer"),
            fullyQualifiedName: "global::ValidatorSample.TestSerializer",
            name: "test-serializer",
            serializerId: 1,
            protocolTypeKey: protocolTypeKey,
            protocolTypeIsInterface: protocolTypeIsInterface,
            declaredAccessibility: Accessibility.Public,
            formatters: formatters.IsDefault ? ImmutableArray<AkkaSerializerGenerator.FormatterInfo>.Empty : formatters,
            closedGenericRegistrations: closedGenericRegistrations.IsDefault ? ImmutableArray<AkkaSerializerGenerator.ClosedGenericRegistrationInfo>.Empty : closedGenericRegistrations,
            closedGenericSchemas: closedGenericSchemas.IsDefault ? ImmutableArray<AkkaSerializerGenerator.MessageInfo>.Empty : closedGenericSchemas,
            isPartial: isPartial,
            isGeneric: isGeneric,
            derivesFromAkkaSerializerBase: derivesFromAkkaSerializerBase);
    }
}
