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
public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// Diagnostics-only output for AKKASG029 (see the comment in <see cref="Initialize"/>). To
    /// preserve the old terminal stage's semantics, a serializer only reaches the coverage scan
    /// after it passes every check that used to precede <see cref="ValidateProtocolCoverage"/> in
    /// the single-output pipeline -- those checks run here SILENTLY (null reporter): the emission
    /// output above is the one that reports them, and reporting them twice would duplicate every
    /// pre-coverage diagnostic.
    /// </summary>
    private static void ReportProtocolCoverage(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages,
        Compilation compilation)
    {
        var duplicateSerializerIds = ComputeDuplicateSerializerIds(serializers);
        var duplicateProtocolBindings = ComputeDuplicateProtocolBindings(serializers);
        var genericDefinitions = messages
            .Where(message => message != null)
            .Cast<MessageInfo>()
            .Where(message => message.IsGenericDefinition)
            .ToImmutableArray();

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            if (string.IsNullOrWhiteSpace(serializer.Name) || serializer.SerializerId <= 0)
                continue;

            if (duplicateSerializerIds.ContainsKey(serializer.SerializerId))
                continue;

            if (duplicateProtocolBindings.ContainsKey(serializer.ProtocolTypeFullName))
                continue;

            if (!ValidateSerializerShape(serializer, report: null))
                continue;

            if (!ValidateProtocolType(serializer, report: null))
                continue;

            if (!ValidateFormatters(serializer, report: null))
                continue;

            if (!ValidateClosedGenericRegistrations(serializer, report: null))
                continue;

            if (!ValidateGenericDefinitions(serializer, genericDefinitions, report: null))
                continue;

            ValidateProtocolCoverage(context, serializer, compilation);
        }
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
    /// Fires AKKASG032 for each way the [AkkaSerializer] class declaration itself is unusable as
    /// a codegen target: not partial, not derived from the AkkaSerializer base class, or generic.
    /// Today each of these produces a wall of raw CS errors (CS0260/CS0759/CS0115/CS0264) pointing
    /// at the GENERATED file instead of the user's declaration; this replaces that with one direct
    /// diagnostic per violated rule, still on the user's class.
    /// A null <paramref name="report"/> evaluates the check silently -- the coverage output uses
    /// that to replicate the emission stage's gating without duplicating its diagnostics.
    /// </summary>
    private static bool ValidateSerializerShape(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        var isValid = true;

        if (!serializer.IsPartial)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must be declared 'partial': the generator emits a second declaration of this class"));
            isValid = false;
        }

        if (!serializer.DerivesFromAkkaSerializerBase)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must derive from Akka.Serialization.V2.AkkaSerializer: the generated members (Identifier, Manifest, Serialize, Deserialize, SizeHint) are declared as overrides of that base"));
            isValid = false;
        }

        if (serializer.IsGeneric)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
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
    private static bool ValidateProtocolType(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.ProtocolTypeFullName.Length == 0 || serializer.ProtocolTypeIsInterface)
            return true;

        report?.Invoke(Diagnostic.Create(ProtocolTypeMustBeInterface, Location.None, serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
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
    /// </summary>
    private static void ValidateProtocolCoverage(SourceProductionContext context, SerializerInfo serializer, Compilation compilation)
    {
        if (serializer.ProtocolTypeFullName.Length == 0)
            return;

        var knownTypes = KnownTypes.From(compilation);
        if (knownTypes.SerializableAttribute == null)
            return;

        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            // This whole-compilation walk re-runs on every edit (its output combines the
            // CompilationProvider by necessity); honor IDE cancellation between candidates.
            context.CancellationToken.ThrowIfCancellationRequested();

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

            context.ReportDiagnostic(Diagnostic.Create(ProtocolMessageNotSerializable, Location.None,
                ToDisplayName(GetFullyQualifiedTypeName(candidate)), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
        }
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

    private static bool ValidateFormatters(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.Formatters.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var formatter in serializer.Formatters)
        {
            if (!formatter.IsTargetSupported)
            {
                report?.Invoke(Diagnostic.Create(FormatterTargetNotSupported, Location.None, ToDisplayName(formatter.TargetTypeFullName), serializer.ClassName));
                isValid = false;
                continue;
            }

            if (formatter.IsAbstract)
            {
                report?.Invoke(Diagnostic.Create(InvalidFormatterType, Location.None, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName, ToDisplayName(formatter.TargetTypeFullName)));
                isValid = false;
                continue;
            }

            if (formatter.CtorKind == FormatterCtorKind.None)
            {
                report?.Invoke(Diagnostic.Create(FormatterConstructorNotUsable, Location.None, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName));
                isValid = false;
            }
        }

        foreach (var duplicate in serializer.Formatters
                     .Where(formatter => formatter.IsTargetSupported)
                     .GroupBy(formatter => formatter.TargetTypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            report?.Invoke(Diagnostic.Create(DuplicateFormatterRegistration, Location.None, serializer.ClassName, ToDisplayName(duplicate.Key)));
            isValid = false;
        }

        return isValid;
    }

    private static bool ValidateClosedGenericRegistrations(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations.Where(registration => registration.Message == null))
        {
            report?.Invoke(Diagnostic.Create(InvalidClosedGenericRegistration, Location.None, ToDisplayName(registration.TargetDisplayName), serializer.ClassName));
            isValid = false;
        }

        foreach (var duplicate in serializer.ClosedGenericRegistrations
                     .Where(registration => registration.Message != null)
                     .GroupBy(registration => registration.TargetDisplayName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            report?.Invoke(Diagnostic.Create(DuplicateClosedGenericRegistration, Location.None, serializer.ClassName, ToDisplayName(duplicate.Key)));
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
    private static bool ValidateGenericDefinitions(SerializerInfo serializer, ImmutableArray<MessageInfo> genericDefinitions, Action<Diagnostic>? report)
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

            report?.Invoke(Diagnostic.Create(GenericSerializableRequiresRegistration, Location.None, ToDisplayName(definition.FullyQualifiedName), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
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

    private static bool ValidateMessages(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingManifest, Location.None, ToDisplayName(message.FullyQualifiedName)));
            isValid = false;
        }

        foreach (var duplicate in topLevelMessages
                     .Where(m => !string.IsNullOrWhiteSpace(m.Manifest))
                     .GroupBy(m => m.Manifest, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var typeNames = string.Join(", ", duplicate.Select(m => ToDisplayName(m.FullyQualifiedName)));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateManifest, Location.None, serializer.ClassName, duplicate.Key, typeNames));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            if (message.Fields.Length == 0 && !message.AllowEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingFields, Location.None, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateFieldIndex, Location.None, ToDisplayName(message.FullyQualifiedName), duplicate.Key));
                isValid = false;
            }

            // Structural [AkkaField] problems found during extraction (static property, or a
            // getter the generated Write path could not call): these properties never made it into
            // message.Fields, so they cannot double-report through any of the checks below.
            foreach (var invalidField in message.InvalidFields)
            {
                context.ReportDiagnostic(Diagnostic.Create(FieldPropertyNotAccessible, Location.None, invalidField.PropertyName, ToDisplayName(message.FullyQualifiedName), invalidField.Reason));
                isValid = false;
            }

            // Read-side reconstruction: either no constructor could be selected, or the selected
            // constructor leaves [AkkaField] properties uncovered with no accessible setter to fall
            // back on -- both make deserialize impossible to generate.
            foreach (var error in message.ConstructionPlan.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoMatchingConstructor, Location.None, ToDisplayName(message.FullyQualifiedName), error));
                isValid = false;
            }

            // Advisory only: the selected constructor still works (its defaulted parameter is simply
            // never supplied), but the parameter's value silently reverts to its default on every
            // deserialize because no [AkkaField] property feeds it.
            foreach (var parameterName in message.ConstructionPlan.UncoveredDefaultedParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(ConstructorParameterNotCovered, Location.None, parameterName, ToDisplayName(message.FullyQualifiedName)));
            }

            // Error (AKKASG038): an object-typed property is always the envelope-payload boundary
            // (the static type alone carries that meaning); a field-level [AkkaUnion] on it can
            // never take effect, so this is contradictory author intent, not a harmless no-op.
            foreach (var field in message.Fields.Where(field => field.UnionDeclaredOnObjectField))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionDeclaredOnObjectField, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
            {
                context.ReportDiagnostic(field.Mapping.SuggestsEnvelopeOrUnion
                    ? Diagnostic.Create(UnsupportedFieldTypePolymorphic, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName))
                    : Diagnostic.Create(UnsupportedFieldType, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.MissingSerializableDefinition))
            {
                ReportMissingNestedSchema(context, message, field.Name, field.TypeFullName, field.Mapping, serializer.ClassName);
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.UnsupportedEnumUnderlyingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnumUnderlyingType, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.Mapping.TypeFullName), ToDisplayName(field.Mapping.EnumUnderlyingTypeName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                if (!ValidateUnionField(context, message, field, messagesByType, serializer.ClassName))
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

                    ReportMissingNestedSchema(context, message, field.Name, objectMapping.TypeFullName, objectMapping, serializer.ClassName);
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
            context.ReportDiagnostic(Diagnostic.Create(DuplicateGeneratedName, Location.None, serializer.ClassName, collision.Key, typeNames));
            isValid = false;
        }

        return isValid;
    }

    // Decision table for a nested type this generator cannot serialize today: a closed generic
    // construction reports AKKASG023 (register it with [AkkaSerializable<T>]); a type declared in a
    // referenced assembly reports the AKKASG007 cross-assembly wording; anything else reports the
    // plain AKKASG007 message.
    private static void ReportMissingNestedSchema(
        SourceProductionContext context,
        MessageInfo message,
        string fieldName,
        string typeFullName,
        TypeMapping mapping,
        string serializerClassName)
    {
        if (mapping.IsGenericConstruction)
        {
            context.ReportDiagnostic(Diagnostic.Create(UnregisteredClosedGenericField, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), serializerClassName));
            return;
        }

        context.ReportDiagnostic(mapping.ForeignAssemblyName.Length > 0
            ? Diagnostic.Create(MissingNestedSerializableDefinitionCrossAssembly, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), mapping.ForeignAssemblyName, serializerClassName)
            : Diagnostic.Create(MissingNestedSerializableDefinition, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName)));
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
    private static bool ValidateClosedGenericProtocolCoverage(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> reachableMessages)
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

            context.ReportDiagnostic(Diagnostic.Create(ClosedGenericRegistrationNotInProtocol, Location.None,
                ToDisplayName(registration.TargetDisplayName), serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
            isValid = false;
        }

        return isValid;
    }

    // Reports AKKASG015 for a union member with no known message. A referenced-assembly member
    // gets the cross-assembly wording; a same-assembly member gets the plain message.
    private static void ReportUnionMemberNotSerializable(SourceProductionContext context, MessageInfo message, string fieldName, UnionMemberInfo member, string serializerClassName)
    {
        context.ReportDiagnostic(member.ForeignAssemblyName.Length > 0
            ? Diagnostic.Create(UnionMemberNotSerializableCrossAssembly, Location.None, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName), member.ForeignAssemblyName, serializerClassName)
            : Diagnostic.Create(UnionMemberNotSerializable, Location.None, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName)));
    }

    private static bool ValidateUnionField(
        SourceProductionContext context,
        MessageInfo message,
        FieldInfo field,
        ImmutableDictionary<string, MessageInfo> messagesByType,
        string serializerClassName)
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
            context.ReportDiagnostic(Diagnostic.Create(InvalidUnionMemberSet, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), $"member type '{ToDisplayName(duplicate.Key)}' is declared more than once"));
            isValid = false;
        }

        var manifests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.TypeFullName, out var memberMessage))
            {
                ReportUnionMemberNotSerializable(context, message, field.Name, member, serializerClassName);
                isValid = false;
                continue;
            }

            if (!member.IsAssignable)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotAssignable, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
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
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberAbstract, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
            else if (!member.IsSealed)
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotSealed, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));

            if (string.IsNullOrWhiteSpace(memberMessage.Manifest))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberMissingManifest, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
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
            context.ReportDiagnostic(Diagnostic.Create(UnionMemberManifestCollision, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), collision.Key, string.Join(", ", collision.Value.Select(ToDisplayName))));
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
    /// to the arguments passed to <c>Diagnostic.Create(...)</c> at each reporting call site.
    /// </summary>
    private static string ToDisplayName(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
    }
}
