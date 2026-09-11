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
using System.Threading;
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
    /// once, by <see cref="EmitResolvedSerializer"/>); reporting them again here would duplicate
    /// every pre-coverage diagnostic. This output still combines the live <see cref="Compilation"/>
    /// (AKKASG029's whole-compilation scan genuinely needs it), so it re-runs on every edit exactly
    /// as before -- only the GATE check underneath it got cheaper.
    /// </summary>
    private static void ReportProtocolCoverage(SourceProductionContext context, ResolvedSerializer resolved, Compilation compilation)
    {
        if (!resolved.IsEmittable)
            return;

        var protocolCoverageDiagnostics = ValidateProtocolCoverage(resolved.Serializer, compilation, context.CancellationToken);
        foreach (var diagnostic in protocolCoverageDiagnostics)
            context.ReportDiagnostic(DiagnosticRegistry.ToDiagnostic(diagnostic));
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

        if (string.IsNullOrWhiteSpace(serializer.Name))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerName, serializer.ClassName));
            return new SerializerGate(false, diagnostics.ToImmutable());
        }

        if (serializer.SerializerId <= 0)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerId, serializer.ClassName, serializer.SerializerId.ToString()));
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
    internal static ImmutableArray<DiagnosticSpec> Validate(SerializerInfo serializer, ImmutableArray<MessageInfo> messages)
    {
        var resolved = ResolveSerializerMessages(serializer, messages);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        ValidateResolved(serializer, resolved, diagnostics);
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
            ComputeGenericDefinitions(messages));
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
    private static void ValidateResolved(SerializerInfo serializer, ResolvedSerializerMessages resolved, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (!ValidateMessages(serializer, resolved.TopLevelMessages, resolved.ReachableMessages, resolved.ResolvedMessagesByType, diagnostics))
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

        if (!serializer.IsPartial)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, serializer.ClassName,
                "must be declared 'partial': the generator emits a second declaration of this class"));
            isValid = false;
        }

        if (!serializer.DerivesFromAkkaSerializerBase)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, serializer.ClassName,
                "must derive from Akka.Serialization.V2.AkkaSerializer: the generated members (Identifier, Manifest, Serialize, Deserialize, SizeHint) are declared as overrides of that base"));
            isValid = false;
        }

        if (serializer.IsGeneric)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidSerializerShape, serializer.ClassName,
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

        diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ProtocolTypeMustBeInterface, serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
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
    /// The protocol interface is matched by fully-qualified name against each candidate's
    /// <see cref="ITypeSymbol.AllInterfaces"/>: the cached <see cref="SerializerInfo"/> is
    /// deliberately symbol-free, and within one compilation a fully-qualified name identifies
    /// exactly one type, so the string comparison is equivalent to the former
    /// <see cref="SymbolEqualityComparer.Default"/> lookup.
    /// This is the one validation step that genuinely needs a <see cref="Compilation"/> (the
    /// whole-compilation type scan below), so unlike its neighbors it is not "model-only" -- but it
    /// still returns <see cref="DiagnosticSpec"/> values rather than reporting through a
    /// <see cref="SourceProductionContext"/> directly, so it can be driven by a plain
    /// <see cref="CancellationToken"/> and asserted against directly in tests.
    /// </summary>
    internal static ImmutableArray<DiagnosticSpec> ValidateProtocolCoverage(SerializerInfo serializer, Compilation compilation, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();

        if (serializer.ProtocolTypeFullName.Length == 0)
            return diagnostics.ToImmutable();

        var knownTypes = KnownTypes.From(compilation);
        if (knownTypes.SerializableAttribute == null)
            return diagnostics.ToImmutable();

        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            // This whole-compilation walk re-runs on every edit (its output combines the
            // CompilationProvider by necessity); honor IDE cancellation between candidates.
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                continue;

            if (candidate.IsAbstract)
                continue;

            if (!ImplementsProtocol(candidate, serializer.ProtocolTypeFullName))
                continue;

            var isMarked = candidate.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            if (isMarked)
                continue;

            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ProtocolMessageNotSerializable,
                ToDisplayName(GetFullyQualifiedTypeName(candidate)), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
        }

        return diagnostics.ToImmutable();
    }

    private static bool ImplementsProtocol(INamedTypeSymbol candidate, string protocolTypeFullName)
    {
        foreach (var implemented in candidate.AllInterfaces)
        {
            if (string.Equals(implemented.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), protocolTypeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every named type declared in <paramref name="compilation"/>'s OWN source (never a referenced
    /// assembly: <see cref="Compilation.Assembly"/> is the assembly being compiled), recursively
    /// including nested types. Used only by <see cref="ValidateProtocolCoverage"/>, transiently,
    /// inside the diagnostics-only coverage callback -- never stored in a cached provider.
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
            if (!formatter.IsTargetSupported)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FormatterTargetNotSupported, ToDisplayName(formatter.TargetTypeFullName), serializer.ClassName));
                isValid = false;
                continue;
            }

            if (formatter.IsAbstract)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidFormatterType, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName, ToDisplayName(formatter.TargetTypeFullName)));
                isValid = false;
                continue;
            }

            if (formatter.CtorKind == FormatterCtorKind.None)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FormatterConstructorNotUsable, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName));
                isValid = false;
            }
        }

        foreach (var duplicate in serializer.Formatters
                     .Where(formatter => formatter.IsTargetSupported)
                     .GroupBy(formatter => formatter.TargetTypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateFormatterRegistration, serializer.ClassName, ToDisplayName(duplicate.Key)));
            isValid = false;
        }

        return isValid;
    }

    private static bool ValidateClosedGenericRegistrations(SerializerInfo serializer, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations.Where(registration => registration.Message == null))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidClosedGenericRegistration, ToDisplayName(registration.TargetDisplayName), serializer.ClassName));
            isValid = false;
        }

        foreach (var duplicate in serializer.ClosedGenericRegistrations
                     .Where(registration => registration.Message != null)
                     .GroupBy(registration => registration.TargetDisplayName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateClosedGenericRegistration, serializer.ClassName, ToDisplayName(duplicate.Key)));
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

            var hasRegistration = serializer.ClosedGenericRegistrations.Any(registration =>
                registration.Message != null &&
                string.Equals(registration.Message.DefinitionFullName, definition.FullyQualifiedName, StringComparison.Ordinal));
            if (hasRegistration)
                continue;

            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.GenericSerializableRequiresRegistration, ToDisplayName(definition.FullyQualifiedName), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
            isValid = false;
        }

        return isValid;
    }

    private static ImmutableArray<MessageInfo> CollectReachableMessages(
        ImmutableArray<MessageInfo> topLevelMessages,
        ImmutableDictionary<string, MessageInfo> allMessagesByType)
    {
        var messages = ImmutableArray.CreateBuilder<MessageInfo>();
        var visited = new HashSet<string>();
        var pending = new Queue<MessageInfo>(topLevelMessages);

        while (pending.Count > 0)
        {
            var message = pending.Dequeue();
            if (!visited.Add(message.FullyQualifiedName))
                continue;

            messages.Add(message);
            var referencedObjectTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in message.Fields)
            {
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                    referencedObjectTypes.Add(objectMapping.TypeFullName);

                // Union members are reachable exactly like nested Object fields: each member needs
                // its Write/Read/SizeOf methods generated for the union dispatch to call into.
                foreach (var unionMember in field.UnionMembers)
                {
                    if (unionMember.IsSupported)
                        referencedObjectTypes.Add(unionMember.TypeFullName);
                }
            }

            foreach (var typeName in referencedObjectTypes)
            {
                if (allMessagesByType.TryGetValue(typeName, out var nestedMessage))
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
        ImmutableDictionary<string, MessageInfo> messagesByType,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.MissingManifest, ToDisplayName(message.FullyQualifiedName)));
            isValid = false;
        }

        foreach (var duplicate in topLevelMessages
                     .Where(m => !string.IsNullOrWhiteSpace(m.Manifest))
                     .GroupBy(m => m.Manifest, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var typeNames = string.Join(", ", duplicate.Select(m => ToDisplayName(m.FullyQualifiedName)));
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateManifest, serializer.ClassName, duplicate.Key, typeNames));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            if (message.Fields.Length == 0 && !message.AllowEmpty)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.MissingFields, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateFieldIndex, ToDisplayName(message.FullyQualifiedName), duplicate.Key.ToString()));
                isValid = false;
            }

            // Structural [AkkaField] problems found during extraction (static property, or a
            // getter the generated Write path could not call): these properties never made it into
            // message.Fields, so they cannot double-report through any of the checks below.
            foreach (var invalidField in message.InvalidFields)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.FieldPropertyNotAccessible, invalidField.PropertyName, ToDisplayName(message.FullyQualifiedName), invalidField.Reason));
                isValid = false;
            }

            // Read-side reconstruction: either no constructor could be selected, or the selected
            // constructor leaves [AkkaField] properties uncovered with no accessible setter to fall
            // back on -- both make deserialize impossible to generate.
            foreach (var error in message.ConstructionPlan.Errors)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.NoMatchingConstructor, ToDisplayName(message.FullyQualifiedName), error));
                isValid = false;
            }

            // Advisory only: the selected constructor still works (its defaulted parameter is simply
            // never supplied), but the parameter's value silently reverts to its default on every
            // deserialize because no [AkkaField] property feeds it.
            foreach (var parameterName in message.ConstructionPlan.UncoveredDefaultedParameters)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ConstructorParameterNotCovered, parameterName, ToDisplayName(message.FullyQualifiedName)));
            }

            // Error (AKKASG038): an object-typed property is always the envelope-payload boundary
            // (the static type alone carries that meaning); a field-level [AkkaUnion] on it can
            // never take effect, so this is contradictory author intent, not a harmless no-op.
            foreach (var field in message.Fields.Where(field => field.UnionDeclaredOnObjectField))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionDeclaredOnObjectField, field.Name, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
            {
                diagnostics.Add(field.Mapping.SuggestsEnvelopeOrUnion
                    ? new DiagnosticSpec(DiagnosticKey.UnsupportedFieldTypePolymorphic, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName))
                    : new DiagnosticSpec(DiagnosticKey.UnsupportedFieldType, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.MissingSerializableDefinition))
            {
                ReportMissingNestedSchema(message, field.Name, field.TypeFullName, field.Mapping, serializer.ClassName, diagnostics);
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.UnsupportedEnumUnderlyingType))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnsupportedEnumUnderlyingType, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.Mapping.TypeFullName), ToDisplayName(field.Mapping.EnumUnderlyingTypeName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                if (!ValidateUnionField(message, field, messagesByType, serializer.ClassName, diagnostics))
                    isValid = false;
            }

            // An Object mapping that resolves to no known message would generate a call to a
            // nonexistent Write/Read/SizeOf method. ReportMissingNestedSchema tells apart the two
            // ways that happens: a genuine unregistered closed generic construction (AKKASG023), or
            // a non-generic type with no syntax in THIS compilation (AKKASG007, cross-assembly
            // wording when the type's declaring assembly says so).
            foreach (var field in message.Fields)
            {
                var seenTypeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                {
                    if (!seenTypeNames.Add(objectMapping.TypeFullName) || messagesByType.ContainsKey(objectMapping.TypeFullName))
                        continue;

                    ReportMissingNestedSchema(message, field.Name, objectMapping.TypeFullName, objectMapping, serializer.ClassName, diagnostics);
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
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.DuplicateGeneratedName, serializer.ClassName, collision.Key, typeNames));
            isValid = false;
        }

        return isValid;
    }

    // Decision table for a nested type this generator cannot serialize today: a closed generic
    // construction reports AKKASG023 (register it with [AkkaSerializable<T>]); a type declared in a
    // referenced assembly reports the AKKASG007 cross-assembly wording; anything else reports the
    // plain AKKASG007 message.
    private static void ReportMissingNestedSchema(
        MessageInfo message,
        string fieldName,
        string typeFullName,
        TypeMapping mapping,
        string serializerClassName,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (mapping.IsGenericConstruction)
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnregisteredClosedGenericField, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), serializerClassName));
            return;
        }

        diagnostics.Add(mapping.ForeignAssemblyName.Length > 0
            ? new DiagnosticSpec(DiagnosticKey.MissingNestedSerializableDefinitionCrossAssembly, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), mapping.ForeignAssemblyName, serializerClassName)
            : new DiagnosticSpec(DiagnosticKey.MissingNestedSerializableDefinition, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName)));
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

        var reachableNames = new HashSet<string>(reachableMessages.Select(message => message.FullyQualifiedName), StringComparer.Ordinal);
        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations)
        {
            if (registration.Message == null)
                continue;

            if (registration.Message.Protocols.Contains(serializer.ProtocolTypeFullName))
                continue;

            if (reachableNames.Contains(registration.Message.FullyQualifiedName))
                continue;

            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.ClosedGenericRegistrationNotInProtocol,
                ToDisplayName(registration.TargetDisplayName), serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
            isValid = false;
        }

        return isValid;
    }

    // Reports AKKASG015 for a union member with no known message. A referenced-assembly member
    // gets the cross-assembly wording; a same-assembly member gets the plain message.
    private static void ReportUnionMemberNotSerializable(MessageInfo message, string fieldName, UnionMemberInfo member, string serializerClassName, ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        diagnostics.Add(member.ForeignAssemblyName.Length > 0
            ? new DiagnosticSpec(DiagnosticKey.UnionMemberNotSerializableCrossAssembly, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName), member.ForeignAssemblyName, serializerClassName)
            : new DiagnosticSpec(DiagnosticKey.UnionMemberNotSerializable, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName)));
    }

    private static bool ValidateUnionField(
        MessageInfo message,
        FieldInfo field,
        ImmutableDictionary<string, MessageInfo> messagesByType,
        string serializerClassName,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var isValid = true;

        // AkkaUnionAttribute(Type first, params Type[] rest) makes an empty member set
        // unrepresentable: `first` is a mandatory constructor argument, so [AkkaUnion()] does not
        // compile and field.UnionMembers can never be empty here. The "at least one member type is
        // required" half of AKKASG019 that used to guard this is gone along with it.
        foreach (var duplicate in field.UnionMembers
                     .GroupBy(member => member.TypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.InvalidUnionMemberSet, field.Name, ToDisplayName(message.FullyQualifiedName), $"member type '{ToDisplayName(duplicate.Key)}' is declared more than once"));
            isValid = false;
        }

        var manifests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.TypeFullName, out var memberMessage))
            {
                ReportUnionMemberNotSerializable(message, field.Name, member, serializerClassName, diagnostics);
                isValid = false;
                continue;
            }

            if (!member.IsAssignable)
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberNotAssignable, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
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
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberAbstract, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
            else if (!member.IsSealed)
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberNotSealed, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));

            if (string.IsNullOrWhiteSpace(memberMessage.Manifest))
            {
                diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberMissingManifest, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
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
            diagnostics.Add(new DiagnosticSpec(DiagnosticKey.UnionMemberManifestCollision, field.Name, ToDisplayName(message.FullyQualifiedName), collision.Key, string.Join(", ", collision.Value.Select(ToDisplayName))));
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
