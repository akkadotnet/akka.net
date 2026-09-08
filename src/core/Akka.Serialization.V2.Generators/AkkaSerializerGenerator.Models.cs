//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Models.cs" company="Akka.NET Project">
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
    /// Structural-equality helpers shared by the cached pipeline models below.
    /// </summary>
    private static class ValueEquality
    {
        public static bool SequenceEquals<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        {
            if (left.IsDefault || right.IsDefault)
                return left.IsDefault && right.IsDefault;

            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                    return false;
            }

            return true;
        }

        public const int Seed = 17;

        public static int Combine(int hash, int value) => unchecked(hash * 31 + value);

        public static int Combine(int hash, bool value) => Combine(hash, value ? 1 : 0);

        public static int Combine(int hash, string? value)
            => Combine(hash, value == null ? 0 : StringComparer.Ordinal.GetHashCode(value));

        public static int Combine<T>(int hash, ImmutableArray<T> values)
        {
            if (values.IsDefault)
                return Combine(hash, -1);

            hash = Combine(hash, values.Length);
            foreach (var value in values)
                hash = Combine(hash, value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(value));

            return hash;
        }
    }

    internal sealed class SerializerInfo : IEquatable<SerializerInfo>
    {
        public SerializerInfo(
            string ns,
            string className,
            string fullyQualifiedName,
            string name,
            int serializerId,
            string protocolTypeFullName,
            bool protocolTypeIsInterface,
            Accessibility declaredAccessibility,
            ImmutableArray<FormatterInfo> formatters,
            ImmutableArray<ClosedGenericRegistrationInfo> closedGenericRegistrations,
            bool isPartial,
            bool isGeneric,
            bool derivesFromAkkaSerializerBase)
        {
            Namespace = ns;
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            SerializerId = serializerId;
            ProtocolTypeFullName = protocolTypeFullName;
            ProtocolTypeIsInterface = protocolTypeIsInterface;
            DeclaredAccessibility = declaredAccessibility;
            Formatters = formatters;
            ClosedGenericRegistrations = closedGenericRegistrations;
            IsPartial = isPartial;
            IsGeneric = isGeneric;
            DerivesFromAkkaSerializerBase = derivesFromAkkaSerializerBase;
        }

        public string Namespace { get; }
        public string ClassName { get; }
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public int SerializerId { get; }

        /// <summary>
        /// Fully-qualified display name of the <c>[AkkaSerializer&lt;TProtocol&gt;]</c> type
        /// argument; empty when the type argument was not a named type. All protocol matching runs
        /// on this string (ordinal) -- the symbol itself is never retained.
        /// </summary>
        public string ProtocolTypeFullName { get; }

        /// <summary>Whether the protocol type argument is an interface. See AKKASG033.</summary>
        public bool ProtocolTypeIsInterface { get; }

        public Accessibility DeclaredAccessibility { get; }
        public ImmutableArray<FormatterInfo> Formatters { get; }
        public ImmutableArray<ClosedGenericRegistrationInfo> ClosedGenericRegistrations { get; }

        /// <summary>Whether every syntax declaration of this class carries 'partial'. See AKKASG032.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the serializer class itself is a generic type definition. See AKKASG032.</summary>
        public bool IsGeneric { get; }

        /// <summary>Whether the class derives (directly or transitively) from <c>Akka.Serialization.V2.AkkaSerializer</c>. See AKKASG032.</summary>
        public bool DerivesFromAkkaSerializerBase { get; }

        public bool Equals(SerializerInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && SerializerId == other.SerializerId
                && string.Equals(ProtocolTypeFullName, other.ProtocolTypeFullName, StringComparison.Ordinal)
                && ProtocolTypeIsInterface == other.ProtocolTypeIsInterface
                && DeclaredAccessibility == other.DeclaredAccessibility
                && IsPartial == other.IsPartial
                && IsGeneric == other.IsGeneric
                && DerivesFromAkkaSerializerBase == other.DerivesFromAkkaSerializerBase
                && ValueEquality.SequenceEquals(Formatters, other.Formatters)
                && ValueEquality.SequenceEquals(ClosedGenericRegistrations, other.ClosedGenericRegistrations);
        }

        public override bool Equals(object? obj) => Equals(obj as SerializerInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Namespace);
            hash = ValueEquality.Combine(hash, ClassName);
            hash = ValueEquality.Combine(hash, FullyQualifiedName);
            hash = ValueEquality.Combine(hash, Name);
            hash = ValueEquality.Combine(hash, SerializerId);
            hash = ValueEquality.Combine(hash, ProtocolTypeFullName);
            hash = ValueEquality.Combine(hash, ProtocolTypeIsInterface);
            hash = ValueEquality.Combine(hash, (int)DeclaredAccessibility);
            hash = ValueEquality.Combine(hash, IsPartial);
            hash = ValueEquality.Combine(hash, IsGeneric);
            hash = ValueEquality.Combine(hash, DerivesFromAkkaSerializerBase);
            hash = ValueEquality.Combine(hash, Formatters);
            hash = ValueEquality.Combine(hash, ClosedGenericRegistrations);
            return hash;
        }
    }

    internal sealed class MessageInfo : IEquatable<MessageInfo>
    {
        public MessageInfo(
            string simpleName,
            string fullyQualifiedName,
            string manifest,
            ImmutableArray<FieldInfo> fields,
            ImmutableArray<string> protocols,
            bool allowEmpty,
            ImmutableArray<InvalidFieldInfo> invalidFields,
            ConstructionPlan constructionPlan,
            bool isGenericDefinition = false,
            string definitionFullName = "")
        {
            SimpleName = simpleName;
            FullyQualifiedName = fullyQualifiedName;
            Manifest = manifest;
            Fields = fields;
            Protocols = protocols;
            AllowEmpty = allowEmpty;
            InvalidFields = invalidFields;
            ConstructionPlan = constructionPlan;
            IsGenericDefinition = isGenericDefinition;
            DefinitionFullName = definitionFullName;
        }

        public string SimpleName { get; }
        public string FullyQualifiedName { get; }
        public string Manifest { get; }
        public ImmutableArray<FieldInfo> Fields { get; }

        /// <summary>
        /// Fully-qualified display names of every implemented interface (see
        /// <see cref="GetProtocolNames"/>) -- the symbol-free protocol list matched ordinally
        /// against <see cref="SerializerInfo.ProtocolTypeFullName"/> for top-level dispatch.
        /// </summary>
        public ImmutableArray<string> Protocols { get; }

        public bool AllowEmpty { get; }

        /// <summary>
        /// [AkkaField] properties excluded from <see cref="Fields"/> because they are structurally
        /// unusable (static, or an inaccessible getter) -- see AKKASG028. Empty for a valid message.
        /// </summary>
        public ImmutableArray<InvalidFieldInfo> InvalidFields { get; }

        /// <summary>
        /// How the read method reconstructs this type on deserialize: the chosen constructor's
        /// NAMED-argument bindings plus any leftover object-initializer assignments, or the reasons
        /// no plan could be built (AKKASG026/027). See <see cref="ConstructionPlan"/>.
        /// </summary>
        public ConstructionPlan ConstructionPlan { get; }

        /// <summary>
        /// True for a generic <c>[AkkaSerializable]</c> DEFINITION (e.g. <c>Wrapper&lt;T&gt;</c>):
        /// a placeholder that is never serialized, never top-level, and never reachable -- it exists
        /// only so AKKASG022 can fire when the definition implements a serializer protocol but has
        /// no registered closed constructions.
        /// </summary>
        public bool IsGenericDefinition { get; }

        /// <summary>
        /// For a registered closed construction: the arity-less fully-qualified name of its generic
        /// definition, linking the registration back to the definition for the AKKASG022 check.
        /// Empty for ordinary non-generic messages.
        /// </summary>
        public string DefinitionFullName { get; }

        /// <summary>
        /// Used by formatter resolution to swap in fields with a resolved <see cref="TypeMapping"/>.
        /// <see cref="ConstructionPlan"/> is keyed by field NAME, not by <see cref="FieldInfo"/>
        /// reference, so it stays valid across this substitution without needing to be rebuilt.
        /// </summary>
        public MessageInfo WithFields(ImmutableArray<FieldInfo> fields)
        {
            return new MessageInfo(SimpleName, FullyQualifiedName, Manifest, fields, Protocols, AllowEmpty, InvalidFields, ConstructionPlan, IsGenericDefinition, DefinitionFullName);
        }

        public bool Equals(MessageInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(SimpleName, other.SimpleName, StringComparison.Ordinal)
                && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(Manifest, other.Manifest, StringComparison.Ordinal)
                && AllowEmpty == other.AllowEmpty
                && IsGenericDefinition == other.IsGenericDefinition
                && string.Equals(DefinitionFullName, other.DefinitionFullName, StringComparison.Ordinal)
                && ConstructionPlan.Equals(other.ConstructionPlan)
                && ValueEquality.SequenceEquals(Fields, other.Fields)
                && ValueEquality.SequenceEquals(Protocols, other.Protocols)
                && ValueEquality.SequenceEquals(InvalidFields, other.InvalidFields);
        }

        public override bool Equals(object? obj) => Equals(obj as MessageInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, SimpleName);
            hash = ValueEquality.Combine(hash, FullyQualifiedName);
            hash = ValueEquality.Combine(hash, Manifest);
            hash = ValueEquality.Combine(hash, AllowEmpty);
            hash = ValueEquality.Combine(hash, IsGenericDefinition);
            hash = ValueEquality.Combine(hash, DefinitionFullName);
            hash = ValueEquality.Combine(hash, ConstructionPlan.GetHashCode());
            hash = ValueEquality.Combine(hash, Fields);
            hash = ValueEquality.Combine(hash, Protocols);
            hash = ValueEquality.Combine(hash, InvalidFields);
            return hash;
        }
    }

    /// <summary>
    /// A single <c>[AkkaField]</c> property found unusable during extraction: static, or its getter
    /// is not accessible to the generated code. See AKKASG028.
    /// </summary>
    internal sealed class InvalidFieldInfo : IEquatable<InvalidFieldInfo>
    {
        public InvalidFieldInfo(string propertyName, string reason)
        {
            PropertyName = propertyName;
            Reason = reason;
        }

        public string PropertyName { get; }

        /// <summary>Free-text reason, e.g. "is static; ..." or "has no accessible getter".</summary>
        public string Reason { get; }

        public bool Equals(InvalidFieldInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal)
                && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as InvalidFieldInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, PropertyName);
            hash = ValueEquality.Combine(hash, Reason);
            return hash;
        }
    }

    /// <summary>
    /// How a message's constructor is called on deserialize. <see cref="Arguments"/> supplies NAMED
    /// constructor arguments (parameter name -&gt; field name); <see cref="InitializerFieldNames"/>
    /// lists [AkkaField] properties assigned afterward via object initializer. Both are non-empty only
    /// when <see cref="IsValid"/>; otherwise <see cref="Errors"/> explains what could not be satisfied
    /// (AKKASG026). <see cref="UncoveredDefaultedParameters"/> is advisory (AKKASG027) and can be
    /// non-empty even when <see cref="IsValid"/> is true.
    /// </summary>
    internal sealed class ConstructionPlan : IEquatable<ConstructionPlan>
    {
        public static readonly ConstructionPlan Empty = new(
            ImmutableArray<ConstructorArgumentPlan>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);

        public ConstructionPlan(
            ImmutableArray<ConstructorArgumentPlan> arguments,
            ImmutableArray<string> initializerFieldNames,
            ImmutableArray<string> uncoveredDefaultedParameters,
            ImmutableArray<string> errors)
        {
            Arguments = arguments;
            InitializerFieldNames = initializerFieldNames;
            UncoveredDefaultedParameters = uncoveredDefaultedParameters;
            Errors = errors;
        }

        public ImmutableArray<ConstructorArgumentPlan> Arguments { get; }
        public ImmutableArray<string> InitializerFieldNames { get; }
        public ImmutableArray<string> UncoveredDefaultedParameters { get; }

        /// <summary>Human-readable reasons construction is impossible; empty when valid.</summary>
        public ImmutableArray<string> Errors { get; }

        public bool Equals(ConstructionPlan? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(Arguments, other.Arguments)
                && ValueEquality.SequenceEquals(InitializerFieldNames, other.InitializerFieldNames)
                && ValueEquality.SequenceEquals(UncoveredDefaultedParameters, other.UncoveredDefaultedParameters)
                && ValueEquality.SequenceEquals(Errors, other.Errors);
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructionPlan);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Arguments);
            hash = ValueEquality.Combine(hash, InitializerFieldNames);
            hash = ValueEquality.Combine(hash, UncoveredDefaultedParameters);
            hash = ValueEquality.Combine(hash, Errors);
            return hash;
        }
    }

    /// <summary>A single NAMED constructor argument: <see cref="ParameterName"/> supplied from the field named <see cref="FieldName"/>.</summary>
    internal readonly struct ConstructorArgumentPlan : IEquatable<ConstructorArgumentPlan>
    {
        public ConstructorArgumentPlan(string parameterName, string fieldName)
        {
            ParameterName = parameterName;
            FieldName = fieldName;
        }

        public string ParameterName { get; }
        public string FieldName { get; }

        public bool Equals(ConstructorArgumentPlan other)
        {
            return string.Equals(ParameterName, other.ParameterName, StringComparison.Ordinal)
                && string.Equals(FieldName, other.FieldName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is ConstructorArgumentPlan other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, ParameterName);
            hash = ValueEquality.Combine(hash, FieldName);
            return hash;
        }
    }

    /// <summary>
    /// A single <c>[AkkaSerializable&lt;T&gt;]</c> registration. <see cref="Message"/> is null
    /// when the target was invalid (not a type, non-generic, unbound, or its definition lacks
    /// <c>[AkkaSerializable]</c>) so AKKASG020 fires instead of the registration silently vanishing.
    /// </summary>
    internal sealed class ClosedGenericRegistrationInfo : IEquatable<ClosedGenericRegistrationInfo>
    {
        public ClosedGenericRegistrationInfo(string targetDisplayName, MessageInfo? message)
        {
            TargetDisplayName = targetDisplayName;
            Message = message;
        }

        public string TargetDisplayName { get; }
        public MessageInfo? Message { get; }

        public bool Equals(ClosedGenericRegistrationInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TargetDisplayName, other.TargetDisplayName, StringComparison.Ordinal)
                && Equals(Message, other.Message);
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedGenericRegistrationInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TargetDisplayName);
            hash = ValueEquality.Combine(hash, Message?.GetHashCode() ?? 0);
            return hash;
        }
    }

    internal sealed class FieldInfo : IEquatable<FieldInfo>
    {
        public FieldInfo(int index, string name, string typeFullName, TypeMapping mapping, bool isNullable, FormatterInfo? formatter = null, ImmutableArray<UnionMemberInfo> unionMembers = default, bool unionDeclaredOnObjectField = false)
        {
            Index = index;
            Name = name;
            TypeFullName = typeFullName;
            Mapping = mapping;
            IsNullable = isNullable;
            Formatter = formatter;
            UnionMembers = unionMembers.IsDefault ? ImmutableArray<UnionMemberInfo>.Empty : unionMembers;
            UnionDeclaredOnObjectField = unionDeclaredOnObjectField;
        }

        public int Index { get; }
        public string Name { get; }
        public string TypeFullName { get; }
        public TypeMapping Mapping { get; }
        public bool IsNullable { get; }
        public FormatterInfo? Formatter { get; }

        /// <summary>Declared members for a <see cref="FieldKind.Union"/> field; empty otherwise.</summary>
        public ImmutableArray<UnionMemberInfo> UnionMembers { get; }

        /// <summary>
        /// True when this is an object-typed (envelope-payload) field that ALSO carries a
        /// field-level [AkkaUnion]: contradictory author intent -- an object property is always the
        /// envelope boundary, so a union declaration on it can never take effect. Drives the
        /// AKKASG038 error.
        /// </summary>
        public bool UnionDeclaredOnObjectField { get; }

        public FieldInfo WithFormatter(TypeMapping mapping, FormatterInfo formatter)
        {
            return new FieldInfo(Index, Name, TypeFullName, mapping, IsNullable, formatter, UnionMembers, UnionDeclaredOnObjectField);
        }

        public bool Equals(FieldInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Index == other.Index
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && Mapping.Equals(other.Mapping)
                && IsNullable == other.IsNullable
                && UnionDeclaredOnObjectField == other.UnionDeclaredOnObjectField
                && Equals(Formatter, other.Formatter)
                && ValueEquality.SequenceEquals(UnionMembers, other.UnionMembers);
        }

        public override bool Equals(object? obj) => Equals(obj as FieldInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Index);
            hash = ValueEquality.Combine(hash, Name);
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, Mapping.GetHashCode());
            hash = ValueEquality.Combine(hash, IsNullable);
            hash = ValueEquality.Combine(hash, UnionDeclaredOnObjectField);
            hash = ValueEquality.Combine(hash, Formatter?.GetHashCode() ?? 0);
            hash = ValueEquality.Combine(hash, UnionMembers);
            return hash;
        }
    }

    internal readonly struct TypeMapping : IEquatable<TypeMapping>
    {
        public TypeMapping(
            FieldKind kind,
            string typeFullName = "",
            bool isValueType = false,
            string declaredTypeName = "",
            bool isNullable = false,
            ImmutableArray<TypeMapping> typeArguments = default,
            string enumUnderlyingTypeName = "",
            string foreignAssemblyName = "",
            bool suggestsEnvelopeOrUnion = false,
            bool isGenericConstruction = false)
        {
            Kind = kind;
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            DeclaredTypeName = declaredTypeName;
            IsNullable = isNullable;
            TypeArguments = typeArguments.IsDefault ? ImmutableArray<TypeMapping>.Empty : typeArguments;
            EnumUnderlyingTypeName = enumUnderlyingTypeName;
            ForeignAssemblyName = foreignAssemblyName;
            SuggestsEnvelopeOrUnion = suggestsEnvelopeOrUnion;
            IsGenericConstruction = isGenericConstruction;
        }

        public FieldKind Kind { get; }
        public string TypeFullName { get; }

        /// <summary>
        /// For <see cref="FieldKind.Object"/>: whether the annotated <c>[AkkaSerializable]</c> nested
        /// type is a value type (for example, a <c>readonly record struct</c>). Mirrors
        /// <see cref="FormatterInfo.IsTargetValueType"/>, which threads the same distinction for
        /// <see cref="FieldKind.Formatted"/> foreign-type formatter targets. Unused for every other kind.
        /// </summary>
        public bool IsValueType { get; }

        /// <summary>
        /// The exact fully-qualified C# type name (from <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>)
        /// used to declare read temporaries and construct collection instances when this mapping is a
        /// collection element/key/value. Populated only for mappings produced by <c>MapCollectionElement</c>.
        /// For a <c>Nullable&lt;T&gt;</c> value element it includes the trailing <c>?</c> (for example
        /// <c>int?</c>); for a reference element it is the non-nullable form.
        /// </summary>
        public string DeclaredTypeName { get; }

        /// <summary>
        /// For a collection element/key/value mapping: whether the element may be MessagePack <c>nil</c>.
        /// True for a <c>Nullable&lt;T&gt;</c> value element or a nullable-annotated reference element.
        /// Reference objects and nested collections are always nil-guarded regardless of this flag; it is
        /// only load-bearing for distinguishing <c>T?</c> from <c>T</c> among value-type elements.
        /// </summary>
        public bool IsNullable { get; }

        /// <summary>
        /// Child mappings for a collection kind: a single element mapping for every single-type-argument
        /// kind (<see cref="FieldKind.Array"/>, <see cref="FieldKind.List"/>, <see cref="FieldKind.ReadOnlyList"/>,
        /// <see cref="FieldKind.ReadOnlyCollection"/>, <see cref="FieldKind.ImmutableArray"/>,
        /// <see cref="FieldKind.ImmutableList"/>, <see cref="FieldKind.ImmutableHashSet"/>), and [key, value]
        /// for every key/value kind (<see cref="FieldKind.Dictionary"/>, <see cref="FieldKind.ReadOnlyDictionary"/>,
        /// <see cref="FieldKind.ImmutableDictionary"/>). Empty for every non-collection kind.
        /// </summary>
        public ImmutableArray<TypeMapping> TypeArguments { get; }

        /// <summary>
        /// For <see cref="FieldKind.UnsupportedEnumUnderlyingType"/>: the display name of the enum's
        /// underlying type (for example <c>long</c>), carried alongside <see cref="TypeFullName"/> (the
        /// enum itself) so AKKASG014 can name both. Empty for every other kind.
        /// </summary>
        public string EnumUnderlyingTypeName { get; }

        // For Object/MissingSerializableDefinition: the type's declaring assembly name, but only
        // when it is not the one this generator is producing output for. Empty otherwise. Drives
        // the AKKASG007 cross-assembly hint.
        public string ForeignAssemblyName { get; }

        // For Unsupported: whether the field's static type is an interface, abstract class, or type
        // parameter, the shapes a forgotten [AkkaUnion], or a field that should be typed object,
        // usually produce. Drives the AKKASG003 hint.
        public bool SuggestsEnvelopeOrUnion { get; }

        /// <summary>
        /// For <see cref="FieldKind.Object"/>: whether the type is a closed construction of a generic
        /// <c>[AkkaSerializable]</c> definition (for example <c>Wrapper&lt;int&gt;</c>), from
        /// <c>INamedTypeSymbol.IsGenericType</c>. Picks AKKASG023 versus AKKASG007 when the type is
        /// missing from this compilation's message table. False for every other kind.
        /// </summary>
        public bool IsGenericConstruction { get; }

        public TypeMapping WithTypeFullName(string typeFullName)
            => new(Kind, typeFullName, IsValueType, DeclaredTypeName, IsNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        public TypeMapping AsCollectionElement(string declaredTypeName, bool isNullable)
            => new(Kind, TypeFullName, IsValueType, declaredTypeName, isNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        // Explicit IEquatable implementation: the compiler-provided struct equality would compare
        // the TypeArguments ImmutableArray by underlying-array REFERENCE, breaking value equality
        // for every collection mapping (and with it, incremental caching of any model carrying one).
        public bool Equals(TypeMapping other)
        {
            return Kind == other.Kind
                && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && IsValueType == other.IsValueType
                && string.Equals(DeclaredTypeName, other.DeclaredTypeName, StringComparison.Ordinal)
                && IsNullable == other.IsNullable
                && string.Equals(EnumUnderlyingTypeName, other.EnumUnderlyingTypeName, StringComparison.Ordinal)
                && string.Equals(ForeignAssemblyName, other.ForeignAssemblyName, StringComparison.Ordinal)
                && SuggestsEnvelopeOrUnion == other.SuggestsEnvelopeOrUnion
                && IsGenericConstruction == other.IsGenericConstruction
                && ValueEquality.SequenceEquals(TypeArguments, other.TypeArguments);
        }

        public override bool Equals(object? obj) => obj is TypeMapping other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, (int)Kind);
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, IsValueType);
            hash = ValueEquality.Combine(hash, DeclaredTypeName);
            hash = ValueEquality.Combine(hash, IsNullable);
            hash = ValueEquality.Combine(hash, EnumUnderlyingTypeName);
            hash = ValueEquality.Combine(hash, ForeignAssemblyName);
            hash = ValueEquality.Combine(hash, SuggestsEnvelopeOrUnion);
            hash = ValueEquality.Combine(hash, IsGenericConstruction);
            hash = ValueEquality.Combine(hash, TypeArguments);
            return hash;
        }
    }

    /// <summary>
    /// A serializer-scoped hand-written formatter registration extracted from
    /// <c>[AkkaSerializerFormatter&lt;TTarget, TFormatter&gt;]</c>. Carries only
    /// strings/bools/enums (no <see cref="ISymbol"/> references) so it stays cheap to hold across
    /// incremental generator passes.
    /// </summary>
    internal sealed class FormatterInfo : IEquatable<FormatterInfo>
    {
        public FormatterInfo(string targetTypeFullName, bool isTargetValueType, string formatterTypeFullName, bool isAbstract, FormatterCtorKind ctorKind, bool isTargetSupported)
        {
            TargetTypeFullName = targetTypeFullName;
            IsTargetValueType = isTargetValueType;
            FormatterTypeFullName = formatterTypeFullName;
            IsAbstract = isAbstract;
            CtorKind = ctorKind;
            IsTargetSupported = isTargetSupported;
        }

        public string TargetTypeFullName { get; }
        public bool IsTargetValueType { get; }
        public string FormatterTypeFullName { get; }

        /// <summary>
        /// Whether TFormatter is an abstract type. The `where TFormatter :
        /// IAkkaMessagePackFormatter&lt;TTarget&gt;` constraint does not rule this out (there is no
        /// `new()` clause), so it is checked here instead (AKKASG008).
        /// </summary>
        public bool IsAbstract { get; }
        public FormatterCtorKind CtorKind { get; }
        public bool IsTargetSupported { get; }

        public bool Equals(FormatterInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TargetTypeFullName, other.TargetTypeFullName, StringComparison.Ordinal)
                && IsTargetValueType == other.IsTargetValueType
                && string.Equals(FormatterTypeFullName, other.FormatterTypeFullName, StringComparison.Ordinal)
                && IsAbstract == other.IsAbstract
                && CtorKind == other.CtorKind
                && IsTargetSupported == other.IsTargetSupported;
        }

        public override bool Equals(object? obj) => Equals(obj as FormatterInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TargetTypeFullName);
            hash = ValueEquality.Combine(hash, IsTargetValueType);
            hash = ValueEquality.Combine(hash, FormatterTypeFullName);
            hash = ValueEquality.Combine(hash, IsAbstract);
            hash = ValueEquality.Combine(hash, (int)CtorKind);
            hash = ValueEquality.Combine(hash, IsTargetSupported);
            return hash;
        }
    }

    internal enum FormatterCtorKind
    {
        None,
        Parameterless,
        System
    }

    internal enum FieldKind
    {
        Unsupported,
        String,
        ByteArray,
        Int32,
        Int64,
        Boolean,
        Double,
        Decimal,
        Guid,
        DateTime,
        DateTimeOffset,
        ActorRef,
        EnvelopePayload,
        Enum,
        Object,
        MissingSerializableDefinition,
        Formatted,
        Array,
        List,
        ReadOnlyList,
        Dictionary,
        UnsupportedEnumUnderlyingType,
        Union,
        ReadOnlyCollection,
        ReadOnlyDictionary,
        ImmutableArray,
        ImmutableList,
        ImmutableHashSet,
        ImmutableDictionary
    }

    /// <summary>
    /// A single declared member of an <c>[AkkaUnion]</c> field. Carries only strings/bools (no
    /// <see cref="ISymbol"/> references) so it stays cheap across incremental generator passes.
    /// Facts requiring symbol access (assignability, unbound-generic detection) are captured at
    /// extraction time; facts requiring the whole-compilation message set (serializability,
    /// manifests) are resolved later against the serializer's message dictionary.
    /// </summary>
    internal sealed class UnionMemberInfo : IEquatable<UnionMemberInfo>
    {
        public UnionMemberInfo(string typeFullName, bool isValueType, bool isAssignable, bool isSupported, bool isSealed, bool isAbstract, string foreignAssemblyName = "")
        {
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            IsAssignable = isAssignable;
            IsSupported = isSupported;
            IsSealed = isSealed;
            IsAbstract = isAbstract;
            ForeignAssemblyName = foreignAssemblyName;
        }

        /// <summary>Message-dictionary key for the member type (arity-aware for generics).</summary>
        public string TypeFullName { get; }

        public bool IsValueType { get; }

        /// <summary>Whether the member type is implicitly convertible to the field's static type.</summary>
        public bool IsAssignable { get; }

        /// <summary>False when the attribute argument was null, not a type, or an unbound generic.</summary>
        public bool IsSupported { get; }

        /// <summary>Whether undeclared subtypes are impossible (sealed class, struct). Advisory AKKASG025 fires otherwise -- unless the member is abstract, which escalates to AKKASG036.</summary>
        public bool IsSealed { get; }

        /// <summary>
        /// Whether the member type is abstract. Exact-runtime-type write dispatch can never select
        /// an abstract member (an abstract type is never a runtime type), so its dispatch branch is
        /// dead code. Advisory AKKASG036 (Warning) fires on it instead of AKKASG025.
        /// </summary>
        public bool IsAbstract { get; }

        // The member type's declaring assembly name, but only when it is not the compilation this
        // generator is producing output for. Empty otherwise. Drives the AKKASG015 cross-assembly hint.
        public string ForeignAssemblyName { get; }

        public bool Equals(UnionMemberInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && IsValueType == other.IsValueType
                && IsAssignable == other.IsAssignable
                && IsSupported == other.IsSupported
                && IsSealed == other.IsSealed
                && IsAbstract == other.IsAbstract
                && string.Equals(ForeignAssemblyName, other.ForeignAssemblyName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as UnionMemberInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, IsValueType);
            hash = ValueEquality.Combine(hash, IsAssignable);
            hash = ValueEquality.Combine(hash, IsSupported);
            hash = ValueEquality.Combine(hash, IsSealed);
            hash = ValueEquality.Combine(hash, IsAbstract);
            hash = ValueEquality.Combine(hash, ForeignAssemblyName);
            return hash;
        }
    }
}
