//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Validation.cs" company="Akka.NET Project">
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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.V2.Generators;

/// <summary>
/// The result of <see cref="AkkaSerializerGenerator.EvaluateGate"/>: whether a serializer's own
/// declaration is usable as a codegen target at all (<see cref="IsEmittable"/>), plus every
/// diagnostic that check produced, as <see cref="AkkaSerializerGenerator.DiagnosticSpec"/> values
/// rather than live <see cref="Diagnostic"/>s. <see cref="Diagnostics"/> is empty whenever the gate
/// fails for a reason that carries no diagnostic of its own here (a duplicate serializer id or
/// protocol binding -- those are reported once, in bulk, by the caller, before the gate ever runs
/// per-serializer). Not a cached pipeline model: it is built and consumed entirely within one
/// <c>RegisterSourceOutput</c> callback and never crosses an incremental boundary, so -- like
/// <c>Local</c>/<c>ValueExpr</c>/<c>TypeName</c> in CodeWriter.cs -- it lives at namespace scope
/// instead of nested under <see cref="AkkaSerializerGenerator"/>.
/// </summary>
internal readonly struct SerializerGate
{
    public SerializerGate(bool isEmittable, ImmutableArray<AkkaSerializerGenerator.DiagnosticSpec> diagnostics)
    {
        IsEmittable = isEmittable;
        Diagnostics = diagnostics;
    }

    public bool IsEmittable { get; }
    public ImmutableArray<AkkaSerializerGenerator.DiagnosticSpec> Diagnostics { get; }
}

