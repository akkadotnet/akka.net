//-----------------------------------------------------------------------
// <copyright file="SourceGeneratorBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Serialization.V2.Generators;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// Baseline timings for <see cref="AkkaSerializerGenerator"/>'s incremental pipeline: a cold
/// (fresh-driver) full run over a synthetic corpus, plus three warm-driver incremental edits. These
/// are the numbers the Serialization.V2 generator architecture pass (PRs 2, 3, 5, 6) is measured
/// against -- a regression here means a migration PR made the pipeline slower for a shape of edit
/// real projects make constantly, not just moved code around.
/// </summary>
/// <remarks>
/// Uses <see cref="ShortRunBenchmarkConfig"/> (<c>Job.ShortRun</c>) so the whole suite finishes in
/// minutes: these are baseline/directional numbers to catch a regression, not final numbers for a
/// release note.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(ShortRunBenchmarkConfig))]
public class SourceGeneratorBenchmarks
{
    /// <summary>Serializers in the synthetic corpus, each with its own protocol, union, nested value object, and closed-generic registration.</summary>
    [Params(5)]
    public int Serializers { get; set; }

    /// <summary>Messages generated per serializer.</summary>
    [Params(100)]
    public int MessagesPerSerializer { get; set; }

    private CSharpParseOptions _parseOptions = null!;
    private ImmutableArray<MetadataReference> _references;

    private CSharpCompilation _baselineCompilation = null!;
    private CSharpCompilation _fieldRenameCompilation = null!;
    private CSharpCompilation _whitespaceEditCompilation = null!;
    private CSharpCompilation _unrelatedEditCompilation = null!;

    // A driver that has already completed one full run over _baselineCompilation. GeneratorDriver
    // is immutable -- RunGeneratorsAndUpdateCompilation returns a NEW driver and never mutates this
    // one -- so building it once here and reusing it, unchanged, across every timed
    // IncrementalXxx invocation below measures only the SECOND run's cost, not the first.
    private GeneratorDriver _warmedDriver = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        _references = CreateMetadataReferences().ToImmutableArray();

        var mainSource = SourceGeneratorBenchmarkCorpus.BuildSource(Serializers, MessagesPerSerializer);
        var fieldRenameSource = SourceGeneratorBenchmarkCorpus.BuildSource(Serializers, MessagesPerSerializer, renameFirstScalarFieldOfMessage: (0, 0));
        var whitespaceEditSource = mainSource + Environment.NewLine + "// a trivial trailing benchmark comment" + Environment.NewLine;

        const string unrelatedSourceBefore = "namespace SourceGeneratorBenchmarkCorpus.Unrelated;\n\npublic static class Untouched\n{\n    public static int Value => 1;\n}\n";
        const string unrelatedSourceAfter = "namespace SourceGeneratorBenchmarkCorpus.Unrelated;\n\npublic static class Untouched\n{\n    public static int Value => 2;\n}\n";

        var mainTree = CSharpSyntaxTree.ParseText(mainSource, _parseOptions, path: "Main.cs");
        var unrelatedTreeBefore = CSharpSyntaxTree.ParseText(unrelatedSourceBefore, _parseOptions, path: "Unrelated.cs");

        _baselineCompilation = CSharpCompilation.Create(
            "SourceGeneratorBenchmarks",
            new[] { mainTree, unrelatedTreeBefore },
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        _fieldRenameCompilation = _baselineCompilation.ReplaceSyntaxTree(
            mainTree, CSharpSyntaxTree.ParseText(fieldRenameSource, _parseOptions, path: "Main.cs"));

        _whitespaceEditCompilation = _baselineCompilation.ReplaceSyntaxTree(
            mainTree, CSharpSyntaxTree.ParseText(whitespaceEditSource, _parseOptions, path: "Main.cs"));

        _unrelatedEditCompilation = _baselineCompilation.ReplaceSyntaxTree(
            unrelatedTreeBefore, CSharpSyntaxTree.ParseText(unrelatedSourceAfter, _parseOptions, path: "Unrelated.cs"));

        var freshDriver = CreateDriver();
        _warmedDriver = freshDriver.RunGeneratorsAndUpdateCompilation(_baselineCompilation, out _, out _);
    }

