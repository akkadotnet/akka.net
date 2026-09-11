//-----------------------------------------------------------------------
// <copyright file="GeneratorResolvedSerializerSnapshotSpec.cs" company="Akka.NET Project">
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
using System.Threading.Tasks;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VerifyXunit;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Snapshot gate for the S3 architecture pass's per-serializer resolve stage
/// (<see cref="AkkaSerializerGenerator.ResolveSerializer"/>) -- the counterpart of
/// <see cref="GeneratorMessageModelSnapshotSpec"/> (which snapshots extraction) and
/// <see cref="GeneratorGoldenOutputSpec"/> (which snapshots emitted text). Extracts every real
/// <c>[AkkaSerializer]</c> and <c>[AkkaSerializable]</c> declaration in the exact same golden corpus
/// (reused from <see cref="GeneratorGoldenOutputSpec.GoldenSource"/>/<see cref="GeneratorGoldenOutputSpec.GlobalNamespaceSource"/>,
/// never a parallel copy) through the SAME test-only entry points
/// (<see cref="AkkaSerializerGenerator.ExtractSerializerForTests"/>/<see cref="AkkaSerializerGenerator.ParseMessageForTests"/>)
/// that wrap the pipeline's real, UNMODIFIED extraction routines, resolves each serializer with
/// <see cref="AkkaSerializerGenerator.ResolveSerializerForTests"/>, and snapshots a stable,
/// human-readable rendering of the RESOLVED model: gate result, top-level closed set, reachable set,
/// resolved closed-generic registrations, used formatters, union plan, and validation diagnostics.
/// This is a rendering of the MODEL, not the emitted code -- <see cref="GeneratorGoldenOutputSpec"/>
/// already gates emitted text separately, and byte-identical output is a hard requirement this spec
/// does not touch.
/// </summary>
public sealed class GeneratorResolvedSerializerSnapshotSpec
{
    private static readonly string[] SerializerNames = { "GoldenSerializer", "MiniSerializer", "RootSerializer" };

