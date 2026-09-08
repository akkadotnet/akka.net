//-----------------------------------------------------------------------
// <copyright file="GeneratorTestHarness.cs" company="Akka.NET Project">
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
using System.Threading;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Serialization.V2.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akka.Serialization.V2.Tests.Harness;

/// <summary>
/// One source file fed to <see cref="GeneratorTestHarness"/>: a syntax tree's committed
/// <see cref="Path"/> (its identity across an incremental "before"/"after" pair, so
/// <see cref="GeneratorTestHarness.RunIncremental(System.Collections.Generic.IReadOnlyList{Akka.Serialization.V2.Tests.Harness.SourceFile},System.Collections.Generic.IReadOnlyList{Akka.Serialization.V2.Tests.Harness.SourceFile},System.Collections.Generic.IReadOnlyList{Microsoft.CodeAnalysis.MetadataReference}?,System.Collections.Generic.IReadOnlyList{Microsoft.CodeAnalysis.MetadataReference}?)"/>
/// knows which tree to replace) and its <see cref="Text"/>.
/// </summary>
internal readonly record struct SourceFile(string Path, string Text);

/// <summary>
/// The outcome of one <see cref="GeneratorTestHarness.Run"/> call: everything a spec needs to
/// assert against, gathered in one place instead of re-derived per test.
/// </summary>
internal sealed record GeneratorRunResult(
    GeneratorDriver Driver,
    GeneratorDriverRunResult RunResult,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompileDiagnostics,
    ImmutableArray<Diagnostic> AllDiagnostics,
    IReadOnlyDictionary<string, string> GeneratedSources,
    IReadOnlyDictionary<string, TimeSpan> ElapsedByGenerator);

/// <summary>
/// Pairs the "before" and "after" <see cref="GeneratorRunResult"/> of one
/// <see cref="GeneratorTestHarness.RunIncremental(string,string,Microsoft.CodeAnalysis.MetadataReference[])"/>
/// call. <see cref="After"/>'s <see cref="GeneratorRunResult.Driver"/> carries the SAME cache
/// state <see cref="Before"/> built -- <see cref="After"/>.RunResult.Results[0].TrackedSteps
/// reports each named step's <see cref="IncrementalStepRunReason"/> relative to that cache, which
/// is what every incrementality assertion in this project keys off.
/// </summary>
internal sealed record IncrementalGeneratorRunResult(GeneratorRunResult Before, GeneratorRunResult After);

/// <summary>
/// Shared driver for every spec in this project that runs <see cref="AkkaSerializerGenerator"/>
/// over ad-hoc source text. Three things every one of those specs used to duplicate by hand:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Building the base <see cref="MetadataReference"/> set (every trusted platform assembly
/// plus the handful of explicit ones the generator's attributes and runtime types live in). This
/// is expensive (dozens of <see cref="MetadataReference.CreateFromFile(string)"/> calls) and was
/// previously repeated by EVERY test method across three spec files. Here it is built exactly
/// once per test process, lazily, behind <see cref="BaseReferences"/>.</item>
/// <item>Wiring a <see cref="CSharpGeneratorDriver"/> with
/// <c>trackIncrementalGeneratorSteps: true</c> so <see cref="GeneratorDriverRunResult.Results"/>
/// carries tracked-step data for every run, not just the specs that remembered to ask for it.</item>
/// <item>The two-compilation ("assembly A supplies types, assembly B runs the generator") and
/// two-generation ("run once, edit, run again on the same driver") shapes that recur across the
/// diagnostics, cross-assembly, and incrementality specs.</item>
/// </list>
/// </remarks>
internal static class GeneratorTestHarness
{
    private const string DefaultAssemblyName = "AkkaSerializationGeneratorHarness";
    private const string DefaultSourcePath = "Source.cs";