    [Benchmark(Description = "Fresh driver, full corpus")]
    public Compilation FullRun()
    {
        var driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(_baselineCompilation, out var outputCompilation, out _);
        return outputCompilation;
    }

    [Benchmark(Description = "Warmed driver, one field renamed in one message")]
    public Compilation IncrementalEditOneMessage()
    {
        var driver = _warmedDriver.RunGeneratorsAndUpdateCompilation(_fieldRenameCompilation, out var outputCompilation, out _);
        return outputCompilation;
    }

    [Benchmark(Description = "Warmed driver, trailing whitespace/comment edit")]
    public Compilation IncrementalWhitespaceEdit()
    {
        var driver = _warmedDriver.RunGeneratorsAndUpdateCompilation(_whitespaceEditCompilation, out var outputCompilation, out _);
        return outputCompilation;
    }

    [Benchmark(Description = "Warmed driver, unrelated file edited")]
    public Compilation IncrementalUnrelatedEdit()
    {
        var driver = _warmedDriver.RunGeneratorsAndUpdateCompilation(_unrelatedEditCompilation, out var outputCompilation, out _);
        return outputCompilation;
    }

    private GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(
            ImmutableArray.Create(new AkkaSerializerGenerator().AsSourceGenerator()),
            parseOptions: _parseOptions);
    }

    // Same reference-gathering pattern as Akka.Serialization.V2.Tests' generator specs -- trusted
    // platform assemblies plus the handful of explicit ones the generator's attributes and runtime
    // types live in.
    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(System.IO.Path.PathSeparator)
            .Where(System.IO.File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(global::Akka.Serialization.V2.AkkaSerializerAttribute<>).Assembly,
            typeof(global::Akka.Serialization.SerializerV2).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
        };

        return trustedAssemblies
            .Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}

/// <summary>
/// Builds a synthetic <c>[AkkaSerializable]</c>/<c>[AkkaSerializer]</c> corpus of a given size, for
/// benchmarking the generator pipeline rather than testing its correctness. Each serializer gets:
/// a scalar-field message mix, one nested value object (a <c>readonly record struct</c>), one
/// <c>[AkkaUnion]</c> with three members, and one closed-generic <c>[AkkaSerializable&lt;T&gt;]</c>
/// registration.
/// </summary>
internal static class SourceGeneratorBenchmarkCorpus
{
    /// <summary>
    /// Builds the corpus. When <paramref name="renameFirstScalarFieldOfMessage"/> is given
    /// (serializer index, message index), that one message's first scalar field is named
    /// "RenamedField" instead of "Name" -- a structurally distinct source used to drive
    /// <c>IncrementalEditOneMessage</c> without any fragile string-replace over the generated text.
    /// </summary>
    public static string BuildSource(int serializers, int messagesPerSerializer, (int SerializerIndex, int MessageIndex)? renameFirstScalarFieldOfMessage = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Akka.Actor;");
        sb.AppendLine("using Akka.Serialization.V2;");
        sb.AppendLine();
        sb.AppendLine("namespace SourceGeneratorBenchmarkCorpus;");
        sb.AppendLine();

        for (var s = 0; s < serializers; s++)
        {
            AppendSerializer(sb, s, messagesPerSerializer, renameFirstScalarFieldOfMessage);
        }

        return sb.ToString();
    }