public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// Diagnostics-only output for AKKASG029 (see the comment in <see cref="Initialize"/>). To
    /// preserve the old terminal stage's semantics, a serializer only reaches the coverage scan
    /// when it is emittable -- consuming <see cref="ResolvedSerializer.IsEmittable"/> directly
    /// instead of re-evaluating <see cref="EvaluateGate"/> from the raw collected arrays, now that
    /// the gate has already been decided once, per serializer, by <see cref="ResolveSerializer"/>.
    /// <see cref="ResolvedSerializer.GateDiagnostics"/> are NOT reported here (they are reported
    /// once, by <see cref="ReportResolvedSerializerDiagnostics"/>); reporting them again here would
    /// duplicate every pre-coverage diagnostic. As of S5 this combines the cached
    /// <see cref="CompilationFacts"/> instead of the live <see cref="Compilation"/> -- see
    /// <see cref="ValidateProtocolCoverage"/>. As of S6 this also combines the merged
    /// <see cref="LocationBag"/>, so each AKKASG029 diagnostic resolves to a real
    /// <see cref="Location"/> -- see <see cref="ValidateProtocolCoverage"/>'s own doc comment for
    /// why it reports at the serializer's own attribute rather than the unmarked implementor.
    /// </summary>
    private static void ReportProtocolCoverage(SourceProductionContext context, ResolvedSerializer resolved, CompilationFacts facts, LocationBag locations)
    {
        if (!resolved.IsEmittable)
            return;

        var protocolCoverageDiagnostics = ValidateProtocolCoverage(resolved.Serializer, facts);
        foreach (var diagnostic in protocolCoverageDiagnostics)
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(diagnostic, locations));
    }

    private static ImmutableDictionary<int, string> ComputeDuplicateSerializerIds(ImmutableArray<SerializerInfo?> serializers)
    {
        return serializers
            .Where(s => s != null)
            .Cast<SerializerInfo>()
            .Where(s => s.SerializerId > 0)
            .GroupBy(s => s.SerializerId)
            .Where(group => group.Count() > 1)
            .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)));
    }

    private static ImmutableDictionary<string, string> ComputeDuplicateProtocolBindings(ImmutableArray<SerializerInfo?> serializers)
    {
        return serializers
            .Where(s => s != null)
            .Cast<SerializerInfo>()
            .Where(s => !string.IsNullOrEmpty(s.ProtocolTypeFullName))
            .GroupBy(s => s.ProtocolTypeFullName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)), StringComparer.Ordinal);
    }

    /// <summary>
    /// The per-serializer gate every codegen target must clear before its message table is even
    /// looked at: is the declaration itself usable (name, id, uniqueness, shape, protocol type,
    /// formatters, closed-generic registrations, generic-definition coverage)? Called exactly once
    /// per serializer, by <see cref="ResolveSerializer"/>, whose <see cref="ResolvedSerializer"/>
    /// result then feeds BOTH <see cref="EmitResolvedSerializer"/> (which reports
    /// <see cref="ResolvedSerializer.GateDiagnostics"/>) and <see cref="ReportProtocolCoverage"/>
    /// (which only reads <see cref="ResolvedSerializer.IsEmittable"/>) -- so the two outputs can
    /// never disagree about which serializers are gated out, without either recomputing the gate
    /// independently. Mirrors the old single-output stage's check order exactly: each step below
    /// short-circuits the ones after it, but a single step (formatters, closed-generic
    /// registrations, generic definitions) can itself append more than one diagnostic before
    /// failing. <paramref name="duplicateSerializerIds"/> and <paramref name="duplicateProtocolBindings"/>
    /// are precomputed by the caller (every caller computes its own copy from the same input array)
    /// so a serializer that is part of either duplicate group is gated out WITHOUT a diagnostic here
    /// -- that diagnostic is reported once, in bulk, by <see cref="ReportCrossSerializerDiagnostics"/>.
    /// </summary>
    internal static SerializerGate EvaluateGate(
        SerializerInfo serializer,
        ImmutableDictionary<int, string> duplicateSerializerIds,
        ImmutableDictionary<string, string> duplicateProtocolBindings,
        ImmutableArray<MessageInfo> genericDefinitions)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();

        // Every diagnostic in this method is serializer-level: reported at the serializer's own
        // [AkkaSerializer<TProtocol>] attribute, per the location rule in AkkaSerializerGenerator.Locations.cs.
        var serializerAt = new LocationKey(serializer.Key, string.Empty);

        if (string.IsNullOrWhiteSpace(serializer.Name))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerName, serializerAt, serializer.ClassName));
            return new SerializerGate(false, diagnostics.ToImmutable());
        }

        if (serializer.SerializerId <= 0)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerId, serializerAt, serializer.ClassName, serializer.SerializerId.ToString()));
            return new SerializerGate(false, diagnostics.ToImmutable());
        }

        if (duplicateSerializerIds.ContainsKey(serializer.SerializerId))
            return new SerializerGate(false, ImmutableArray<DiagnosticSpec>.Empty);

        if (duplicateProtocolBindings.ContainsKey(serializer.ProtocolTypeFullName))
            return new SerializerGate(false, ImmutableArray<DiagnosticSpec>.Empty);

        if (!ValidateSerializerShape(serializer, diagnostics))
            return new SerializerGate(false, diagnostics.ToImmutable());

        if (!ValidateProtocolType(serializer, diagnostics))
            return new SerializerGate(false, diagnostics.ToImmutable());

        if (!ValidateFormatters(serializer, diagnostics))
            return new SerializerGate(false, diagnostics.ToImmutable());

        if (!ValidateClosedGenericRegistrations(serializer, diagnostics))
            return new SerializerGate(false, diagnostics.ToImmutable());

        if (!ValidateGenericDefinitions(serializer, genericDefinitions, diagnostics))
            return new SerializerGate(false, diagnostics.ToImmutable());

        return new SerializerGate(true, diagnostics.ToImmutable());
    }

    /// <summary>
    /// Runs every model-only validation check for one serializer against its message table -- the
    /// SECOND half of the per-serializer pipeline, after <see cref="EvaluateGate"/> has already
    /// passed (there is no reason to validate a message table for a serializer whose own declaration
    /// is broken). Mirrors the call sequence the old single-output emission stage ran inline: resolve
    /// formatters, pick out this serializer's top-level messages, walk to everything reachable from
    /// them, validate the result, then check closed-generic registrations against it. Takes only
    /// symbol-free models -- no <see cref="SourceProductionContext"/>, <see cref="Compilation"/>, or
    /// driver of any kind -- so a test can call it directly on hand-built models (see
    /// GeneratorValidatorSpec.cs) with no generator run at all.
    /// </summary>
    internal static ImmutableArray<DiagnosticSpec> Validate(SerializerInfo serializer, ImmutableArray<MessageInfo> messages, MetadataSchemaTable? metadataSchemas = null)
    {
        var schemas = metadataSchemas ?? MetadataSchemaTable.Empty;
        var resolved = ResolveSerializerMessages(serializer, messages, schemas);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        ValidateResolved(serializer, resolved, schemas, diagnostics);
        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Test-only entry point for <see cref="ResolveSerializer"/>, with no duplicate-id/duplicate-
    /// protocol-binding group and no generic-definition list to hand-build: a scenario driving this
    /// directly (see GeneratorResolveSpec.cs) has exactly the one serializer under test in scope, so
    /// those three cross-serializer inputs are trivially empty/derived from <paramref name="messages"/>
    /// itself -- mirroring how <see cref="Validate"/> is <see cref="ResolveSerializer"/>'s
    /// single-serializer counterpart for validation alone.
    /// </summary>
    internal static ResolvedSerializer ResolveSerializerForTests(SerializerInfo serializer, ImmutableArray<MessageInfo> messages)
    {
        return ResolveSerializer(
            serializer,
            messages,
            ImmutableDictionary<int, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ComputeGenericDefinitions(messages),
            MetadataSchemaTable.Empty);
    }

    /// <summary>
    /// Shared by <see cref="Validate"/> and <see cref="ResolveSerializer"/> so both run the exact same
    /// validation over the exact same <see cref="ResolvedSerializerMessages"/> -- <see cref="ResolveSerializer"/>
    /// calls this with a table it also reuses for code generation; <see cref="Validate"/> computes one
    /// just for the call. <see cref="ValidateClosedGenericProtocolCoverage"/> runs only when
    /// <see cref="ValidateMessages"/> found no error, mirroring the old stage's short-circuit exactly
    /// (a message-level error already means nothing will be emitted, so the AKKASG034 coverage scan
    /// over a table already known to be broken is skipped, exactly as before).
    /// </summary>
    private static void ValidateResolved(SerializerInfo serializer, ResolvedSerializerMessages resolved, MetadataSchemaTable metadataSchemas, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (!ValidateMessages(serializer, resolved.TopLevelMessages, resolved.ReachableMessages, resolved.ResolvedMessagesByType, metadataSchemas.AccessibilityFailuresByType, diagnostics))
            return;

        ValidateClosedGenericProtocolCoverage(serializer, resolved.ReachableMessages, diagnostics);
    }

    /// <summary>
    /// Fires AKKASG032 for each way the [AkkaSerializer] class declaration itself is unusable as
    /// a codegen target: not partial, not derived from the AkkaSerializer base class, or generic.
    /// Today each of these produces a wall of raw CS errors (CS0260/CS0759/CS0115/CS0264) pointing
    /// at the GENERATED file instead of the user's declaration; this replaces that with one direct
    /// diagnostic per violated rule, still on the user's class.
    /// </summary>
    private static bool ValidateSerializerShape(SerializerInfo serializer, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var isValid = true;
        var at = new LocationKey(serializer.Key, string.Empty);

        if (!serializer.IsPartial)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, at, serializer.ClassName,
                "must be declared 'partial': the generator emits a second declaration of this class"));
            isValid = false;
        }

        if (!serializer.DerivesFromAkkaSerializerBase)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, at, serializer.ClassName,
                "must derive from Akka.Serialization.V2.AkkaSerializer: the generated members (Identifier, Manifest, Serialize, Deserialize, SizeHint) are declared as overrides of that base"));
            isValid = false;
        }

        if (serializer.IsGeneric)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, at, serializer.ClassName,
                "cannot be a generic type: the generator emits one concrete, closed partial class per [AkkaSerializer] declaration"));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Fires AKKASG033 when <c>TProtocol</c> in <c>[AkkaSerializer&lt;TProtocol&gt;]</c> is not an
    /// interface. Top-level dispatch matches a message via <c>message.Protocols</c>, which is
    /// populated from <c>INamedTypeSymbol.AllInterfaces</c> (see <see cref="ExtractMessageCore"/>)
    /// -- a class or struct can never appear there, so a non-interface protocol type silently
    /// produces a serializer whose Manifest/Serialize/Deserialize switches have no cases at all.
    /// An empty <see cref="SerializerInfo.ProtocolTypeFullName"/> means the attribute's type
    /// argument was not a named type at all (the extraction stored no name for it) and is exempt,
    /// exactly as the former null-symbol check was.
    /// </summary>
    private static bool ValidateProtocolType(SerializerInfo serializer, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (serializer.ProtocolTypeFullName.Length == 0 || serializer.ProtocolTypeIsInterface)
            return true;

        diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ProtocolTypeMustBeInterface, new LocationKey(serializer.Key, string.Empty),
            serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
        return false;
    }

    /// <summary>
    /// Fires AKKASG029 when a named type declared IN THIS COMPILATION implements the serializer's
    /// protocol interface but is not [AkkaSerializable]. Exempt: interfaces and abstract classes
    /// (never concrete runtime message types -- their concrete subtypes are checked individually),
    /// and [AkkaSerializable]-marked open generic definitions (governed entirely by AKKASG022's
    /// registration machinery). An unmarked OPEN GENERIC definition that implements the protocol is
    /// still an error here: with no [AkkaSerializable] on the definition, none of its closed
    /// constructions could ever be registered with [AkkaSerializable&lt;T&gt;] in the first place.
    /// A type this flags is invisible to the generated Manifest/Serialize/Deserialize switches
    /// today and only fails at runtime, the first time it is sent.
    /// As of S5, the whole-compilation scan that used to run HERE, per serializer, now runs exactly
    /// once per compilation change for every serializer's protocol at once, inside
    /// <see cref="ComputeCompilationFacts"/> (see <see cref="ComputeLocalUnmarkedImplementorsByProtocol"/>).
    /// This method is now a pure, symbol-free, Compilation-free function of the resolved serializer
    /// plus that precomputed <see cref="CompilationFacts"/> -- it only formats the diagnostic for
    /// each already-identified implementor. Diagnostic id, text, and trigger conditions are
    /// unchanged from before the split. Reports at the SERIALIZER's own attribute
    /// (<c>LocationKey(serializer.Key, "")</c>), never at the unmarked implementor: that implementor
    /// is, by definition, not an attributed type this generator's model knows anything about, and
    /// <see cref="CompilationFacts"/> deliberately carries no location of its own (it must stay
    /// whitespace-insensitive to every candidate type's own declaration -- see that type's doc
    /// comment) so there is no local site on the implementor to point at in the first place.
    /// </summary>
    internal static ImmutableArray<DiagnosticSpec> ValidateProtocolCoverage(SerializerInfo serializer, CompilationFacts facts)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();

        if (serializer.ProtocolTypeFullName.Length == 0)
            return diagnostics.ToImmutable();

        if (!facts.LocalUnmarkedImplementorsByProtocol.TryGetValue(serializer.ProtocolTypeKey, out var implementors))
            return diagnostics.ToImmutable();

        var at = new LocationKey(serializer.Key, string.Empty);
        foreach (var implementor in implementors)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ProtocolMessageNotSerializable, at,
                ToDisplayName(implementor.DisplayName ?? string.Empty), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> implements the protocol identified by <paramref name="protocolKey"/>.
    /// Builds a comparison-only <see cref="TypeKey"/> (<c>includeDisplayName: false</c>) per
    /// candidate interface and compares it against <paramref name="protocolKey"/> by METADATA identity
    /// -- replacing the former per-interface <c>ToDisplayString</c> call and ordinal string compare.
    /// Equality never reads either side's <see cref="TypeKey.DisplayName"/>, so this scan never
    /// formats a display string for an interface it does not need one for.
    /// <see cref="CouldMatchByMetadataName"/> filters out every interface that cannot possibly match
    /// -- for example a record's compiler-synthesized <c>IEquatable&lt;T&gt;</c>, or any OTHER
    /// serializer's protocol -- with a single, allocation-free string compare, before paying for a
    /// full <see cref="TypeKey.FromSymbol"/> (which recurses into type arguments for a generic
    /// interface). As of S5 this runs once per candidate type per PROTOCOL KEY, inside
    /// <see cref="ComputeLocalUnmarkedImplementorsByProtocol"/> -- once for the whole compilation,
    /// not once per serializer as before -- and its result is never itself cached across candidates
    /// (only the caller's aggregated <see cref="CompilationFacts"/> output is), so skipping the
    /// expensive path for the overwhelming majority of interfaces that were never going to match is
    /// still the whole saving.
    /// </summary>
    private static bool ImplementsProtocol(INamedTypeSymbol candidate, TypeKey protocolKey)
    {
        foreach (var implemented in candidate.AllInterfaces)
        {
            if (!CouldMatchByMetadataName(protocolKey.MetadataName, implemented.MetadataName))
                continue;

            if (TypeKey.FromSymbol(implemented, includeDisplayName: false).Equals(protocolKey))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cheap, allocation-free pre-filter for <see cref="ImplementsProtocol"/>: whether
    /// <paramref name="candidateMetadataName"/> (an interface's own simple metadata name, arity
    /// suffix included) could possibly be the FINAL segment of <paramref name="protocolMetadataName"/>
    /// (the protocol's full metadata name, as <see cref="TypeKey.MetadataName"/> always builds it --
    /// a namespace/containing-type prefix, then the type's own simple metadata name last). A
    /// necessary, not sufficient, condition: a "yes" still falls through to the authoritative full
    /// <see cref="TypeKey"/> comparison (two different namespaces can share a simple type name), but
    /// a "no" can never be a false negative, since a genuine match's OWN simple metadata name is
    /// always exactly this trailing segment.
    /// </summary>
    private static bool CouldMatchByMetadataName(string protocolMetadataName, string candidateMetadataName)
    {
        if (protocolMetadataName.Length < candidateMetadataName.Length)
            return false;

        if (!protocolMetadataName.EndsWith(candidateMetadataName, StringComparison.Ordinal))
            return false;

        var boundaryIndex = protocolMetadataName.Length - candidateMetadataName.Length - 1;
        return boundaryIndex < 0 || protocolMetadataName[boundaryIndex] is '.' or '+';
    }

    /// <summary>
    /// Every named type declared in <paramref name="compilation"/>'s OWN source (never a referenced
    /// assembly: <see cref="Compilation.Assembly"/> is the assembly being compiled), recursively
    /// including nested types. Used by <see cref="ComputeLocalUnmarkedImplementorsByProtocol"/>,
    /// transiently, inside the S5 compilation-facts stage -- never stored in a cached provider
    /// itself (its CALLER's output is what gets cached; see <see cref="CompilationFacts"/>).
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypes(Compilation compilation)
    {
        return GetSourceDeclaredTypes(compilation.Assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var nested in GetSourceDeclaredTypesIncludingSelf(type))
                yield return nested;
        }

        foreach (var nestedNamespace in ns.GetNamespaceMembers())
        {
            foreach (var type in GetSourceDeclaredTypes(nestedNamespace))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypesIncludingSelf(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var descendant in GetSourceDeclaredTypesIncludingSelf(nested))
                yield return descendant;
        }
    }

    private static bool ValidateFormatters(SerializerInfo serializer, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (serializer.Formatters.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var formatter in serializer.Formatters)
        {
            // Every formatter diagnostic reports at ITS OWN [AkkaSerializerFormatter<TTarget,
            // TFormatter>] attribute application, not the serializer's main attribute -- see
            // FormatterLocationMember in AkkaSerializerGenerator.Locations.cs.
            var at = new LocationKey(serializer.Key, FormatterLocationMember(formatter.TargetTypeFullName));

            if (!formatter.IsTargetSupported)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FormatterTargetNotSupported, at, ToDisplayName(formatter.TargetTypeFullName), serializer.ClassName));
                isValid = false;
                continue;
            }

            if (formatter.IsAbstract)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidFormatterType, at, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName, ToDisplayName(formatter.TargetTypeFullName)));
                isValid = false;
                continue;
            }

            if (formatter.CtorKind == FormatterCtorKind.None)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FormatterConstructorNotUsable, at, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName));
                isValid = false;
            }
        }

        foreach (var duplicate in serializer.Formatters
                     .Where(formatter => formatter.IsTargetSupported)
                     .GroupBy(formatter => formatter.TargetTypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var at = new LocationKey(serializer.Key, FormatterLocationMember(duplicate.Key));
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateFormatterRegistration, at, serializer.ClassName, ToDisplayName(duplicate.Key)));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Fires AKKASG020 when a registration's target has no matching entry in
    /// <see cref="SerializerInfo.ClosedGenericSchemas"/> -- the light spec (see
    /// <see cref="ClosedGenericRegistrationInfo"/>) carries only the target's own key, so a target
    /// this serializer never managed to extract a schema for (not a type, non-generic, unbound, or
    /// its definition lacks <c>[AkkaSerializable]</c>) is exactly the registration with no matching
    /// key in <see cref="SerializerInfo.ClosedGenericSchemas"/>.
    /// </summary>
    private static bool ValidateClosedGenericRegistrations(SerializerInfo serializer, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
            return true;

        var schemaKeys = new HashSet<TypeKey>();
        foreach (var schema in serializer.ClosedGenericSchemas)
            schemaKeys.Add(schema.Key);

        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations.Where(registration => !schemaKeys.Contains(registration.Target)))
        {
            // Reports at THIS registration's own [AkkaSerializable<T>] attribute application --
            // Decision 16: "the registration attribute for a closed generic".
            var at = new LocationKey(serializer.Key, ClosedGenericLocationMember(registration.TargetDisplayName));
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidClosedGenericRegistration, at, ToDisplayName(registration.TargetDisplayName), serializer.ClassName));
            isValid = false;
        }

        foreach (var duplicate in serializer.ClosedGenericRegistrations
                     .Where(registration => schemaKeys.Contains(registration.Target))
                     .GroupBy(registration => registration.Target)
                     .Where(group => group.Count() > 1))
        {
            var targetDisplayName = duplicate.Key.DisplayName ?? string.Empty;
            var at = new LocationKey(serializer.Key, ClosedGenericLocationMember(targetDisplayName));
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateClosedGenericRegistration, at, serializer.ClassName, ToDisplayName(targetDisplayName)));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Fires AKKASG022 when a generic <c>[AkkaSerializable]</c> definition implements this
    /// serializer's protocol interface but no closed construction of it is registered: without a
    /// registration the type would silently never serialize, which is exactly the confusing
    /// broken-codegen failure mode this diagnostic replaces.
    /// </summary>
    private static bool ValidateGenericDefinitions(SerializerInfo serializer, ImmutableArray<MessageInfo> genericDefinitions, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (genericDefinitions.IsDefaultOrEmpty || serializer.ProtocolTypeFullName.Length == 0)
            return true;

        var isValid = true;
        foreach (var definition in genericDefinitions)
        {
            if (!definition.Protocols.Contains(serializer.ProtocolTypeFullName))
                continue;

            var hasRegistration = serializer.ClosedGenericSchemas.Any(schema =>
                string.Equals(schema.DefinitionFullName, definition.FullyQualifiedName, StringComparison.Ordinal));
            if (hasRegistration)
                continue;

            // Reports at the SERIALIZER's own attribute: the fix ("register each closed construction
            // with [AkkaSerializable<T>] ... on the serializer class") is an edit to the serializer,
            // not to the generic definition itself.
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.GenericSerializableRequiresRegistration, new LocationKey(serializer.Key, string.Empty),
                ToDisplayName(definition.FullyQualifiedName), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
            isValid = false;
        }

        return isValid;
    }

    private static ImmutableArray<MessageInfo> CollectReachableMessages(
        ImmutableArray<MessageInfo> topLevelMessages,
        ImmutableDictionary<TypeKey, MessageInfo> allMessagesByType)
    {
        var messages = ImmutableArray.CreateBuilder<MessageInfo>();
        var visited = new HashSet<TypeKey>();
        var pending = new Queue<MessageInfo>(topLevelMessages);

        while (pending.Count > 0)
        {
            var message = pending.Dequeue();
            if (!visited.Add(message.Key))
                continue;

            messages.Add(message);
            var referencedObjectTypes = new HashSet<TypeKey>();
            foreach (var field in message.Fields)
            {
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                    referencedObjectTypes.Add(objectMapping.Key);

                // Union members are reachable exactly like nested Object fields: each member needs
                // its Write/Read/SizeOf methods generated for the union dispatch to call into.
                foreach (var unionMember in field.UnionMembers)
                {
                    if (unionMember.IsSupported)
                        referencedObjectTypes.Add(unionMember.Key);
                }
            }

            foreach (var typeKey in referencedObjectTypes)
            {
                if (allMessagesByType.TryGetValue(typeKey, out var nestedMessage))
                    pending.Enqueue(nestedMessage);
            }
        }

        return messages.ToImmutable();
    }

    // Walks a mapping and its collection element/key/value mappings, yielding every Object mapping
    // found. A nested [AkkaSerializable] type used only inside a collection (a List<Reading>
    // element, say) is found this way too. Yields the full mapping, not just its name, so a caller
    // can read its flags; a caller that only needs names can project TypeFullName itself.
    private static IEnumerable<TypeMapping> EnumerateObjectMappings(TypeMapping mapping)
    {
        if (mapping.Kind == FieldKind.Object)
            yield return mapping;

        foreach (var argument in mapping.TypeArguments)
        {
            foreach (var nested in EnumerateObjectMappings(argument))
                yield return nested;
        }
    }

    private static bool ValidateMessages(
        SerializerInfo serializer,
        ImmutableArray<MessageInfo> topLevelMessages,
        ImmutableArray<MessageInfo> reachableMessages,
        ImmutableDictionary<TypeKey, MessageInfo> messagesByType,
        ImmutableDictionary<TypeKey, AccessibilityFailure> accessibilityFailuresByType,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            // A closed-generic SCHEMA can be top-level too (its construction implements the
            // protocol directly) -- MessageTypeLocationKey redirects to its own registration
            // attribute in that case, since the construction itself has no separate syntax.
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.MissingManifest, MessageTypeLocationKey(serializer, message), ToDisplayName(message.FullyQualifiedName)));
            isValid = false;
        }

        foreach (var duplicate in topLevelMessages
                     .Where(m => !string.IsNullOrWhiteSpace(m.Manifest))
                     .GroupBy(m => m.Manifest, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var typeNames = string.Join(", ", duplicate.Select(m => ToDisplayName(m.FullyQualifiedName)));

            // Spans multiple messages -- no single message owns this diagnostic, so it reports at the
            // serializer's own attribute instead.
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateManifest, new LocationKey(serializer.Key, string.Empty), serializer.ClassName, duplicate.Key, typeNames));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            // Type-level diagnostics on a closed-generic SCHEMA (MissingFields, NoMatchingConstructor,
            // ConstructorParameterNotCovered, DuplicateFieldIndex below) redirect to that
            // construction's own registration attribute -- see MessageTypeLocationKey's doc comment.
            var messageAt = MessageTypeLocationKey(serializer, message);

            if (message.Fields.Length == 0 && !message.AllowEmpty)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.MissingFields, messageAt, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
            {
                // Spans multiple fields of the same message -- reports at the message type itself.
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateFieldIndex, messageAt, ToDisplayName(message.FullyQualifiedName), duplicate.Key.ToString()));
                isValid = false;
            }

            // Structural [AkkaField] problems found during extraction (static property, or a
            // getter the generated Write path could not call): these properties never made it into
            // message.Fields, so they cannot double-report through any of the checks below.
            foreach (var invalidField in message.InvalidFields)
            {
                var at = new LocationKey(message.Key, invalidField.PropertyName);
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FieldPropertyNotAccessible, at, invalidField.PropertyName, ToDisplayName(message.FullyQualifiedName), invalidField.Reason));
                isValid = false;
            }

            // Read-side reconstruction: either no constructor could be selected, or the selected
            // constructor leaves [AkkaField] properties uncovered with no accessible setter to fall
            // back on -- both make deserialize impossible to generate. Not any one field's fault, so
            // reports at the message type itself.
            foreach (var error in message.ConstructionPlan.Errors)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.NoMatchingConstructor, messageAt, ToDisplayName(message.FullyQualifiedName), error));
                isValid = false;
            }

            // Advisory only: the selected constructor still works (its defaulted parameter is simply
            // never supplied), but the parameter's value silently reverts to its default on every
            // deserialize because no [AkkaField] property feeds it. The parameter need not even
            // correspond to an [AkkaField] property, so this reports at the message type itself too.
            foreach (var parameterName in message.ConstructionPlan.UncoveredDefaultedParameters)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ConstructorParameterNotCovered, messageAt, parameterName, ToDisplayName(message.FullyQualifiedName)));
            }

            // Error (AKKASG038): an object-typed property is always the envelope-payload boundary
            // (the static type alone carries that meaning); a field-level [AkkaUnion] on it can
            // never take effect, so this is contradictory author intent, not a harmless no-op.
            foreach (var field in message.Fields.Where(field => field.UnionDeclaredOnObjectField))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionDeclaredOnObjectField, new LocationKey(message.Key, field.Name), field.Name, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
            {
                var at = new LocationKey(message.Key, field.Name);
                diagnostics.Add(field.Mapping.SuggestsEnvelopeOrUnion
                    ? new DiagnosticSpec(DiagnosticKey.UnsupportedFieldTypePolymorphic, at, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName))
                    : new DiagnosticSpec(DiagnosticKey.UnsupportedFieldType, at, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.MissingSerializableDefinition))
            {
                ReportMissingNestedSchema(message, field.Name, field.TypeFullName, field.Mapping, serializer.ClassName, accessibilityFailuresByType, diagnostics);
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.UnsupportedEnumUnderlyingType))
            {
                var at = new LocationKey(message.Key, field.Name);
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnsupportedEnumUnderlyingType, at, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.Mapping.TypeFullName), ToDisplayName(field.Mapping.EnumUnderlyingTypeName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                if (!ValidateUnionField(message, field, messagesByType, accessibilityFailuresByType, serializer.ClassName, diagnostics))
                    isValid = false;
            }

            // An Object mapping that resolves to no known message would generate a call to a
            // nonexistent Write/Read/SizeOf method. ReportMissingNestedSchema tells apart three ways
            // that happens: a genuine unregistered closed generic construction (AKKASG023), a
            // referenced-assembly type this compilation cannot see or read a member of even though it
            // carries [AkkaSerializable] (AKKASG039), or a non-generic type with no [AkkaSerializable]
            // anywhere this generator can see (AKKASG007, cross-assembly wording when the type's
            // declaring assembly says so).
            foreach (var field in message.Fields)
            {
                var seenTypeKeys = new HashSet<TypeKey>();
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                {
                    if (!seenTypeKeys.Add(objectMapping.Key) || messagesByType.ContainsKey(objectMapping.Key))
                        continue;

                    ReportMissingNestedSchema(message, field.Name, objectMapping.TypeFullName, objectMapping, serializer.ClassName, accessibilityFailuresByType, diagnostics);
                    isValid = false;
                }
            }
        }

        // Flattening generic constructions into generated member names is collision-prone in
        // principle (mirrors System.Text.Json's DuplicateTypeName handling): detect and fail
        // instead of silently emitting duplicate members.
        foreach (var collision in reachableMessages
                     .GroupBy(GetMessageMethodName, StringComparer.Ordinal)
                     .Where(group => group.Select(m => m.FullyQualifiedName).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var typeNames = string.Join(", ", collision.Select(m => ToDisplayName(m.FullyQualifiedName)));

            // Spans multiple messages -- no single message owns this diagnostic, so it reports at the
            // serializer's own attribute instead.
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateGeneratedName, new LocationKey(serializer.Key, string.Empty), serializer.ClassName, collision.Key, typeNames));
            isValid = false;
        }

        return isValid;
    }

    // Decision table for a nested type this generator cannot serialize today: a closed generic
    // construction reports AKKASG023 (register it with [AkkaSerializable<T>]); a type declared in a
    // referenced assembly that this compilation cannot see (or cannot read a member of) reports the
    // new AKKASG039; a type declared in a referenced assembly with no schema this generator can read
    // at all reports the AKKASG007 cross-assembly wording; anything else reports the plain AKKASG007
    // message. Reports at the LOCAL referencing property -- per Decision 16, this is true even for
    // both cross-assembly variants: the foreign type's own assembly gets no diagnostic at all, since
    // no generator work runs there.
    private static void ReportMissingNestedSchema(
        MessageInfo message,
        string fieldName,
        string typeFullName,
        TypeMapping mapping,
        string serializerClassName,
        ImmutableDictionary<TypeKey, AccessibilityFailure> accessibilityFailuresByType,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var at = new LocationKey(message.Key, fieldName);

        if (mapping.IsGenericConstruction)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnregisteredClosedGenericField, at, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), serializerClassName));
            return;
        }

        if (mapping.ForeignAssemblyName.Length > 0 && accessibilityFailuresByType.TryGetValue(mapping.Key, out var failure))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.NestedFieldNotAccessibleCrossAssembly, at, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), mapping.ForeignAssemblyName, failure.Description, serializerClassName));
            return;
        }

        diagnostics.Add(mapping.ForeignAssemblyName.Length > 0
            ? new DiagnosticSpec(DiagnosticKey.MissingNestedSerializableDefinitionCrossAssembly, at, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), mapping.ForeignAssemblyName, serializerClassName)
            : new DiagnosticSpec(DiagnosticKey.MissingNestedSerializableDefinition, at, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName)));
    }

    /// <summary>
    /// Fires AKKASG034 when a valid <c>[AkkaSerializable&lt;T&gt;]</c> registration's construction
    /// neither implements the serializer's protocol (so it can never become a top-level message)
    /// nor is referenced by any [AkkaField] property of a message reachable from a top-level
    /// message (so it can never be emitted as a nested Object field either, AKKASG023's mechanism).
    /// Such a registration compiles clean today and simply does nothing: <see cref="CollectReachableMessages"/>
    /// never reaches it, so it gets no generated Write/Read/SizeOf methods at all. A construction
    /// registered ONLY for nested-field use (legitimate; it need not implement the protocol) is
    /// exempt as long as it is actually reachable.
    /// </summary>
    private static bool ValidateClosedGenericProtocolCoverage(SerializerInfo serializer, ImmutableArray<MessageInfo> reachableMessages, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty || serializer.ProtocolTypeFullName.Length == 0)
            return true;

        var schemasByKey = serializer.ClosedGenericSchemas.ToDictionary(schema => schema.Key);
        var reachableKeys = new HashSet<TypeKey>(reachableMessages.Select(message => message.Key));
        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations)
        {
            if (!schemasByKey.TryGetValue(registration.Target, out var schema))
                continue;

            if (schema.Protocols.Contains(serializer.ProtocolTypeFullName))
                continue;

            if (reachableKeys.Contains(schema.Key))
                continue;

            // Reports at THIS registration's own [AkkaSerializable<T>] attribute application --
            // Decision 16: "the registration attribute for a closed generic".
            var at = new LocationKey(serializer.Key, ClosedGenericLocationMember(registration.TargetDisplayName));
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ClosedGenericRegistrationNotInProtocol, at,
                ToDisplayName(registration.TargetDisplayName), serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
            isValid = false;
        }

        return isValid;
    }

    // Reports AKKASG015 for a union member with no known message, or AKKASG039 when the member IS
    // [AkkaSerializable] but this compilation cannot see it or one of its members. A same-assembly
    // member with no known message gets the plain AKKASG015 message.
    private static void ReportUnionMemberNotSerializable(
        MessageInfo message,
        string fieldName,
        UnionMemberInfo member,
        string serializerClassName,
        ImmutableDictionary<TypeKey, AccessibilityFailure> accessibilityFailuresByType,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        // Both cross-assembly variants report at the LOCAL referencing field too -- per Decision 16,
        // the foreign member type's own assembly gets no diagnostic at all.
        var at = new LocationKey(message.Key, fieldName);

        if (member.ForeignAssemblyName.Length > 0 && accessibilityFailuresByType.TryGetValue(member.Key, out var failure))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberNotAccessibleCrossAssembly, at, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName), member.ForeignAssemblyName, failure.Description, serializerClassName));
            return;
        }

        diagnostics.Add(member.ForeignAssemblyName.Length > 0
            ? new DiagnosticSpec(DiagnosticKey.UnionMemberNotSerializableCrossAssembly, at, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName), member.ForeignAssemblyName, serializerClassName)
            : new DiagnosticSpec(DiagnosticKey.UnionMemberNotSerializable, at, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName)));
    }

    private static bool ValidateUnionField(
        MessageInfo message,
        FieldInfo field,
        ImmutableDictionary<TypeKey, MessageInfo> messagesByType,
        ImmutableDictionary<TypeKey, AccessibilityFailure> accessibilityFailuresByType,
        string serializerClassName,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var isValid = true;

        // Every union diagnostic below reports at the FIELD carrying the union -- not at any one
        // member's own declaration, which may not even be in this compilation.
        var at = new LocationKey(message.Key, field.Name);

        // AkkaUnionAttribute(Type first, params Type[] rest) makes an empty member set
        // unrepresentable: `first` is a mandatory constructor argument, so [AkkaUnion()] does not
        // compile and field.UnionMembers can never be empty here. The "at least one member type is
        // required" half of AKKASG019 that used to guard this is gone along with it. Grouped by
        // TypeKey (not display text) so two distinct types that happen to render the same display
        // string are never mistaken for a duplicate declaration of one type.
        foreach (var duplicate in field.UnionMembers
                     .GroupBy(member => member.Key)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidUnionMemberSet, at, field.Name, ToDisplayName(message.FullyQualifiedName), $"member type '{ToDisplayName(duplicate.Key.DisplayName ?? string.Empty)}' is declared more than once"));
            isValid = false;
        }

        var manifests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.Key, out var memberMessage))
            {
                ReportUnionMemberNotSerializable(message, field.Name, member, serializerClassName, accessibilityFailuresByType, diagnostics);
                isValid = false;
                continue;
            }

            if (!member.IsAssignable)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberNotAssignable, at, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            // Advisory tier for member types whose exact-runtime-type dispatch is compromised:
            //  - abstract member (AKKASG036, Warning): dispatch can NEVER select it -- an abstract
            //    type is never a runtime type, so its branch is dead code;
            //  - merely unsealed member (AKKASG025, Info): works, but an undeclared subtype of it
            //    fails at write time -- worth surfacing, not worth failing.
            // An abstract member fires AKKASG036 ONLY: it is definitionally unsealed, and stacking
            // the weaker AKKASG025 on top of it would be noise.
            if (member.IsAbstract)
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberAbstract, at, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
            else if (!member.IsSealed)
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberNotSealed, at, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));

            if (string.IsNullOrWhiteSpace(memberMessage.Manifest))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberMissingManifest, at, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
                continue;
            }

            if (!manifests.TryGetValue(memberMessage.Manifest, out var typesWithManifest))
            {
                typesWithManifest = new List<string>();
                manifests[memberMessage.Manifest] = typesWithManifest;
            }

            typesWithManifest.Add(member.TypeFullName);
        }

        foreach (var collision in manifests.Where(pair => pair.Value.Distinct(StringComparer.Ordinal).Count() > 1))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberManifestCollision, at, field.Name, ToDisplayName(message.FullyQualifiedName), collision.Key, string.Join(", ", collision.Value.Select(ToDisplayName))));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Strips a leading <c>global::</c> prefix from a fully-qualified type name string for display
    /// in a <see cref="Diagnostic"/> message ONLY. Every internal use of a fully-qualified name --
    /// dictionary keys, equality/grouping comparisons, and text appended into emitted source --
    /// keeps the raw <c>global::</c>-qualified form produced by <see cref="GetFullyQualifiedTypeName"/>
    /// and <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>; this helper must be applied only
    /// to the arguments carried by a <see cref="DiagnosticSpec"/> at each call site above.
    /// </summary>
    private static string ToDisplayName(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
    }
}