    [Fact(DisplayName = "Resolved serializer models for the golden corpus should match their committed snapshot")]
    public Task Resolved_serializers_should_match_committed_snapshot()
    {
        var result = GeneratorTestHarness.Run(new[]
        {
            new SourceFile("GoldenSample.cs", GeneratorGoldenOutputSpec.GoldenSource),
            new SourceFile("GlobalSample.cs", GeneratorGoldenOutputSpec.GlobalNamespaceSource)
        });

        var compilation = result.OutputCompilation;
        var serializableAttributeType = compilation.GetTypeByMetadataName(typeof(AkkaSerializableAttribute).FullName!)
            ?? throw new InvalidOperationException($"Could not resolve {typeof(AkkaSerializableAttribute).FullName} in the harness compilation.");

        var messageSymbols = new List<INamedTypeSymbol>();
        var serializerSymbolsByName = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var filePath in new[] { "GoldenSample.cs", "GlobalSample.cs" })
        {
            var tree = compilation.SyntaxTrees.Single(t => t.FilePath == filePath);
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
                    continue;

                if (symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializableAttributeType)))
                    messageSymbols.Add(symbol);

                if (SerializerNames.Contains(symbol.Name, StringComparer.Ordinal))
                    serializerSymbolsByName[symbol.Name] = symbol;
            }
        }

        var messages = messageSymbols
            .Select(symbol => AkkaSerializerGenerator.ParseMessageForTests(symbol, compilation))
            .Where(message => message != null)
            .Cast<AkkaSerializerGenerator.MessageInfo>()
            .ToImmutableArray();

        var document = string.Join(
            Environment.NewLine + new string('=', 80) + Environment.NewLine,
            SerializerNames
                .Select(name => serializerSymbolsByName[name])
                .Select(symbol =>
                {
                    var serializer = AkkaSerializerGenerator.ExtractSerializerForTests(symbol, compilation)
                        ?? throw new InvalidOperationException($"'{symbol.Name}' was not recognized as [AkkaSerializer].");
                    var resolved = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, messages);
                    return RenderResolvedSerializer(resolved);
                }));

        return Verifier.Verify(document)
            .UseDirectory("MessageModelSnapshots")
            .UseFileName("GoldenCorpusResolvedSerializers");
    }

    private static string RenderResolvedSerializer(AkkaSerializerGenerator.ResolvedSerializer resolved)
    {
        var sb = new StringBuilder();
        var serializer = resolved.Serializer;

        sb.AppendLine($"Serializer: {serializer.ClassName} (ns={serializer.Namespace}, name={serializer.Name}, id={serializer.SerializerId})");
        sb.AppendLine($"Protocol: {(serializer.ProtocolTypeFullName.Length == 0 ? "(none)" : serializer.ProtocolTypeFullName)}");
        sb.AppendLine($"IsEmittable: {resolved.IsEmittable}");

        if (!resolved.GateDiagnostics.IsDefaultOrEmpty)
        {
            sb.AppendLine("GateDiagnostics:");
            foreach (var diagnostic in resolved.GateDiagnostics)
                sb.AppendLine($"  {RenderDiagnostic(diagnostic)}");
        }

        if (!resolved.IsEmittable)
            return sb.ToString();

        sb.AppendLine("TopLevelMessages (closed set, declaration order):");
        if (resolved.TopLevelMessages.Members.IsDefaultOrEmpty)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var member in resolved.TopLevelMessages.Members)
                sb.AppendLine($"  {member.TypeFullName} (manifest={member.Manifest}, method={member.MethodName})");
        }

        sb.AppendLine($"ReachableMessages ({resolved.ReachableMessages.Length}, reachability-walk order):");
        foreach (var message in resolved.ReachableMessages)
            sb.AppendLine($"  {message.FullyQualifiedName}");

        sb.AppendLine("ResolvedMessagesByType keys (sorted):");
        foreach (var key in resolved.ResolvedMessagesByType.Keys.OrderBy(k => k, StringComparer.Ordinal))
            sb.AppendLine($"  {key}");

        if (!resolved.ResolvedClosedGenericRegistrations.IsDefaultOrEmpty)
        {
            sb.AppendLine("ResolvedClosedGenericRegistrations:");
            foreach (var registration in resolved.ResolvedClosedGenericRegistrations)
                sb.AppendLine($"  {registration.TargetDisplayName} -> {(registration.Message == null ? "(invalid)" : registration.Message.FullyQualifiedName)}");
        }

        if (!resolved.UsedFormatters.IsDefaultOrEmpty)
        {
            sb.AppendLine("UsedFormatters:");
            foreach (var formatter in resolved.UsedFormatters)
                sb.AppendLine($"  {formatter.TargetTypeFullName} -> {formatter.FormatterTypeFullName} (ctorKind={formatter.CtorKind})");
        }

        if (!resolved.UnionPlan.Helpers.IsDefaultOrEmpty)
        {
            sb.AppendLine("UnionPlan:");
            foreach (var helper in resolved.UnionPlan.Helpers)
            {
                sb.AppendLine($"  {helper.HelperName} (fieldType={helper.FieldTypeFullName}, signature={helper.Signature}):");
                foreach (var member in helper.Members.Members)
                    sb.AppendLine($"    {member.TypeFullName} (manifest={member.Manifest}, method={member.MethodName})");
            }
        }

        if (!resolved.ValidationDiagnostics.IsDefaultOrEmpty)
        {
            sb.AppendLine("ValidationDiagnostics:");
            foreach (var diagnostic in resolved.ValidationDiagnostics)
                sb.AppendLine($"  {RenderDiagnostic(diagnostic)}");
        }

        return sb.ToString();
    }

    private static string RenderDiagnostic(AkkaSerializerGenerator.DiagnosticSpec diagnostic)
    {
        return $"{diagnostic.Key}({string.Join(", ", diagnostic.MessageArgs)})";
    }
}