    private static void AppendSerializer(StringBuilder sb, int s, int messagesPerSerializer, (int SerializerIndex, int MessageIndex)? renamed)
    {
        sb.AppendLine($"public interface IProtocol{s}");
        sb.AppendLine("{");
        sb.AppendLine("}");
        sb.AppendLine();

        // One [AkkaUnion] with three members.
        sb.AppendLine($"[AkkaUnion(typeof(UnionMember{s}A), typeof(UnionMember{s}B), typeof(UnionMember{s}C))]");
        sb.AppendLine($"public interface IUnion{s}");
        sb.AppendLine("{");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"[AkkaSerializable(Manifest = \"union{s}-a-v1\")]");
        sb.AppendLine($"public sealed record UnionMember{s}A([property: AkkaField(1)] string Value) : IUnion{s};");
        sb.AppendLine();
        sb.AppendLine($"[AkkaSerializable(Manifest = \"union{s}-b-v1\")]");
        sb.AppendLine($"public sealed record UnionMember{s}B([property: AkkaField(1)] int Value) : IUnion{s};");
        sb.AppendLine();
        sb.AppendLine($"[AkkaSerializable(Manifest = \"union{s}-c-v1\")]");
        sb.AppendLine($"public sealed record UnionMember{s}C([property: AkkaField(1)] bool Value) : IUnion{s};");
        sb.AppendLine();

        // One nested value object.
        sb.AppendLine($"[AkkaSerializable]");
        sb.AppendLine($"public readonly record struct NestedValue{s}(");
        sb.AppendLine("    [property: AkkaField(1)] double X,");
        sb.AppendLine("    [property: AkkaField(2)] double Y);");
        sb.AppendLine();

        // One generic definition, registered as a closed construction on the serializer below.
        // It implements IProtocol{s} directly, so the closed construction is reachable without
        // needing to be referenced from any message field (see design.md Decision 13 / the golden
        // corpus's Wrapper<T> for the same pattern).
        sb.AppendLine($"[AkkaSerializable]");
        sb.AppendLine($"public sealed record Wrapper{s}<T>(");
        sb.AppendLine("    [property: AkkaField(1)] string Id,");
        sb.AppendLine($"    [property: AkkaField(2)] T Payload) : IProtocol{s};");
        sb.AppendLine();

        for (var m = 0; m < messagesPerSerializer; m++)
        {
            var firstFieldName = renamed is { SerializerIndex: var rs, MessageIndex: var rm } && rs == s && rm == m
                ? "RenamedField"
                : "Name";

            sb.AppendLine($"[AkkaSerializable(Manifest = \"msg-{s}-{m}-v1\")]");
            sb.Append($"public sealed record Message{s}_{m}(");

            if (m == 0)
            {
                // One message per serializer carries the nested value object field.
                sb.AppendLine();
                sb.AppendLine($"    [property: AkkaField(1)] string {firstFieldName},");
                sb.AppendLine("    [property: AkkaField(2)] int Count,");
                sb.AppendLine("    [property: AkkaField(3)] bool Flag,");
                sb.AppendLine("    [property: AkkaField(4)] double Amount,");
                sb.AppendLine($"    [property: AkkaField(5)] NestedValue{s} Location) : IProtocol{s};");
            }
            else if (m == 1)
            {
                // One message per serializer carries the union field.
                sb.AppendLine();
                sb.AppendLine($"    [property: AkkaField(1)] string {firstFieldName},");
                sb.AppendLine("    [property: AkkaField(2)] int Count,");
                sb.AppendLine("    [property: AkkaField(3)] bool Flag,");
                sb.AppendLine("    [property: AkkaField(4)] double Amount,");
                sb.AppendLine($"    [property: AkkaField(5)] IUnion{s} Event) : IProtocol{s};");
            }
            else
            {
                // Everything else: a plain scalar-field mix.
                sb.AppendLine();
                sb.AppendLine($"    [property: AkkaField(1)] string {firstFieldName},");
                sb.AppendLine("    [property: AkkaField(2)] int Count,");
                sb.AppendLine("    [property: AkkaField(3)] long Sequence,");
                sb.AppendLine("    [property: AkkaField(4)] bool Flag,");
                sb.AppendLine($"    [property: AkkaField(5)] double Amount) : IProtocol{s};");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"[AkkaSerializer<IProtocol{s}>(\"bench-serializer-{s}\", {190000 + s})]");
        sb.AppendLine($"[AkkaSerializable<Wrapper{s}<int>>(Manifest = \"wrapper{s}-int-v1\")]");
        sb.AppendLine($"public sealed partial class BenchSerializer{s} : AkkaSerializer");
        sb.AppendLine("{");
        sb.AppendLine("    public static partial SerializerRegistration CreateRegistration();");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
