//-----------------------------------------------------------------------
// <copyright file="CrossAssemblyBaselineSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// CHARACTERIZATION tests (issue #8384, PR 1): pin what <see cref="AkkaSerializerGenerator"/>
/// ACTUALLY does today when a serializable type lives in a REFERENCED assembly rather than the
/// compilation that hosts the [AkkaSerializer] module. These assert observed behavior, not
/// intended behavior -- see the design.md Decision 13 note this spec verifies (the "scoped to the
/// current compilation" claim).
/// </summary>
/// <remarks>
/// The harness compiles a small "assembly A" from source into an in-memory <see cref="MetadataReference"/>
/// (no generator run against it -- A never hosts a serializer), then compiles "assembly B"
/// referencing A plus the same base references <see cref="AkkaSerializerGeneratorDiagnosticsSpec"/>
/// uses, and runs the generator against B exactly as that spec does.
/// </remarks>
public sealed class CrossAssemblyBaselineSpec
{
    [Fact(DisplayName = "Cross-assembly baseline: nested field type declared and [AkkaSerializable] in a referenced assembly fails AKKASG007 with a cross-assembly hint naming the type and its assembly, never the AKKASG023 closed-generic mislabel")]
    public void Nested_field_type_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case1.AssemblyA;

            [AkkaSerializable]
            public sealed record Money([property: AkkaField(1)] long Cents);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case1.AssemblyA;

            namespace CrossAssemblyBaseline.Case1.AssemblyB;

            public interface IShop { }

            [AkkaSerializable(Manifest = "pay-v1")]
            public sealed record Pay([property: AkkaField(1)] Money Amount) : IShop;

