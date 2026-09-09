//-----------------------------------------------------------------------
// <copyright file="GeneratorCompilationFactsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Exercises the S5 whole-compilation facts stage's PURE compute entry point,
/// <see cref="AkkaSerializerGenerator.ComputeCompilationFacts"/>, directly on a
/// <see cref="Compilation"/> built by <see cref="GeneratorTestHarness"/> -- with no driver, no
/// incremental pipeline, and no <see cref="SourceProductionContext"/> involved. Complements
/// <see cref="GeneratorIncrementalScenariosSpec"/>, which asserts this stage's CACHING behavior
/// (<see cref="Microsoft.CodeAnalysis.IncrementalStepRunReason"/>) across edits; this spec asserts
/// what the stage actually COMPUTES.
/// </summary>
public sealed class GeneratorCompilationFactsSpec
{
    private const string LocalImplementorsSource = """
        #nullable enable
        using Akka.Serialization.V2;

        namespace FactsSample;

        public interface IProtocol
        {
        }

        public interface IUnrelatedProtocol
        {
        }

        [AkkaSerializable(Manifest = "marked-v1")]
        public sealed record Marked(string Value) : IProtocol;

        // Declared out of alphabetical order deliberately -- Zeta before Alpha -- so a sorted
        // result is actual proof of sorting, not an accident of declaration order.
        public sealed record ZetaUnmarked(string Value) : IProtocol;

        public sealed record AlphaUnmarked(string Value) : IProtocol;

        // Exempt: never a concrete runtime message type (AKKASG029's own documented exemption).
        public abstract class AbstractUnmarked : IProtocol
        {
        }
        """;