    private static readonly Lazy<ImmutableArray<MetadataReference>> LazyBaseReferences =
        new(BuildBaseReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The shared base reference set (trusted platform assemblies plus the generator's explicit
    /// runtime dependencies), built once per process on first use.
    /// </summary>
    public static ImmutableArray<MetadataReference> BaseReferences => LazyBaseReferences.Value;

    public static CSharpParseOptions ParseOptions { get; } =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);

    /// <summary>
    /// Runs <see cref="AkkaSerializerGenerator"/> once over a single source file.
    /// </summary>
    public static GeneratorRunResult Run(string source, params MetadataReference[] extraReferences)
    {
        return Run(new[] { new SourceFile(DefaultSourcePath, source) }, extraReferences);
    }

    /// <summary>
    /// Runs <see cref="AkkaSerializerGenerator"/> once over an arbitrary set of source files.
    /// </summary>
    public static GeneratorRunResult Run(IReadOnlyList<SourceFile> sources, params MetadataReference[] extraReferences)
    {
        var compilation = CreateCompilation(DefaultAssemblyName, sources, extraReferences);
        return RunCore(CreateDriver(), compilation);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> to an in-memory assembly and returns a
    /// <see cref="MetadataReference"/> to it -- the "assembly A supplies types, assembly B runs the
    /// generator" shape used by the cross-assembly baseline spec. The generator never runs over
    /// this compilation.
    /// </summary>
    public static MetadataReference CompileToReference(string source, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: assemblyName + ".cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        if (!errors.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' must compile with no errors -- it supplies types only, it does no generator work: " +
                string.Join("; ", errors.Select(e => e.ToString())));
        }

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' failed to emit: " + string.Join("; ", emitResult.Diagnostics.Select(d => d.ToString())));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>
    /// Runs the generator on <paramref name="before"/>, then replaces the single source file with
    /// <paramref name="after"/> and runs again on the SAME driver, so the second run's tracked
    /// steps reflect real incremental caching against the first.
    /// </summary>
    public static IncrementalGeneratorRunResult RunIncremental(string before, string after, params MetadataReference[] extraReferences)
    {
        return RunIncremental(
            new[] { new SourceFile(DefaultSourcePath, before) },
            new[] { new SourceFile(DefaultSourcePath, after) },
            extraReferences,
            extraReferences);
    }

    /// <summary>
    /// General multi-file / changing-references form of <see cref="RunIncremental(string,string,MetadataReference[])"/>:
    /// any file present in both <paramref name="beforeSources"/> and <paramref name="afterSources"/>
    /// (matched by <see cref="SourceFile.Path"/>) is replaced; a file present only in
    /// <paramref name="afterSources"/> is added for the second run. <paramref name="afterExtraReferences"/>,
    /// when given, REPLACES the reference list used for the second run (so a scenario can add,
    /// rather than only edit, a reference between runs).
    /// </summary>
    public static IncrementalGeneratorRunResult RunIncremental(
        IReadOnlyList<SourceFile> beforeSources,
        IReadOnlyList<SourceFile> afterSources,
        IReadOnlyList<MetadataReference>? beforeExtraReferences = null,
        IReadOnlyList<MetadataReference>? afterExtraReferences = null)
    {
        var beforeCompilation = CreateCompilation(DefaultAssemblyName, beforeSources, beforeExtraReferences ?? Array.Empty<MetadataReference>());
        var driver = CreateDriver();
        var beforeResult = RunCore(driver, beforeCompilation);

        var afterByPath = afterSources.ToDictionary(s => s.Path, s => s.Text, StringComparer.Ordinal);
        var editedCompilation = beforeCompilation;
        foreach (var tree in beforeCompilation.SyntaxTrees)
        {
            if (afterByPath.TryGetValue(tree.FilePath, out var newText))
                editedCompilation = editedCompilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(newText, ParseOptions, path: tree.FilePath));
        }

        var beforePaths = new HashSet<string>(beforeSources.Select(s => s.Path), StringComparer.Ordinal);
        foreach (var addedSource in afterSources.Where(s => !beforePaths.Contains(s.Path)))
            editedCompilation = editedCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(addedSource.Text, ParseOptions, path: addedSource.Path));

        if (afterExtraReferences != null)
        {
            editedCompilation = editedCompilation
                .RemoveReferences(editedCompilation.References.Except(BaseReferences))
                .AddReferences(afterExtraReferences);
        }

        // Re-run on beforeResult.Driver (not a fresh driver): that is what lets the second run's
        // tracked steps report Cached/Unchanged/Modified relative to the first run's cache.
        var afterResult = RunCore(beforeResult.Driver, editedCompilation);
        return new IncrementalGeneratorRunResult(beforeResult, afterResult);
    }

    private static GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(
            ImmutableArray.Create(new AkkaSerializerGenerator().AsSourceGenerator()),
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private static GeneratorRunResult RunCore(GeneratorDriver driver, CSharpCompilation compilation)
    {
        var ranDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = ranDriver.GetRunResult();

        var generatedSources = runResult.GeneratedTrees
            .ToImmutableDictionary(tree => System.IO.Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);

        var compileDiagnostics = outputCompilation.GetDiagnostics();

        var elapsedByGenerator = ranDriver.GetTimingInfo().GeneratorTimes
            .ToImmutableDictionary(timing => timing.Generator.GetType().Name, timing => timing.ElapsedTime);

        return new GeneratorRunResult(
            ranDriver,
            runResult,
            outputCompilation,
            generatorDiagnostics,
            compileDiagnostics,
            generatorDiagnostics.AddRange(compileDiagnostics),
            generatedSources,
            elapsedByGenerator);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, IReadOnlyList<SourceFile> sources, IReadOnlyList<MetadataReference> extraReferences)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.Text, ParseOptions, path: s.Path));
        return CSharpCompilation.Create(
            assemblyName,
            trees,
            BaseReferences.Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ImmutableArray<MetadataReference> BuildBaseReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(System.IO.Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(AkkaSerializerAttribute<>).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(global::MessagePack.MessagePackWriter).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies
            .Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First())
            .ToImmutableArray();
    }
}
