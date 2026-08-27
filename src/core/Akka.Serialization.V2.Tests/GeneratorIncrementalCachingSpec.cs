//-----------------------------------------------------------------------
// <copyright file="GeneratorIncrementalCachingSpec.cs" company="Akka.NET Project">
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
/// Regression guard for the generator's incremental-caching discipline: every cached pipeline
/// model must be symbol-free and value-equatable, and the code-emission output must not consume
/// the Compilation. If someone reintroduces a retained <c>ISymbol</c> (never equal across
/// compilations) or a non-equatable model, the tracked steps below stop reporting
/// <see cref="IncrementalStepRunReason.Cached"/>/<see cref="IncrementalStepRunReason.Unchanged"/>
/// and this spec fails.
/// </summary>
public sealed class GeneratorIncrementalCachingSpec
{
    /// <summary>
    /// Exercises every cached model shape at once: formatter registration
    /// (<c>FormatterInfo</c>), closed generic registration (<c>ClosedGenericRegistrationInfo</c>
    /// carrying a nested <c>MessageInfo</c>), a type-level union (<c>UnionMemberInfo</c>),
    /// collection fields (recursive <c>TypeMapping</c>), and ordinary scalar fields
    /// (<c>FieldInfo</c>/<c>ConstructionPlan</c>).
    /// </summary>
    private const string SerializerSource = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using Akka.Actor;
        using Akka.Serialization.V2;
        using MessagePack;

        namespace CachingSample;

        public interface IProtocol
        {
        }

        public sealed record Foreign(string Value);

        public sealed class ForeignFormatter : IAkkaMessagePackFormatter<Foreign>
        {
            public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
            public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
            public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
        }

        [AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled))]
        public interface IOrderEvent
        {
        }

        [AkkaSerializable(Manifest = "order-placed-v1")]
        public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrderEvent;

        [AkkaSerializable(Manifest = "order-cancelled-v1")]
        public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;

        [AkkaSerializable]
        public sealed record Wrapper<T>(
            [property: AkkaField(1)] string Id,
            [property: AkkaField(2)] T Payload) : IProtocol;

        [AkkaSerializer<IProtocol>("caching-sample", 150001)]
        [AkkaSerializerFormatter<Foreign, ForeignFormatter>]
        [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
        public sealed partial class SampleSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "outer-v1")]
        public sealed record Outer(
            [property: AkkaField(1)] string Name,
            [property: AkkaField(2)] Foreign Foreign,
            [property: AkkaField(3)] IOrderEvent Event,
            [property: AkkaField(4)] List<int> Numbers,
            [property: AkkaField(5)] Dictionary<string, string> Tags) : IProtocol;
        """;

    private const string UnrelatedSourceBefore = """
        namespace CachingSample.Unrelated;

        public static class Untouched
        {
            public static int Value => 1;
        }
        """;

    private const string UnrelatedSourceAfter = """
        namespace CachingSample.Unrelated;

        public static class Untouched
        {
            public static int Value => 2;
        }
        """;

    [Fact(DisplayName = "Generator should reuse every cached pipeline stage across an unrelated edit")]
    public void Generator_should_reuse_cached_stages_across_unrelated_edit()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var serializerTree = CSharpSyntaxTree.ParseText(SerializerSource, parseOptions, path: "Serializer.cs");
        var unrelatedTree = CSharpSyntaxTree.ParseText(UnrelatedSourceBefore, parseOptions, path: "Unrelated.cs");
        var compilation = CreateCompilation(serializerTree, unrelatedTree);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var firstRun = driver.GetRunResult();

        // Guard against a vacuous pass: the baseline source must actually generate, cleanly.
        firstRun.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        firstRun.GeneratedTrees.Should().NotBeEmpty();

        // The unrelated edit: same trees except one file the generator does not care about.
        var editedTree = CSharpSyntaxTree.ParseText(UnrelatedSourceAfter, parseOptions, path: "Unrelated.cs");
        var editedCompilation = compilation.ReplaceSyntaxTree(unrelatedTree, editedTree);

        driver = driver.RunGenerators(editedCompilation);
        var secondRun = driver.GetRunResult();

        // The emitted source must be byte-for-byte identical...
        secondRun.GeneratedTrees.Select(tree => tree.ToString())
            .Should().Equal(firstRun.GeneratedTrees.Select(tree => tree.ToString()));

        // ...and, stronger, every named pipeline stage must have been served from cache
        // (Cached: not re-run; Unchanged: re-run but produced an equal value). Any other reason
        // means a model stopped comparing equal across compilations -- a symbol or other
        // non-equatable state leaked into a cached model.
        var trackedSteps = secondRun.Results.Single().TrackedSteps;
        foreach (var trackingName in AkkaSerializerGenerator.TrackingNames.All)
        {
            trackedSteps.Should().ContainKey(trackingName);
            foreach (var step in trackedSteps[trackingName])
            {
                foreach (var (_, reason) in step.Outputs)
                {
                    (reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged)
                        .Should().BeTrue($"step '{trackingName}' must not recompute on an unrelated edit, but reported '{reason}'");
                }
            }
        }
    }

    [Fact(DisplayName = "Generator should emit source alongside an AKKASG029 coverage error")]
    public void Generator_should_emit_source_alongside_AKKASG029()
    {
        // Documents the pipeline-split gating decision: AKKASG029's whole-compilation coverage
        // scan lives in a diagnostics-only output and no longer suppresses code emission. The
        // build still fails (AKKASG029 is an Error), so the emitted source never ships -- but it
        // IS emitted, keeping the generated members resolvable in the IDE while the user fixes
        // the coverage gap. Before the split, this scenario emitted nothing.
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace CoverageSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("coverage-sample", 150002)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;

            // Forgotten [AkkaSerializable]: invisible to the generated switches.
            public sealed record Forgotten(string Value) : IProtocol;
            """;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var compilation = CreateCompilation(CSharpSyntaxTree.ParseText(source, parseOptions));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG029" && diagnostic.Severity == DiagnosticSeverity.Error);
        runResult.GeneratedTrees.Should().NotBeEmpty("emission is no longer gated by the coverage scan");
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        return CSharpCompilation.Create(
            "AkkaSerializationGeneratorCaching",
            trees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

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
