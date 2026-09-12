//-----------------------------------------------------------------------
// <copyright file="GeneratorMessageModelSnapshotSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
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
/// Snapshot gate for the generator's EXTRACTION layer -- the counterpart of
/// <see cref="GeneratorGoldenOutputSpec"/>, which gates the EMISSION layer. Parses every
/// <c>[AkkaSerializable]</c>-annotated type declared in the exact same golden corpus (reused from
/// <see cref="GeneratorGoldenOutputSpec.GoldenSource"/>/<see cref="GeneratorGoldenOutputSpec.GlobalNamespaceSource"/>,
/// never a parallel copy) through <see cref="AkkaSerializerGenerator.ParseMessageForTests"/> -- the
/// test-only entry point that wraps the pipeline's real, UNMODIFIED extraction routine -- and
/// snapshots a stable, human-readable rendering of every resulting model: manifest, fields (index,
/// kind, nullability), union members, and construction plan. A mismatch here means extraction
/// itself changed shape; it says nothing about emitted text, which
/// <see cref="GeneratorGoldenOutputSpec"/> covers separately.
/// </summary>
/// <remarks>
/// <see cref="AkkaSerializerGenerator.ParseMessageForTests"/> extracts one message IN ISOLATION --
/// it never sees a serializer's <c>[AkkaSerializerFormatter&lt;TTarget, TFormatter&gt;]</c>
/// registrations, which are resolved later, per-serializer, in the emission stage. A field whose
/// real pipeline behavior depends on such a registration (for example <c>NestedMessage.Foreign</c>,
/// which <c>GoldenSerializer</c> resolves via <c>ForeignFormatter</c>) therefore snapshots here at
/// its PRE-formatter-resolution kind (<c>MissingSerializableDefinition</c>), not the
/// <c>Formatted</c> kind <see cref="GeneratorGoldenOutputSpec"/>'s emitted output actually uses.
/// That is real, correct behavior for this test-only entry point, not a bug.
/// </remarks>
public sealed class GeneratorMessageModelSnapshotSpec
{
    [Fact(DisplayName = "Parsed message models for the golden corpus should match their committed snapshot")]
    public Task Message_models_should_match_committed_snapshot()
    {
        var result = GeneratorTestHarness.Run(new[]
        {
            new SourceFile("GoldenSample.cs", GeneratorGoldenOutputSpec.GoldenSource),
            new SourceFile("GlobalSample.cs", GeneratorGoldenOutputSpec.GlobalNamespaceSource)
        });

        var compilation = result.OutputCompilation;
        var attributeType = compilation.GetTypeByMetadataName(typeof(AkkaSerializableAttribute).FullName!)
            ?? throw new InvalidOperationException($"Could not resolve {typeof(AkkaSerializableAttribute).FullName} in the harness compilation.");

        var symbols = new List<INamedTypeSymbol>();
        foreach (var filePath in new[] { "GoldenSample.cs", "GlobalSample.cs" })
        {
            var tree = compilation.SyntaxTrees.Single(t => t.FilePath == filePath);
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
                    continue;

                if (symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType)))
                    symbols.Add(symbol);
            }
        }

        var document = string.Join(
            Environment.NewLine + new string('=', 80) + Environment.NewLine,
            symbols
                .OrderBy(s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .Select(symbol => RenderMessage(symbol, AkkaSerializerGenerator.ParseMessageForTests(symbol, compilation))));

        return Verifier.Verify(document)
            .UseDirectory("MessageModelSnapshots")
            .UseFileName("GoldenCorpusMessageModels");
    }

    private static string RenderMessage(INamedTypeSymbol symbol, AkkaSerializerGenerator.MessageInfo? message)
    {
        var sb = new StringBuilder();

        if (message == null)
        {
            sb.AppendLine($"Type: {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
            sb.AppendLine("ParseMessageForTests: null (not recognized as [AkkaSerializable])");
            return sb.ToString();
        }

        sb.AppendLine($"Type: {message.FullyQualifiedName}");
        sb.AppendLine($"SimpleName: {message.SimpleName}");
        sb.AppendLine($"Manifest: {(string.IsNullOrEmpty(message.Manifest) ? "(none)" : message.Manifest)}");
        sb.AppendLine($"AllowEmpty: {message.AllowEmpty}");
        sb.AppendLine($"IsGenericDefinition: {message.IsGenericDefinition}");
        if (message.IsGenericDefinition)
            sb.AppendLine($"DefinitionFullName: {message.DefinitionFullName}");
        sb.AppendLine($"Protocols: {(message.Protocols.IsDefaultOrEmpty ? "(none)" : string.Join(", ", message.Protocols))}");

        sb.AppendLine("Fields:");
        if (message.Fields.IsDefaultOrEmpty)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var field in message.Fields.OrderBy(f => f.Index))
            {
                var flags = new List<string> { $"nullable: {field.IsNullable}" };
                if (field.Mapping.IsValueType)
                    flags.Add("valueType");
                if (field.UnionDeclaredOnObjectField)
                    flags.Add("unionDeclaredOnObjectField");

                sb.AppendLine($"  [{field.Index}] {field.Name}: kind={field.Mapping.Kind}, type={field.TypeFullName} ({string.Join(", ", flags)})");

                if (!field.UnionMembers.IsDefaultOrEmpty)
                {
                    foreach (var member in field.UnionMembers.OrderBy(m => m.TypeFullName, StringComparer.Ordinal))
                    {
                        sb.AppendLine(
                            $"      union member: {member.TypeFullName} (valueType={member.IsValueType}, assignable={member.IsAssignable}, " +
                            $"supported={member.IsSupported}, sealed={member.IsSealed}, abstract={member.IsAbstract})");
                    }
                }

                if (field.Formatter != null)
                    sb.AppendLine($"      formatter: {field.Formatter.FormatterTypeFullName} (ctorKind={field.Formatter.CtorKind})");
            }
        }

        if (!message.InvalidFields.IsDefaultOrEmpty)
        {
            sb.AppendLine("InvalidFields:");
            foreach (var invalid in message.InvalidFields)
                sb.AppendLine($"  {invalid.PropertyName}: {invalid.Reason}");
        }

        var plan = message.ConstructionPlan;
        sb.AppendLine($"ConstructionPlan: valid={plan.Errors.IsDefaultOrEmpty}");
        if (!plan.Arguments.IsDefaultOrEmpty)
        {
            sb.AppendLine("  Arguments:");
            foreach (var argument in plan.Arguments)
                sb.AppendLine($"    {argument.ParameterName} <- {argument.FieldName}");
        }

        if (!plan.InitializerFieldNames.IsDefaultOrEmpty)
            sb.AppendLine($"  InitializerFields: {string.Join(", ", plan.InitializerFieldNames)}");

        if (!plan.UncoveredDefaultedParameters.IsDefaultOrEmpty)
            sb.AppendLine($"  UncoveredDefaultedParameters: {string.Join(", ", plan.UncoveredDefaultedParameters)}");

        if (!plan.Errors.IsDefaultOrEmpty)
            sb.AppendLine($"  Errors: {string.Join(" | ", plan.Errors)}");

        return sb.ToString();
    }
}