            [AkkaSerializer<IShop>("shop", 130001)]
            public sealed partial class ShopSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case1.A");
        var (generatorDiagnostics, compileDiagnostics, _) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case1.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // CONTRACT: [AkkaSerializable] detection reads attributes off the type SYMBOL, which is
        // visible cross-assembly, so Money maps to FieldKind.Object; but the "known message"
        // registry (messagesByType) is built from the syntax provider, which only sees declarations
        // local to B, so the Object mapping still resolves to no known message. That case is now
        // told apart from a genuine unregistered closed generic by the message-dictionary key
        // shape (Money's key carries no '<'), so it reports AKKASG007 -- naming the offending type
        // and its declaring assembly, and pointing at the two fixes that actually work today --
        // never AKKASG023. Money already carries [AkkaSerializable] and [AkkaField] in assembly A,
        // so the message must NOT claim the type lacks them (annotating it there fixes nothing
        // until this generator can read schemas across assemblies).
        all.Should().NotContain(d => d.Id == "AKKASG023");
        all.Should().Contain(d =>
            d.Id == "AKKASG007" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Amount", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("Money", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("CrossAssemblyBaseline.Case1.A", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("AkkaSerializerFormatter<CrossAssemblyBaseline.Case1.AssemblyA.Money, TFormatter>", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("ShopSerializer", StringComparison.Ordinal) &&
            !d.GetMessage(null).Contains("Add [AkkaSerializable]", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Cross-assembly baseline: union members declared and [AkkaSerializable] in a referenced assembly fail AKKASG015 (not found), even though the type-level [AkkaUnion] declaration on the interface IS discovered")]
    public void Union_members_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case2.AssemblyA;

            [AkkaUnion(typeof(Placed), typeof(Cancelled))]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record Placed([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record Cancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case2.AssemblyA;

            namespace CrossAssemblyBaseline.Case2.AssemblyB;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "order-notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrders;

            [AkkaSerializer<IOrders>("orders", 130002)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case2.A");
        var (generatorDiagnostics, compileDiagnostics, _) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case2.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // CONTRACT: the type-level [AkkaUnion(...)] on IOrderEvent IS found (symbol-based attribute
        // discovery works cross-assembly), so the field maps to FieldKind.Union and AKKASG003 never
        // fires. But union member validation resolves each member against the same syntax-provider
        // `messagesByType`, which knows nothing about Placed/Cancelled -- so both members fail as
        // "not serializable" (AKKASG015), each now carrying the cross-assembly hint naming the
        // member type, its declaring assembly, and the two fixes that actually work today. Placed
        // and Cancelled already carry [AkkaSerializable] and a manifest in assembly A, so the
        // message must NOT claim otherwise.
        all.Should().NotContain(d => d.Id == "AKKASG003");
        all.Should().Contain(d =>
            d.Id == "AKKASG015" &&
            d.GetMessage(null).Contains("Placed", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("CrossAssemblyBaseline.Case2.A", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("AkkaSerializerFormatter<CrossAssemblyBaseline.Case2.AssemblyA.Placed, TFormatter>", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("OrdersSerializer", StringComparison.Ordinal) &&
            !d.GetMessage(null).Contains("Add [AkkaSerializable]", StringComparison.Ordinal));
        all.Should().Contain(d =>
            d.Id == "AKKASG015" &&
            d.GetMessage(null).Contains("Cancelled", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("CrossAssemblyBaseline.Case2.A", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("AkkaSerializerFormatter<CrossAssemblyBaseline.Case2.AssemblyA.Cancelled, TFormatter>", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("OrdersSerializer", StringComparison.Ordinal) &&
            !d.GetMessage(null).Contains("Add [AkkaSerializable]", StringComparison.Ordinal));
        all.Count(d => d.Id == "AKKASG015").Should().Be(2);
    }

    [Fact(DisplayName = "Cross-assembly baseline: a generic [AkkaSerializable] definition from a referenced assembly compiles clean and emits Wrapper<int> helpers when closed-generic-registered in B, but still fails AKKASG023 when it is not")]
    public void Generic_definition_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case3.AssemblyA;

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Body);
            """;

        const string sourceBRegistered = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case3.AssemblyA;

            namespace CrossAssemblyBaseline.Case3.AssemblyB;

            public interface IWrap { }

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Wrapper<int> Count) : IWrap;

            [AkkaSerializer<IWrap>("wrap", 130003)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrap-int-v1")]
            public sealed partial class WrapSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case3.A");
        var (registeredGeneratorDiagnostics, registeredCompileDiagnostics, generatedSource) =
            RunGeneratorAgainstB(sourceBRegistered, assemblyA, "CrossAssemblyBaseline.Case3.Registered");
        var registeredAll = registeredGeneratorDiagnostics.AddRange(registeredCompileDiagnostics);

        // OBSERVED: closed-generic registration extraction reads the generic DEFINITION's
        // [AkkaSerializable]/[AkkaField] data straight off the type symbol (not syntax), and the
        // registered construction Wrapper<int> is injected into messagesByType directly by that
        // extraction step rather than the syntax provider -- sidestepping the gap cases 1/2 hit.
        // No errors; the helper methods are emitted.
        registeredAll.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("WriteWrapperInt");
        generatedSource.Should().Contain("ReadWrapperInt");

        // Second variant: same referenced-assembly definition and field usage, but with NO
        // [AkkaSerializable<Wrapper<int>>] registration on the serializer.
        const string sourceBUnregistered = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case3.AssemblyA;

            namespace CrossAssemblyBaseline.Case3.AssemblyB;

            public interface IWrap { }

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Wrapper<int> Count) : IWrap;

            [AkkaSerializer<IWrap>("wrap", 130004)]
            public sealed partial class WrapSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var (unregisteredGeneratorDiagnostics, unregisteredCompileDiagnostics, _) =
            RunGeneratorAgainstB(sourceBUnregistered, assemblyA, "CrossAssemblyBaseline.Case3.Unregistered");
        var unregisteredAll = unregisteredGeneratorDiagnostics.AddRange(unregisteredCompileDiagnostics);

        unregisteredAll.Should().Contain(d =>
            d.Id == "AKKASG023" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Wrapper", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Cross-assembly baseline: registering a closed generic construction of an unreachable, non-protocol referenced-assembly definition fires AKKASG034 and suppresses ALL emission for that serializer")]
    public void Unreachable_closed_generic_registration_of_customer_envelope()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case4.AssemblyA;

            [AkkaSerializable]
            public sealed record Envelope<T>(
                [property: AkkaField(1)] T Message,
                [property: AkkaField(2)] string TraceId);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case4.AssemblyA;

            namespace CrossAssemblyBaseline.Case4.AssemblyB;

            public interface IComms { }

            [AkkaSerializable(Manifest = "dmac")]
            public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : IComms;

            [AkkaSerializer<IComms>("comms", 130005)]
            [AkkaSerializable<Envelope<AcceptCassette>>(Manifest = "env-dmac")]
            public sealed partial class CommsSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case4.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case4.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // Envelope<AcceptCassette> implements nothing and is not a field of any message reachable
        // from a top-level message -- the registration has no effect either way, regardless of
        // where the generic definition lives.
        all.Should().Contain(d =>
            d.Id == "AKKASG034" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage(null).Contains("Envelope", StringComparison.Ordinal) &&
            d.GetMessage(null).Contains("IComms", StringComparison.Ordinal));

        // OBSERVED (not what the task brief assumed): AKKASG034 fails ValidateClosedGenericProtocolCoverage,
        // which makes the pipeline `continue` past AddSource for THIS SERIALIZER ENTIRELY -- there is
        // no partial CommsSerializer file at all, so there's no "binds only IComms" list to inspect.
        generatedSource.Should().BeEmpty("AKKASG034 fails the whole serializer's coverage check, so the pipeline skips AddSource for CommsSerializer entirely");
    }

    [Fact(DisplayName = "Cross-assembly baseline: [AkkaEnvelopePayload] on a generic property, substituted through a referenced-assembly definition and made reachable, is still recognized as an envelope payload")]
    public void Envelope_payload_on_generic_property_from_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case5.AssemblyA;

            [AkkaSerializable]
            public sealed record Envelope<T>(
                [property: AkkaField(1), AkkaEnvelopePayload] T Message,
                [property: AkkaField(2)] string TraceId);
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case5.AssemblyA;

            namespace CrossAssemblyBaseline.Case5.AssemblyB;

            public interface IComms { }

            [AkkaSerializable(Manifest = "dmac")]
            public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : IComms;

            [AkkaSerializable(Manifest = "holder-v1")]
            public sealed record Holder([property: AkkaField(1)] Envelope<IComms> Inner) : IComms;

            [AkkaSerializer<IComms>("comms", 130006)]
            [AkkaSerializable<Envelope<IComms>>(Manifest = "env-any")]
            public sealed partial class CommsSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case5.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case5.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // OBSERVED: Roslyn's substituted-member symbol (Envelope<IComms>.Message) returns the
        // ORIGINAL DEFINITION's attributes, including [AkkaEnvelopePayload], regardless of which
        // assembly declared the generic definition -- so the generator treats the field as an
        // envelope payload boundary instead of failing AKKASG003 (T substituted to an interface
        // would otherwise be unsupported).
        all.Should().NotContain(d => d.Id == "AKKASG003");
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("WriteEnvelopePayload");
    }

    [Fact(DisplayName = "Cross-assembly baseline: a protocol implementor declared only in a referenced assembly is invisible to AKKASG029's compilation-local scan, and gets no Manifest dispatch arm")]
    public void Protocol_implementor_declared_only_in_referenced_assembly()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace CrossAssemblyBaseline.Case6.AssemblyA;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrders;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using CrossAssemblyBaseline.Case6.AssemblyA;

            namespace CrossAssemblyBaseline.Case6.AssemblyB;

            [AkkaSerializer<IOrders>("orders", 130007)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = CompileAssemblyAToReference(sourceA, "CrossAssemblyBaseline.Case6.A");
        var (generatorDiagnostics, compileDiagnostics, generatedSource) = RunGeneratorAgainstB(sourceB, assemblyA, "CrossAssemblyBaseline.Case6.B");
        var all = generatorDiagnostics.AddRange(compileDiagnostics);

        // AKKASG029's protocol-coverage walk only enumerates named types DECLARED IN THIS
        // COMPILATION, so OrderPlaced (declared only in A) is never visited or flagged, even
        // though it implements IOrders. The generated switch has no knowledge OrderPlaced exists.
        all.Should().NotContain(d => d.Id == "AKKASG029");
        generatedSource.Should().NotContain("OrderPlaced");
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    private static MetadataReference CompileAssemblyAToReference(string source, string assemblyName)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        errors.Should().BeEmpty($"assembly A ('{assemblyName}') must compile with no errors -- it does no generator work, it only supplies types for B to reference");

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue($"assembly A ('{assemblyName}') must emit successfully");

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileDiagnostics, string GeneratedSource) RunGeneratorAgainstB(
        string sourceB, MetadataReference assemblyAReference, string assemblyName)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceB, parseOptions);
        var references = CreateMetadataReferences().Append(assemblyAReference);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatedSource = string.Join(
            Environment.NewLine,
            runResult.GeneratedTrees.Select(tree => tree.ToString()));

        return (generatorDiagnostics, updatedCompilation.GetDiagnostics(), generatedSource);
    }

    // Reused verbatim from AkkaSerializerGeneratorDiagnosticsSpec: the same base reference set, so
    // the [AkkaSerializer]/[AkkaSerializable] attributes and runtime types resolve identically.
    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(AkkaSerializerAttribute<>).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies.Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}
