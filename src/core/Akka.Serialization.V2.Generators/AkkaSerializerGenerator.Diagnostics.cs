//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Diagnostics.cs" company="Akka.NET Project">
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
    // AkkaSerializerAttribute<TProtocol>(string name, int serializerId) requires both arguments at
    // every call site -- there is no longer a way to OMIT Name or SerializerId, so AKKASG001/002
    // no longer guard "missing" registration. They still guard the argument VALUES: a caller can
    // still write [AkkaSerializer<T>(null!, 0)] or an empty/whitespace name or a non-positive id,
    // and those remain compile-time errors from this generator.
    private static readonly DiagnosticDescriptor InvalidSerializerName = new(
        "AKKASG001",
        "Serializer name must be a non-empty string",
        "[AkkaSerializer] class '{0}' specifies an invalid Name: it must not be null, empty, or consist only of whitespace",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSerializerId = new(
        "AKKASG002",
        "Serializer id must be a positive integer",
        "[AkkaSerializer] class '{0}' specifies SerializerId {1}, which must be a positive, non-zero integer unique within the actor system",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedFieldType = new(
        "AKKASG003",
        "Unsupported field type",
        "Property '{0}' on type '{1}' has unsupported generated serializer field type '{2}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as UnsupportedFieldType. Used only for an interface, abstract class,
    // or type parameter field, which is usually a forgotten [AkkaUnion], or a field that should
    // simply be typed object.
    private static readonly DiagnosticDescriptor UnsupportedFieldTypePolymorphic = new(
        "AKKASG003",
        "Unsupported field type",
        "Property '{0}' on type '{1}' has unsupported generated serializer field type '{2}'. Declare a closed member set with [AkkaUnion], or type the property as object.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingFields = new(
        "AKKASG004",
        "No serializable fields",
        "[AkkaSerializable] type '{0}' must declare at least one [AkkaField] property, or set AllowEmpty = true if the message is deliberately fieldless",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateFieldIndex = new(
        "AKKASG005",
        "Duplicate field index",
        "[AkkaSerializable] type '{0}' has duplicate [AkkaField] index {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingManifest = new(
        "AKKASG006",
        "Top-level message manifest is required",
        "[AkkaSerializable] top-level protocol message '{0}' must specify Manifest for serializer dispatch",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingNestedSerializableDefinition = new(
        "AKKASG007",
        "Nested value object serialization definition is required",
        "Property '{0}' on type '{1}' uses nested value object type '{2}', which must be annotated with [AkkaSerializable] and explicit [AkkaField] fields",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as MissingNestedSerializableDefinition. Used only when the nested
    // type's assembly is not the one being compiled. This generator can only read a schema from
    // the current compilation, so the type may already carry both attributes in its own assembly
    // and still be unreadable from here. The message must name only the fixes that work today, and
    // must not claim the type lacks the attributes.
    private static readonly DiagnosticDescriptor MissingNestedSerializableDefinitionCrossAssembly = new(
        "AKKASG007",
        "Nested value object serialization definition is required",
        "Property '{0}' on type '{1}' uses nested value object type '{2}', which is declared in assembly '{3}'. " +
        "This generator cannot read a schema from a referenced assembly yet. " +
        "Register [AkkaSerializerFormatter<{2}, TFormatter>] on '{4}', or declare the type in this assembly.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // The `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint on
    // AkkaSerializerFormatterAttribute<TTarget, TFormatter> now makes interface conformance a
    // compile-time error at the attribute usage site, so this narrows to the one thing a generic
    // constraint cannot express: TFormatter must not be abstract (an abstract type still satisfies
    // the constraint, and there is deliberately no `new()` clause to rule it out, since a formatter
    // with only an ExtendedActorSystem constructor is legitimate).
    private static readonly DiagnosticDescriptor InvalidFormatterType = new(
        "AKKASG008",
        "Formatter type must not be abstract",
        "Formatter '{0}' on serializer '{1}' must not be abstract: it cannot be instantiated as the runtime IAkkaMessagePackFormatter<{2}>",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateFormatterRegistration = new(
        "AKKASG009",
        "Duplicate formatter registration",
        "Serializer '{0}' registers multiple formatters for type '{1}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FormatterConstructorNotUsable = new(
        "AKKASG010",
        "Formatter constructor not usable",
        "Formatter '{0}' on serializer '{1}' must have a public parameterless constructor or a public constructor taking ExtendedActorSystem",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FormatterTargetNotSupported = new(
        "AKKASG011",
        "Formatter target type is not supported",
        "Formatter target type '{0}' on serializer '{1}' must be a non-generic, non-array named type",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateManifest = new(
        "AKKASG012",
        "Duplicate top-level message manifest",
        "Serializer '{0}' has multiple top-level [AkkaSerializable] messages with manifest '{1}': {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateSerializerId = new(
        "AKKASG013",
        "Duplicate serializer id",
        "SerializerId {0} is used by multiple [AkkaSerializer] classes: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedEnumUnderlyingType = new(
        "AKKASG014",
        "Enum underlying type is not supported",
        "Property '{0}' on type '{1}' uses enum type '{2}' whose underlying type '{3}' is not fully int32-representable; " +
        "generated serializers encode enums as int32, so use an enum backed by sbyte, byte, short, ushort, or int",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotSerializable = new(
        "AKKASG015",
        "Union member type is not serializable",
        "Union member '{0}' on property '{1}' of type '{2}' must be an [AkkaSerializable] class or struct handled by this serializer",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as UnionMemberNotSerializable. Used only when the member type's
    // assembly is not the one being compiled. See MissingNestedSerializableDefinitionCrossAssembly
    // for why the member may already carry [AkkaSerializable] and still be unreadable from here.
    private static readonly DiagnosticDescriptor UnionMemberNotSerializableCrossAssembly = new(
        "AKKASG015",
        "Union member type is not serializable",
        "Union member '{0}' on property '{1}' of type '{2}' is declared in assembly '{3}'. " +
        "This generator cannot read a schema from a referenced assembly yet. " +
        "Register [AkkaSerializerFormatter<{0}, TFormatter>] on '{4}', or declare the member in this assembly.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberMissingManifest = new(
        "AKKASG016",
        "Union member manifest is required",
        "Union member '{0}' on property '{1}' of type '{2}' must specify Manifest in its [AkkaSerializable] attribute: the manifest is the union discriminator",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberManifestCollision = new(
        "AKKASG017",
        "Union member manifests must be unique",
        "Union on property '{0}' of type '{1}' has multiple members with manifest '{2}': {3}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotAssignable = new(
        "AKKASG018",
        "Union member is not assignable to the field type",
        "Union member '{0}' on property '{1}' of type '{2}' is not implicitly convertible to the field type '{3}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidUnionMemberSet = new(
        "AKKASG019",
        "Union member set is invalid",
        "Union on property '{0}' of type '{1}' has an invalid member set: {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidClosedGenericRegistration = new(
        "AKKASG020",
        "Closed generic registration is invalid",
        "[AkkaSerializable<T>] registration '{0}' on serializer '{1}' must be a closed generic construction of a generic type annotated with [AkkaSerializable]",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateClosedGenericRegistration = new(
        "AKKASG021",
        "Duplicate closed generic registration",
        "Serializer '{0}' registers the closed construction '{1}' more than once",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericSerializableRequiresRegistration = new(
        "AKKASG022",
        "Generic serializable type requires closed generic registrations",
        "Generic [AkkaSerializable] type '{0}' implements protocol '{1}' of serializer '{2}' but has no [AkkaSerializable<T>] registrations; a source generator cannot serialize an open generic, so register each closed construction with [AkkaSerializable<T>(Manifest = ...)] on the serializer class",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnregisteredClosedGenericField = new(
        "AKKASG023",
        "Closed generic field type is not registered",
        "Property '{0}' on type '{1}' uses closed generic [AkkaSerializable] type '{2}', which must be registered on serializer '{3}' with [AkkaSerializable<T>]",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateGeneratedName = new(
        "AKKASG024",
        "Generated member name collision",
        "Serializer '{0}' produces the same generated member name '{1}' for distinct message types {2}; rename one of the types to disambiguate",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotSealed = new(
        "AKKASG025",
        "Union member type is not sealed",
        "Union member '{0}' on property '{1}' of type '{2}' is not sealed; union write dispatch matches the exact runtime type, so an undeclared subtype of '{0}' fails serialization -- consider sealing it",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoMatchingConstructor = new(
        "AKKASG026",
        "No matching constructor",
        "[AkkaSerializable] type '{0}' cannot be reconstructed on deserialize: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConstructorParameterNotCovered = new(
        "AKKASG027",
        "Constructor parameter not covered by [AkkaField]",
        "Constructor parameter '{0}' of [AkkaSerializable] type '{1}' has a default value and is not covered by any [AkkaField] property; it silently resets to its default value on every deserialize",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FieldPropertyNotAccessible = new(
        "AKKASG028",
        "[AkkaField] must be on an accessible instance property",
        "[AkkaField] property '{0}' on type '{1}' {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ProtocolMessageNotSerializable = new(
        "AKKASG029",
        "Protocol message type is not [AkkaSerializable]",
        "Type '{0}' implements protocol '{1}' of serializer '{2}' but is not [AkkaSerializable]; it is invisible to the generated Manifest/Serialize/Deserialize switches and fails only at runtime, when it is first sent",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateProtocolBinding = new(
        "AKKASG031",
        "Protocol interface bound by multiple serializers",
        "Protocol '{0}' is bound by multiple [AkkaSerializer] classes: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSerializerShape = new(
        "AKKASG032",
        "Serializer class shape is invalid",
        "[AkkaSerializer] class '{0}' {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ProtocolTypeMustBeInterface = new(
        "AKKASG033",
        "Protocol type must be an interface",
        "[AkkaSerializer<{1}>] class '{0}' specifies a protocol type that is not an interface; dispatch matches messages via AllInterfaces, so a non-interface protocol type silently generates a serializer with empty Manifest/Serialize/Deserialize switches",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ClosedGenericRegistrationNotInProtocol = new(
        "AKKASG034",
        "Registered closed generic type does not implement the serializer protocol",
        "Closed generic construction '{0}' registered on serializer '{1}' does not implement protocol '{2}' and is not referenced by any [AkkaField] property of a message reachable from it; the registration has no effect",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberAbstract = new(
        "AKKASG036",
        "Union member type is abstract",
        "Union member '{0}' on property '{1}' of type '{2}' is abstract; union write dispatch matches the exact runtime type, and an abstract type is never a runtime type, so this member's dispatch branch is dead code -- declare its concrete subtypes as union members instead",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ManifestIgnoredOnGenericDefinition = new(
        "AKKASG037",
        "Manifest on a generic [AkkaSerializable] definition is ignored",
        "Generic [AkkaSerializable] type '{0}' specifies Manifest '{1}', which is ignored: a generic definition is never serialized directly, and each closed construction registered with [AkkaSerializable<T>] supplies its own Manifest",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// An object-typed property is ALWAYS the envelope-payload boundary (Decision 20): the static
    /// type alone carries that meaning, with no attribute involved. A field-level <c>[AkkaUnion]</c>
    /// on such a property is therefore contradictory author intent -- not a harmless no-op -- so
    /// this is an ERROR, unlike the retired AKKASG035 advisory it replaces (deliberately a
    /// different id; AKKASG035 stays a permanent gap).
    /// </summary>
    private static readonly DiagnosticDescriptor UnionDeclaredOnObjectField = new(
        "AKKASG038",
        "Union declaration on an object-typed property",
        "Property '{0}' on type '{1}' is typed object, which is always an envelope payload boundary, but carries a field-level [AkkaUnion]. Type the property as the union's base type, or remove the attribute.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