    [Fact(DisplayName = "ComputeCompilationFacts should find every local unmarked implementor of a serializer's protocol, sorted deterministically")]
    public void Should_find_sorted_local_unmarked_implementors()
    {
        var compilation = Compile(LocalImplementorsSource);
        var protocolKey = ProtocolKey(compilation, "FactsSample.IProtocol");
        var serializer = BuildSerializerInfo(protocolKey);

        var facts = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilation, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializer), CancellationToken.None);

        facts.LocalUnmarkedImplementorsByProtocol.Should().ContainKey(protocolKey);
        var implementors = facts.LocalUnmarkedImplementorsByProtocol[protocolKey];

        // Marked (has [AkkaSerializable]) and AbstractUnmarked (abstract) are excluded; only the
        // two concrete, unmarked implementors remain, in ASCENDING metadata-name order even though
        // Zeta was declared before Alpha in source.
        implementors.Select(key => key.MetadataName).Should().Equal(
            "FactsSample.AlphaUnmarked", "FactsSample.ZetaUnmarked");

        // A protocol nothing implements (unmarked) still gets its own entry, but empty -- a lookup
        // MISS means "no serializer asked about this protocol", not "no implementors".
        facts.LocalUnmarkedImplementorsByProtocol.Should().NotContainKey(ProtocolKey(compilation, "FactsSample.IUnrelatedProtocol"));

        // Decision 19's referenced-assembly implementor walk is a skeleton in this change: always
        // empty, regardless of what local implementors were found.
        facts.ReferencedAssemblyImplementorsByProtocol.Should().BeEmpty();
    }

    [Fact(DisplayName = "ComputeCompilationFacts should produce equal facts across two independent builds of the same compilation")]
    public void Should_produce_equal_facts_across_two_builds_of_the_same_compilation()
    {
        var compilationA = Compile(LocalImplementorsSource);
        var compilationB = Compile(LocalImplementorsSource);

        var serializerA = BuildSerializerInfo(ProtocolKey(compilationA, "FactsSample.IProtocol"));
        var serializerB = BuildSerializerInfo(ProtocolKey(compilationB, "FactsSample.IProtocol"));

        var factsA = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilationA, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializerA), CancellationToken.None);
        var factsB = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilationB, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializerB), CancellationToken.None);

        // Two structurally identical compilations (fresh symbols each time -- TWO separate Compile()
        // calls, never the same Compilation instance) must still produce EQUAL, symbol-free facts:
        // this is exactly the property that keeps the coverage output (and any future consumer)
        // cached across an edit these facts do not care about.
        factsA.Should().Be(factsB);
        factsA.Equals(factsB).Should().BeTrue();
        factsA.GetHashCode().Should().Be(factsB.GetHashCode());
    }

    private const string ExtraReferenceSourceUsingV2 = """
        using Akka.Serialization.V2;

        namespace FactsSample.ReferencedAssembly;

        [AkkaSerializable(Manifest = "referenced-marker-v1")]
        public sealed class ReferencedMarker
        {
        }
        """;

    private const string ExtraReferenceSourceWithoutV2 = """
        namespace FactsSample.PlainReferencedAssembly;

        public static class PlainHelper
        {
            public static int Value => 1;
        }
        """;

    [Fact(DisplayName = "ComputeCompilationFacts should list a referenced assembly that itself references Akka.Serialization.V2, and ignore one that does not")]
    public void Should_filter_referenced_assemblies_by_whether_they_reference_v2()
    {
        var referenceUsingV2 = GeneratorTestHarness.CompileToReference(ExtraReferenceSourceUsingV2, "FactsReferencedAssemblyUsingV2");
        var referenceWithoutV2 = GeneratorTestHarness.CompileToReference(ExtraReferenceSourceWithoutV2, "FactsReferencedAssemblyWithoutV2");

        var compilation = Compile(LocalImplementorsSource, referenceUsingV2, referenceWithoutV2);

        var facts = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilation, ImmutableArray<AkkaSerializerGenerator.SerializerInfo?>.Empty, CancellationToken.None);

        // FactsReferencedAssemblyUsingV2 actually USES a type from Akka.Serialization.V2
        // ([AkkaSerializable] on ReferencedMarker), so the C# compiler emits a genuine AssemblyRef
        // to Akka.Serialization.V2 into its own compiled metadata -- picked up here.
        facts.ReferencedAssembliesUsingV2.Should().Contain("FactsReferencedAssemblyUsingV2");

        // FactsReferencedAssemblyWithoutV2 uses nothing from V2 at all: even though BOTH assemblies
        // were supplied as MetadataReferences to the SAME base reference set (which itself includes
        // Akka.Serialization.V2), an assembly's own AssemblyRef table only names assemblies it
        // actually uses -- an unused supplied reference is never emitted into it.
        facts.ReferencedAssembliesUsingV2.Should().NotContain("FactsReferencedAssemblyWithoutV2");

        // Sorted (ordinal) -- verified generically rather than pinning an exact full list (the
        // harness's own base references, e.g. the executing test assembly, also reference V2 and
        // legitimately appear here too).
        facts.ReferencedAssembliesUsingV2.Should().Equal(facts.ReferencedAssembliesUsingV2.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static Compilation Compile(string source, params MetadataReference[] extraReferences)
    {
        return GeneratorTestHarness.Run(source, extraReferences).OutputCompilation;
    }

    private static AkkaSerializerGenerator.TypeKey ProtocolKey(Compilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{metadataName}' in the harness compilation.");
        return AkkaSerializerGenerator.TypeKey.FromSymbol(symbol);
    }

    private static AkkaSerializerGenerator.SerializerInfo BuildSerializerInfo(AkkaSerializerGenerator.TypeKey protocolTypeKey)
    {
        return new AkkaSerializerGenerator.SerializerInfo(
            ns: "FactsSample",
            className: "TestSerializer",
            key: new AkkaSerializerGenerator.TypeKey("FactsSample.TestSerializer", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::FactsSample.TestSerializer"),
            fullyQualifiedName: "global::FactsSample.TestSerializer",
            name: "test-serializer",
            serializerId: 1,
            protocolTypeKey: protocolTypeKey,
            protocolTypeIsInterface: true,
            declaredAccessibility: Accessibility.Public,
            formatters: ImmutableArray<AkkaSerializerGenerator.FormatterInfo>.Empty,
            closedGenericRegistrations: ImmutableArray<AkkaSerializerGenerator.ClosedGenericRegistrationInfo>.Empty,
            closedGenericSchemas: ImmutableArray<AkkaSerializerGenerator.MessageInfo>.Empty,
            isPartial: true,
            isGeneric: false,
            derivesFromAkkaSerializerBase: true);
    }
}
