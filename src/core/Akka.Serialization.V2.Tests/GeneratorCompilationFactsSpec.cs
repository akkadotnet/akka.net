//-----------------------------------------------------------------------
// <copyright file="GeneratorCompilationFactsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
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

        // LocalMarkedImplementorsByClosedSetKey is now derived purely from the collected messages
        // array (no symbol walk) -- so, unlike LocalUnmarkedImplementorsByProtocol, it needs the
        // real, extracted MessageInfo for Marked (with its own cached Protocols) to have anything
        // to filter. ParseMessageForTests runs the SAME extraction routine the real per-node
        // pipeline stage does, so Marked.Protocols here is exactly what the pipeline would have
        // collected for this compilation.
        var marked = ParseMessage(compilation, "FactsSample.Marked");

        var facts = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilation,
            ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializer),
            ImmutableArray.Create<AkkaSerializerGenerator.MessageInfo?>(marked),
            CancellationToken.None);

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

        // Decision 19's local closed-set walk finds the SAME marked implementor (Marked), since
        // IProtocol is both the serializer's own protocol key and a requested closed-set key.
        facts.LocalMarkedImplementorsByClosedSetKey.Should().ContainKey(protocolKey);
        facts.LocalMarkedImplementorsByClosedSetKey[protocolKey].Select(key => key.MetadataName).Should().Equal("FactsSample.Marked");

        // No referenced assembly is involved in this compilation at all, so Decision 19's
        // referenced-assembly walk (real, not a skeleton) finds nothing to report -- an empty
        // dictionary, not one padded with an empty entry per requested key (see SortAndBuild's own
        // doc comment for why that convention differs from LocalUnmarkedImplementorsByProtocol's).
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
            compilationA, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializerA), ImmutableArray<AkkaSerializerGenerator.MessageInfo?>.Empty, CancellationToken.None);
        var factsB = AkkaSerializerGenerator.ComputeCompilationFacts(
            compilationB, ImmutableArray.Create<AkkaSerializerGenerator.SerializerInfo?>(serializerB), ImmutableArray<AkkaSerializerGenerator.MessageInfo?>.Empty, CancellationToken.None);

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
            compilation, ImmutableArray<AkkaSerializerGenerator.SerializerInfo?>.Empty, ImmutableArray<AkkaSerializerGenerator.MessageInfo?>.Empty, CancellationToken.None);

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

    private const string CachingProofAssemblySource = """
        #nullable enable
        using System;
        using System.Buffers;
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace FactsCaching.AssemblyA;

        public interface IRemoteProtocol
        {
        }

        // Hand-written, not partial: CompileToReference never runs the generator against this
        // compilation, so there is no partial CreateRegistration to implement. Only the
        // [AkkaSerializer<IRemoteProtocol>] declaration matters here -- it is what forces this
        // assembly's own AssemblyRef table to name Akka.Serialization.V2 (qualifying it for
        // ReferencedAssembliesUsingV2), and it is the one thing ComputeUpstreamSerializerBindings'
        // per-assembly walk exists to find.
        [AkkaSerializer<IRemoteProtocol>("remote-core", 150001)]
        public sealed class RemoteCoreSerializer : AkkaSerializer
        {
            public RemoteCoreSerializer(ExtendedActorSystem system) : base(system) { }
            public override int Identifier => 150001;
            public override string Manifest(object obj) => throw new NotSupportedException();
            public override int Serialize(object obj, IBufferWriter<byte> writer) => throw new NotSupportedException();
            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => throw new NotSupportedException();
        }
        """;

    private const string CachingProofMainBefore = """
        namespace FactsCaching.Main;

        public static class Placeholder
        {
            public static int Value => 1;
        }
        """;

    // An edit to the MAIN compilation's own source only -- a brand-new, wholly unrelated member.
    // AssemblyA itself is never touched.
    private const string CachingProofMainAfter = """
        namespace FactsCaching.Main;

        public static class Placeholder
        {
            public static int Value => 1;
            public static int Other => 2;
        }
        """;

    [Fact(DisplayName = "The referenced-assembly walk is cached per MetadataReference, not per assembly symbol: an edit to the user's own source does not re-walk an unchanged reference, but a genuinely different reference does")]
    public void ReferencedAssemblyWalk_is_cached_per_metadata_reference_not_per_assembly_symbol()
    {
        // This test does NOT rely on Roslyn reusing the same IAssemblySymbol instance across the two
        // runs below -- that reuse only happens while an earlier bound symbol for the reference is
        // still reachable through Roslyn's own internal (weak) symbol cache, which depends on
        // whatever else this process happens to be holding onto at the time, i.e. on things this
        // test does not control. The fix under test (ReferencedAssemblyFactsCache keyed on
        // MetadataReference, not IAssemblySymbol -- see that field's own doc remarks in
        // AkkaSerializerGenerator.Facts.cs) makes the assertions below true regardless of whether
        // Roslyn happens to hand back the same symbol instance or a fresh one for assemblyA on the
        // second run.
        //
        // Nor does this test read the process-wide AkkaSerializerGenerator.ReferencedAssemblyWalkCount
        // (a single global int): this test project's own tests do NOT execute one-at-a-time on a
        // single thread in every observed run -- xunit's own collection scheduler here dispatches
        // work across a pool of worker threads, and a DIFFERENT test's own walk of an assembly with a
        // DIFFERENT name can land inside this test's measurement window, inflating a global counter's
        // delta regardless of what this test itself does. Instead this test reads
        // AkkaSerializerGenerator.ReferencedAssemblyWalksByAssemblyName, filtered to two assembly
        // names this test itself constructs and that no other test in this project ever uses
        // ("FactsCaching.AssemblyA", "FactsCaching.AssemblyA2") -- immune to any concurrently running
        // test's own walk of a differently-named assembly, and snapshotted before the first run below
        // so even a hypothetical earlier run of this exact test in the same process cannot interfere.
        //
        // Warm-up, deliberately excluded from every count below: GeneratorTestHarness.BaseReferences
        // includes the executing test assembly itself (Akka.Serialization.V2.Tests), which also
        // references Akka.Serialization.V2 and so ALSO qualifies for ReferencedAssembliesUsingV2 --
        // every test that runs the generator at all causes it to be walked once, the first time, then
        // cached for the rest of the process. That walk is keyed by ITS OWN name
        // ("Akka.Serialization.V2.Tests"), so it can never collide with either name below -- but the
        // warm-up is kept anyway, since it costs nothing and keeps this test's own timings unaffected
        // by whether it happens to run first in the process.
        GeneratorTestHarness.Run(CachingProofMainBefore);

        var assemblyA = GeneratorTestHarness.CompileToReference(CachingProofAssemblySource, "FactsCaching.AssemblyA");

        var walksAtStart = SnapshotWalkCounts("FactsCaching.AssemblyA", "FactsCaching.AssemblyA2");

        // afterExtraReferences is deliberately omitted (null): the general RunIncremental overload
        // then never calls RemoveReferences/AddReferences at all, so the edited ("after")
        // compilation carries forward the EXACT SAME reference list -- including the exact same
        // assemblyA MetadataReference instance -- the "before" compilation had. This is the
        // "reference set unchanged" case ReferencedAssemblyFactsCache exists for: only the user's
        // own source changed.
        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", CachingProofMainBefore) },
            new[] { new SourceFile("Main.cs", CachingProofMainAfter) },
            new[] { assemblyA },
            null);

        var walksAfterUnrelatedEdit = SnapshotWalkCounts("FactsCaching.AssemblyA", "FactsCaching.AssemblyA2");

        // CompilationFacts itself re-runs on every edit (it combines the CompilationProvider
        // directly, by necessity -- see TrackingNames.CompilationFacts' own doc comment), so
        // ComputeUpstreamSerializerBindings was invoked for assemblyA on BOTH the "before" and the
        // "after" run. But assemblyA's own MetadataReference instance was reused verbatim across
        // both (the reference set never changed) -- the actual, expensive per-assembly walk
        // (ComputeReferencedAssemblyFacts) must therefore have run only ONCE across both runs: a
        // second increment here would mean the cache is not doing its job, and the walk is
        // re-enumerating this assembly's types on every keystroke again.
        (walksAfterUnrelatedEdit["FactsCaching.AssemblyA"] - walksAtStart["FactsCaching.AssemblyA"]).Should().Be(1,
            "assemblyA's own MetadataReference instance was reused verbatim across both runs, so ComputeReferencedAssemblyFacts should not run a second time");
        (walksAfterUnrelatedEdit["FactsCaching.AssemblyA2"] - walksAtStart["FactsCaching.AssemblyA2"]).Should().Be(0,
            "assemblyA2 does not exist yet at this point in the test");

        // Now swap assemblyA for assemblyA2 -- a FRESH compile of the byte-for-byte identical
        // source, so a genuinely different MetadataReference instance. A cache keyed on the
        // reference (rather than on some global/assembly-name key) must treat this as a real miss:
        // proves the cache is scoped to "this particular reference", not "any reference that looks
        // like this one" -- the distinction that matters the day a project's reference is
        // legitimately replaced (a NuGet package bump, a rebuilt project reference), which must not
        // silently keep serving a stale, cached result forever.
        var assemblyA2 = GeneratorTestHarness.CompileToReference(CachingProofAssemblySource, "FactsCaching.AssemblyA2");
        var thirdCompilation = result.After.OutputCompilation
            .RemoveReferences(assemblyA)
            .AddReferences(assemblyA2);

        result.After.Driver.RunGeneratorsAndUpdateCompilation(thirdCompilation, out _, out _);

        var walksAfterReferenceSwap = SnapshotWalkCounts("FactsCaching.AssemblyA", "FactsCaching.AssemblyA2");

        (walksAfterReferenceSwap["FactsCaching.AssemblyA"] - walksAtStart["FactsCaching.AssemblyA"]).Should().Be(1,
            "assemblyA itself was not walked again -- it was removed from the compilation, not edited");
        (walksAfterReferenceSwap["FactsCaching.AssemblyA2"] - walksAtStart["FactsCaching.AssemblyA2"]).Should().Be(1,
            "assemblyA2 is a freshly compiled, genuinely distinct MetadataReference from assemblyA, so this must be a real cache miss");

        // Keep every compilation and reference reachable for the whole test, so nothing here is
        // collected mid-test and no assertion above is accidentally proven by an object lifetime
        // this test does not actually control.
        GC.KeepAlive(assemblyA);
        GC.KeepAlive(assemblyA2);
        GC.KeepAlive(result);
        GC.KeepAlive(thirdCompilation);
    }

    /// <summary>
    /// Reads <see cref="AkkaSerializerGenerator.ReferencedAssemblyWalksByAssemblyName"/> for exactly
    /// the given names, defaulting an absent name to 0 -- a snapshot safe to diff against a later
    /// snapshot from the same call, immune to any OTHER assembly name's own count changing
    /// concurrently in the same process.
    /// </summary>
    private static Dictionary<string, int> SnapshotWalkCounts(params string[] assemblyNames)
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in assemblyNames)
            snapshot[name] = AkkaSerializerGenerator.ReferencedAssemblyWalksByAssemblyName.GetValueOrDefault(name);

        return snapshot;
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

    private static AkkaSerializerGenerator.MessageInfo ParseMessage(Compilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{metadataName}' in the harness compilation.");
        return AkkaSerializerGenerator.ParseMessageForTests(symbol, compilation)
            ?? throw new InvalidOperationException($"'{metadataName}' was not recognized as [AkkaSerializable].");
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
