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

        /// <summary>
        /// Value equality for an <see cref="ImmutableDictionary{TKey,TValue}"/>-shaped cached model
        /// member (<see cref="ResolvedSerializer.ResolvedMessagesByType"/>): same key set, same value
        /// (by <see cref="IEquatable{T}"/>/<see cref="EqualityComparer{T}.Default"/>) for every key.
        /// <see cref="ImmutableDictionary{TKey,TValue}"/> has no such built-in equality of its own (its
        /// default <c>Equals</c> is reference equality on the underlying node), so every dictionary-typed
        /// model member must be compared through this helper instead of a bare <c>==</c>/<c>Equals</c>.
        /// Generic over the key type (<c>string</c> or <see cref="TypeKey"/>) so the same helper serves
        /// every dictionary-shaped model member regardless of key kind.
        /// </summary>
        public static bool DictionaryEquals<TKey, TValue>(ImmutableDictionary<TKey, TValue> left, ImmutableDictionary<TKey, TValue> right)
            where TKey : notnull
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left.Count != right.Count)
                return false;

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue) || !EqualityComparer<TValue>.Default.Equals(pair.Value, otherValue))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Order-independent hash companion to <see cref="DictionaryEquals{TKey,TValue}"/>: entries are
        /// combined with an order-insensitive operator (addition) so two dictionaries holding the same
        /// key/value pairs in a different enumeration order still hash equal, honoring the
        /// Equals/GetHashCode contract <see cref="DictionaryEquals{TKey,TValue}"/> establishes.
        /// </summary>
        public static int CombineDictionary<TKey, TValue>(int hash, ImmutableDictionary<TKey, TValue> dictionary)
            where TKey : notnull
        {
            hash = Combine(hash, dictionary.Count);

            var entriesHash = 0;
            foreach (var pair in dictionary)
            {
                var entryHash = Combine(Combine(Seed, EqualityComparer<TKey>.Default.GetHashCode(pair.Key)), pair.Value == null ? 0 : EqualityComparer<TValue>.Default.GetHashCode(pair.Value));
                unchecked
                {
                    entriesHash += entryHash;
                }
            }

            return Combine(hash, entriesHash);
        }

        /// <summary>
        /// Value equality for an <see cref="ImmutableDictionary{TKey,TValue}"/>-shaped cached model
        /// member whose VALUE is itself an <see cref="ImmutableArray{T}"/> (<see cref="CompilationFacts"/>'s
        /// two implementor-by-protocol maps): same key set, and for each key, the same ORDERED
        /// element sequence via <see cref="SequenceEquals{T}"/> -- deliberately NOT
        /// <see cref="DictionaryEquals{TKey,TValue}"/>'s <c>EqualityComparer&lt;TValue&gt;.Default</c>,
        /// which for <c>TValue</c> = <see cref="ImmutableArray{T}"/> would resolve to
        /// <see cref="ImmutableArray{T}"/>'s OWN <see cref="IEquatable{T}"/> implementation --
        /// reference equality on the wrapped array, not a structural compare -- and would wrongly
        /// treat two independently-built arrays with identical content as unequal.
        /// </summary>
        public static bool ArrayDictionaryEquals<TKey, TValue>(ImmutableDictionary<TKey, ImmutableArray<TValue>> left, ImmutableDictionary<TKey, ImmutableArray<TValue>> right)
            where TKey : notnull
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left.Count != right.Count)
                return false;

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue) || !SequenceEquals(pair.Value, otherValue))
                    return false;
            }

            return true;
        }

        /// <summary>Order-independent hash companion to <see cref="ArrayDictionaryEquals{TKey,TValue}"/>, mirroring <see cref="CombineDictionary{TKey,TValue}"/>.</summary>
        public static int CombineArrayDictionary<TKey, TValue>(int hash, ImmutableDictionary<TKey, ImmutableArray<TValue>> dictionary)
            where TKey : notnull
        {
            hash = Combine(hash, dictionary.Count);

            var entriesHash = 0;
            foreach (var pair in dictionary)
            {
                var entryHash = Combine(Combine(Seed, EqualityComparer<TKey>.Default.GetHashCode(pair.Key)), pair.Value);
                unchecked
                {
                    entriesHash += entryHash;
                }
            }

            return Combine(hash, entriesHash);
        }
    }

    /// <summary>
    /// A type's identity for every dictionary key and equality check in the pipeline, replacing the
    /// former scheme of keying off a type's own fully-qualified DISPLAY name (a plain <c>string</c>).
    /// <see cref="MetadataName"/> is built from exactly what <see cref="INamedTypeSymbol"/> exposes
    /// through its own <see cref="ISymbol.MetadataName"/> (the CLR metadata name, arity suffix
    /// included -- e.g. <c>Wrapper`1</c>) walked up its <see cref="INamedTypeSymbol.ContainingType"/>
    /// chain joined by <c>+</c> (matching real CLR nested-type metadata names), then its containing
    /// namespace joined by <c>.</c>. Two types whose DISPLAY STRING happens to render identically --
    /// a nested type <c>A.Outer+Inner</c> and an unrelated namespace-qualified type <c>A.Outer.Inner</c>
    /// both render as <c>"A.Outer.Inner"</c> -- no longer collide as a dictionary key: their
    /// <see cref="MetadataName"/>s differ ("A.Outer+Inner" vs "A.Outer.Inner"). <see cref="TypeArguments"/>
    /// carries the same key, recursively, for each of a generic construction's own type arguments, so
    /// <c>Wrapper&lt;int&gt;</c> and <c>Wrapper&lt;string&gt;</c> never collide even though they share
    /// one <see cref="MetadataName"/>.
    /// <see cref="DisplayName"/> is carried verbatim -- the EXACT string the generator has always
    /// rendered for this type -- so emitted text and diagnostics do not move, but it plays NO part in
    /// <see cref="Equals(TypeKey)"/>/<see cref="GetHashCode"/>: two keys with the same metadata
    /// identity are equal regardless of what (if anything) their <see cref="DisplayName"/> carries.
    /// That is what lets a purely comparison-scoped key skip building a display string entirely (see
    /// <see cref="FromSymbol"/>'s <c>includeDisplayName</c> parameter, used by the AKKASG029 protocol
    /// coverage scan to avoid formatting every interface of every candidate type just to test it
    /// against the serializer's own protocol type).
    /// </summary>
    internal readonly struct TypeKey : IEquatable<TypeKey>
    {
        public TypeKey(string metadataName, ImmutableArray<TypeKey> typeArguments, string displayName)
        {
            MetadataName = metadataName;
            TypeArguments = typeArguments.IsDefault ? ImmutableArray<TypeKey>.Empty : typeArguments;
            DisplayName = displayName;
        }

        /// <summary>The CLR metadata name (arity suffix included), containing types joined by '+', containing namespace joined by '.'.</summary>
        public string MetadataName { get; }

        /// <summary>Value-equal child keys for a generic construction's own type arguments; empty for a non-generic type.</summary>
        public ImmutableArray<TypeKey> TypeArguments { get; }

        /// <summary>The exact fully-qualified display string the generator has always rendered for this type. Carried verbatim; never compared.</summary>
        public string DisplayName { get; }

        /// <summary>The compact, collision-checked (AKKASG024) generated-member-name folding of <see cref="DisplayName"/>. See <see cref="FoldTypeName"/>.</summary>
        public string Fold() => FoldTypeName(DisplayName ?? string.Empty);

        public override string ToString() => DisplayName ?? string.Empty;

        // Equality is METADATA-based only: MetadataName plus TypeArguments. DisplayName is carried for
        // rendering, never compared -- see the type's own doc comment for why that split is what
        // closes the nested-vs-dotted collision and lets a display-free comparison key exist at all.
        public bool Equals(TypeKey other)
        {
            return string.Equals(MetadataName, other.MetadataName, StringComparison.Ordinal)
                && ValueEquality.SequenceEquals(TypeArguments, other.TypeArguments);
        }

        public override bool Equals(object? obj) => obj is TypeKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, MetadataName);
            hash = ValueEquality.Combine(hash, TypeArguments);
            return hash;
        }

        /// <summary>
        /// Builds a key from a live symbol -- the only place a <see cref="TypeKey"/> is ever produced,
        /// since extraction is the only phase with symbols. <paramref name="includeDisplayName"/> is
        /// false only for a transient, single-compilation-pass comparison that never crosses an
        /// incremental boundary and never needs a display string of its own: the AKKASG029 protocol
        /// coverage scan builds a throwaway key per candidate interface purely to test it against the
        /// serializer's own protocol key, and skipping <see cref="ISymbol.ToDisplayString(SymbolDisplayFormat)"/>
        /// there is exactly the saving that replaces the old "format every interface of every type" scan.
        /// </summary>
        public static TypeKey FromSymbol(ITypeSymbol type, bool includeDisplayName = true)
        {
            if (type is INamedTypeSymbol named)
            {
                var metadataName = BuildMetadataName(named);

                if (!named.IsGenericType)
                {
                    // Non-generic (the overwhelming common case): GetFullyQualifiedTypeName's own
                    // dot-joined, arity-less text is IDENTICAL to ToDisplayString(FullyQualifiedFormat)
                    // for any non-generic type, nested or not -- both are "global::" plus the
                    // namespace plus each containing type's simple name, dot-joined; a generic type's
                    // OWN type-parameter list is the only thing ToDisplayString would add that this
                    // does not produce, and this branch never runs for one. Reusing it here avoids
                    // paying for a second, independent Roslyn symbol-display pass just to populate
                    // DisplayName, on top of the walk BuildMetadataName already did.
                    var displayName = includeDisplayName ? GetFullyQualifiedTypeName(named) : string.Empty;
                    return new TypeKey(metadataName, ImmutableArray<TypeKey>.Empty, displayName);
                }

                var genericDisplayName = includeDisplayName ? named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : string.Empty;
                var typeArguments = BuildTypeArguments(named, includeDisplayName);
                return new TypeKey(metadataName, typeArguments, genericDisplayName);
            }

            // A type argument that is not itself a named type (array, pointer, type parameter,
            // dynamic, ...) has no CLR metadata name of its own to key on; its identity degrades to
            // its display string, exactly as it already did before this type existed -- these shapes
            // were never distinguishable from their own display string in the first place.
            var fallbackName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new TypeKey(fallbackName, ImmutableArray<TypeKey>.Empty, includeDisplayName ? fallbackName : string.Empty);
        }

        private static ImmutableArray<TypeKey> BuildTypeArguments(INamedTypeSymbol symbol, bool includeDisplayName)
        {
            var builder = ImmutableArray.CreateBuilder<TypeKey>(symbol.TypeArguments.Length);
            foreach (var argument in symbol.TypeArguments)
                builder.Add(FromSymbol(argument, includeDisplayName));

            return builder.MoveToImmutable();
        }

        private static string BuildMetadataName(INamedTypeSymbol symbol)
        {
            // Fast path: a non-nested type -- by far the common case -- needs no containing-type
            // walk (or Stack allocation) at all; the general path below produces the identical
            // string for this shape too, just at needless extra cost.
            if (symbol.ContainingType == null)
            {
                var ns0 = GetNamespace(symbol);
                return ns0.Length == 0 ? symbol.MetadataName : ns0 + "." + symbol.MetadataName;
            }

            var parts = new Stack<string>();
            INamedTypeSymbol? current = symbol;
            while (current != null)
            {
                parts.Push(current.MetadataName);
                current = current.ContainingType;
            }

            var ns = GetNamespace(symbol);
            var nested = string.Join("+", parts);
            return string.IsNullOrEmpty(ns) ? nested : ns + "." + nested;
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
            TypeKey protocolTypeKey,
            bool protocolTypeIsInterface,
            Accessibility declaredAccessibility,
            ImmutableArray<FormatterInfo> formatters,
            ImmutableArray<ClosedGenericRegistrationInfo> closedGenericRegistrations,
            ImmutableArray<MessageInfo> closedGenericSchemas,
            bool isPartial,
            bool isGeneric,
            bool derivesFromAkkaSerializerBase)
        {
            Namespace = ns;
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            SerializerId = serializerId;
            ProtocolTypeKey = protocolTypeKey;
            ProtocolTypeIsInterface = protocolTypeIsInterface;
            DeclaredAccessibility = declaredAccessibility;
            Formatters = formatters;
            ClosedGenericRegistrations = closedGenericRegistrations;
            ClosedGenericSchemas = closedGenericSchemas.IsDefault ? ImmutableArray<MessageInfo>.Empty : closedGenericSchemas;
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
        /// Key of the <c>[AkkaSerializer&lt;TProtocol&gt;]</c> type argument; default when the type
        /// argument was not a named type. All protocol matching runs on this key -- the symbol itself
        /// is never retained.
        /// </summary>
        public TypeKey ProtocolTypeKey { get; }

        /// <summary>
        /// Fully-qualified display name of the protocol type, derived from <see cref="ProtocolTypeKey"/>
        /// -- the single source of truth for anywhere this is read for display or ordinal string
        /// matching against <see cref="MessageInfo.Protocols"/>. Empty when the type argument was not
        /// a named type.
        /// </summary>
        public string ProtocolTypeFullName => ProtocolTypeKey.DisplayName ?? string.Empty;

        /// <summary>Whether the protocol type argument is an interface. See AKKASG033.</summary>
        public bool ProtocolTypeIsInterface { get; }

        public Accessibility DeclaredAccessibility { get; }
        public ImmutableArray<FormatterInfo> Formatters { get; }

        /// <summary>
        /// This serializer's <c>[AkkaSerializable&lt;T&gt;]</c> registrations, as light specs: what to
        /// target, its manifest, and whether an empty schema is allowed. The full extracted schema for
        /// each VALID registration lives beside this array, in <see cref="ClosedGenericSchemas"/>, keyed
        /// the same way as every other message -- not embedded inside the registration itself.
        /// </summary>
        public ImmutableArray<ClosedGenericRegistrationInfo> ClosedGenericRegistrations { get; }

        /// <summary>
        /// The full <see cref="MessageInfo"/> for each VALID closed-generic registration's construction,
        /// extracted once at extraction time (the only phase with symbols) through the same
        /// <c>ExtractMessageCore</c> routine every other schema goes through. One entry per valid
        /// registration in <see cref="ClosedGenericRegistrations"/> (invalid registrations have no
        /// entry here); the resolve stage folds these into its own schema table alongside every
        /// declared message.
        /// </summary>
        public ImmutableArray<MessageInfo> ClosedGenericSchemas { get; }

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
                && ProtocolTypeKey.Equals(other.ProtocolTypeKey)
                && ProtocolTypeIsInterface == other.ProtocolTypeIsInterface
                && DeclaredAccessibility == other.DeclaredAccessibility
                && IsPartial == other.IsPartial
                && IsGeneric == other.IsGeneric
                && DerivesFromAkkaSerializerBase == other.DerivesFromAkkaSerializerBase
                && ValueEquality.SequenceEquals(Formatters, other.Formatters)
                && ValueEquality.SequenceEquals(ClosedGenericRegistrations, other.ClosedGenericRegistrations)
                && ValueEquality.SequenceEquals(ClosedGenericSchemas, other.ClosedGenericSchemas);
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
            hash = ValueEquality.Combine(hash, ProtocolTypeKey.GetHashCode());
            hash = ValueEquality.Combine(hash, ProtocolTypeIsInterface);
            hash = ValueEquality.Combine(hash, (int)DeclaredAccessibility);
            hash = ValueEquality.Combine(hash, IsPartial);
            hash = ValueEquality.Combine(hash, IsGeneric);
            hash = ValueEquality.Combine(hash, DerivesFromAkkaSerializerBase);
            hash = ValueEquality.Combine(hash, Formatters);
            hash = ValueEquality.Combine(hash, ClosedGenericRegistrations);
            hash = ValueEquality.Combine(hash, ClosedGenericSchemas);
            return hash;
        }
    }

    internal sealed class MessageInfo : IEquatable<MessageInfo>
    {
        public MessageInfo(
            string simpleName,
            TypeKey key,
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
            Key = key;
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

        /// <summary>This message's own type key -- the dictionary key every message table in the pipeline is keyed by.</summary>
        public TypeKey Key { get; }

        /// <summary>Fully-qualified display name of this message's type, derived from <see cref="Key"/> -- the single source of truth for display and emission.</summary>
        public string FullyQualifiedName => Key.DisplayName ?? string.Empty;

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
            return new MessageInfo(SimpleName, Key, Manifest, fields, Protocols, AllowEmpty, InvalidFields, ConstructionPlan, IsGenericDefinition, DefinitionFullName);
        }

        public bool Equals(MessageInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(SimpleName, other.SimpleName, StringComparison.Ordinal)
                && Key.Equals(other.Key)
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
            hash = ValueEquality.Combine(hash, Key.GetHashCode());
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
    /// A single <c>[AkkaSerializable&lt;T&gt;]</c> registration, as a light spec: what to target, its
    /// manifest, and whether an empty schema is allowed -- nothing else. The full schema for a VALID
    /// target lives beside this registration, in <see cref="SerializerInfo.ClosedGenericSchemas"/>,
    /// keyed by <see cref="Target"/> the same way as every other message; a target with no matching
    /// entry there (not a type, non-generic, unbound, or its definition lacks <c>[AkkaSerializable]</c>)
    /// is invalid, so AKKASG020 fires instead of the registration silently vanishing.
    /// </summary>
    internal sealed class ClosedGenericRegistrationInfo : IEquatable<ClosedGenericRegistrationInfo>
    {
        public ClosedGenericRegistrationInfo(TypeKey target, string manifest, bool allowEmpty)
        {
            Target = target;
            Manifest = manifest;
            AllowEmpty = allowEmpty;
        }

        public TypeKey Target { get; }

        /// <summary>Display name of <see cref="Target"/>, derived -- read only for diagnostics.</summary>
        public string TargetDisplayName => Target.DisplayName ?? string.Empty;

        public string Manifest { get; }
        public bool AllowEmpty { get; }

        public bool Equals(ClosedGenericRegistrationInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Target.Equals(other.Target)
                && string.Equals(Manifest, other.Manifest, StringComparison.Ordinal)
                && AllowEmpty == other.AllowEmpty;
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedGenericRegistrationInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Target.GetHashCode());
            hash = ValueEquality.Combine(hash, Manifest);
            hash = ValueEquality.Combine(hash, AllowEmpty);
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
            TypeKey key = default,
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
            Key = key;
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

        /// <summary>This mapping's type key, for a kind that names a type (Object, Formatted, Enum, MissingSerializableDefinition, UnsupportedEnumUnderlyingType); default for every scalar/collection kind.</summary>
        public TypeKey Key { get; }

        /// <summary>Fully-qualified display name derived from <see cref="Key"/> -- the single source of truth for display, emission, and dictionary-key matching by display text.</summary>
        public string TypeFullName => Key.DisplayName ?? string.Empty;

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

        public TypeMapping WithKey(TypeKey key)
            => new(Kind, key, IsValueType, DeclaredTypeName, IsNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        public TypeMapping AsCollectionElement(string declaredTypeName, bool isNullable)
            => new(Kind, Key, IsValueType, declaredTypeName, isNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        // Explicit IEquatable implementation: the compiler-provided struct equality would compare
        // the TypeArguments ImmutableArray by underlying-array REFERENCE, breaking value equality
        // for every collection mapping (and with it, incremental caching of any model carrying one).
        public bool Equals(TypeMapping other)
        {
            return Kind == other.Kind
                && Key.Equals(other.Key)
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
            hash = ValueEquality.Combine(hash, Key.GetHashCode());
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
        public FormatterInfo(TypeKey targetTypeKey, bool isTargetValueType, string formatterTypeFullName, bool isAbstract, FormatterCtorKind ctorKind, bool isTargetSupported)
        {
            TargetTypeKey = targetTypeKey;
            IsTargetValueType = isTargetValueType;
            FormatterTypeFullName = formatterTypeFullName;
            IsAbstract = isAbstract;
            CtorKind = ctorKind;
            IsTargetSupported = isTargetSupported;
        }

        /// <summary>Key of the formatter's target type -- what the formatter dictionary in <c>ResolveMessages</c> is keyed by.</summary>
        public TypeKey TargetTypeKey { get; }

        /// <summary>Fully-qualified display name of the target type, derived from <see cref="TargetTypeKey"/> -- read only for diagnostics and the generated formatter field name.</summary>
        public string TargetTypeFullName => TargetTypeKey.DisplayName ?? string.Empty;

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

            return TargetTypeKey.Equals(other.TargetTypeKey)
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
            hash = ValueEquality.Combine(hash, TargetTypeKey.GetHashCode());
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
        public UnionMemberInfo(TypeKey key, bool isValueType, bool isAssignable, bool isSupported, bool isSealed, bool isAbstract, string foreignAssemblyName = "")
        {
            Key = key;
            IsValueType = isValueType;
            IsAssignable = isAssignable;
            IsSupported = isSupported;
            IsSealed = isSealed;
            IsAbstract = isAbstract;
            ForeignAssemblyName = foreignAssemblyName;
        }

        /// <summary>Message-dictionary key for the member type.</summary>
        public TypeKey Key { get; }

        /// <summary>Fully-qualified display name of the member type, derived from <see cref="Key"/>.</summary>
        public string TypeFullName => Key.DisplayName ?? string.Empty;

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

            return Key.Equals(other.Key)
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
            hash = ValueEquality.Combine(hash, Key.GetHashCode());
            hash = ValueEquality.Combine(hash, IsValueType);
            hash = ValueEquality.Combine(hash, IsAssignable);
            hash = ValueEquality.Combine(hash, IsSupported);
            hash = ValueEquality.Combine(hash, IsSealed);
            hash = ValueEquality.Combine(hash, IsAbstract);
            hash = ValueEquality.Combine(hash, ForeignAssemblyName);
            return hash;
        }
    }

    /// <summary>
    /// Identifies exactly one <see cref="DiagnosticDescriptor"/> field declared in
    /// AkkaSerializerGenerator.Diagnostics.cs -- by DESCRIPTOR FIELD, not by public diagnostic id.
    /// Key scheme: three ids are each backed by TWO distinct descriptor fields with the same
    /// id/title/severity but different message text (AKKASG003: plain vs. the polymorphic-hint
    /// variant for an interface/abstract/type-parameter field; AKKASG007 and AKKASG015: same-assembly
    /// vs. the cross-assembly-hint variant), so the public id alone cannot tell a
    /// <see cref="DiagnosticSpec"/> apart from its sibling variant. Every member below is named
    /// IDENTICALLY to the descriptor field it resolves to (see the private DiagnosticRegistry in
    /// AkkaSerializerGenerator.Diagnostics.cs), so the 1:1 mapping is obvious at both ends.
    /// </summary>
    internal enum DiagnosticKey
    {
        InvalidSerializerName,
        InvalidSerializerId,
        UnsupportedFieldType,
        UnsupportedFieldTypePolymorphic,
        MissingFields,
        DuplicateFieldIndex,
        MissingManifest,
        MissingNestedSerializableDefinition,
        MissingNestedSerializableDefinitionCrossAssembly,
        InvalidFormatterType,
        DuplicateFormatterRegistration,
        FormatterConstructorNotUsable,
        FormatterTargetNotSupported,
        DuplicateManifest,
        DuplicateSerializerId,
        UnsupportedEnumUnderlyingType,
        UnionMemberNotSerializable,
        UnionMemberNotSerializableCrossAssembly,
        UnionMemberMissingManifest,
        UnionMemberManifestCollision,
        UnionMemberNotAssignable,
        InvalidUnionMemberSet,
        InvalidClosedGenericRegistration,
        DuplicateClosedGenericRegistration,
        GenericSerializableRequiresRegistration,
        UnregisteredClosedGenericField,
        DuplicateGeneratedName,
        UnionMemberNotSealed,
        NoMatchingConstructor,
        ConstructorParameterNotCovered,
        FieldPropertyNotAccessible,
        ProtocolMessageNotSerializable,
        DuplicateProtocolBinding,
        InvalidSerializerShape,
        ProtocolTypeMustBeInterface,
        ClosedGenericRegistrationNotInProtocol,
        UnionMemberAbstract,
        ManifestIgnoredOnGenericDefinition,
        UnionDeclaredOnObjectField
    }

    /// <summary>
    /// A diagnostic to report, with no live <see cref="Diagnostic"/>, <see cref="Location"/>, or
    /// symbol reference: just <see cref="Key"/> (which <see cref="DiagnosticDescriptor"/> field --
    /// see <see cref="DiagnosticKey"/>) and the already display-formatted message arguments (a
    /// numeric argument, e.g. AKKASG002's serializer id or AKKASG005's field index, is converted to
    /// its decimal string ahead of time, since <see cref="MessageArgs"/> is homogeneous). Pure
    /// validation functions in AkkaSerializerGenerator.Validation.cs return these instead of calling
    /// <c>SourceProductionContext.ReportDiagnostic</c> directly, so validation runs -- and can be
    /// asserted against directly, by <see cref="Key"/> and <see cref="MessageArgs"/> rather than by
    /// message substring -- with no driver, context, or <see cref="Compilation"/> at all. The private
    /// DiagnosticRegistry in AkkaSerializerGenerator.Diagnostics.cs is the one place a
    /// <see cref="DiagnosticSpec"/> is turned into a real <see cref="Diagnostic"/>, always at
    /// <see cref="Location.None"/> (every diagnostic this generator has ever reported already was).
    /// </summary>
    internal sealed class DiagnosticSpec : IEquatable<DiagnosticSpec>
    {
        public DiagnosticSpec(DiagnosticKey key, params string[] messageArgs)
        {
            Key = key;
            MessageArgs = messageArgs.Length == 0 ? ImmutableArray<string>.Empty : ImmutableArray.Create(messageArgs);
        }

        public DiagnosticKey Key { get; }
        public ImmutableArray<string> MessageArgs { get; }

        public bool Equals(DiagnosticSpec? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Key == other.Key && ValueEquality.SequenceEquals(MessageArgs, other.MessageArgs);
        }

        public override bool Equals(object? obj) => Equals(obj as DiagnosticSpec);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, (int)Key);
            hash = ValueEquality.Combine(hash, MessageArgs);
            return hash;
        }
    }

    /// <summary>
    /// One member of a <see cref="ClosedSet"/>: everything a top-level dispatch switch or a union
    /// write/read/size helper needs to name and call into ONE message's generated methods --
    /// nothing else. Reduced from a full <see cref="MessageInfo"/> (which also carries every field,
    /// irrelevant to dispatch) so a <see cref="ClosedSet"/> stays cheap to hold inside a cached
    /// <see cref="ResolvedSerializer"/>. <see cref="MethodName"/> is the same
    /// <see cref="GetMessageMethodName"/> result the corresponding <c>Write&lt;name&gt;</c>/
    /// <c>Read&lt;name&gt;</c>/<c>SizeOf&lt;name&gt;</c> methods use, computed once so every dispatch
    /// site (top-level or union) agrees on it without recomputing <see cref="FoldTypeName"/>.
    /// </summary>
    internal sealed class ClosedSetMember : IEquatable<ClosedSetMember>
    {
        public ClosedSetMember(TypeKey key, string manifest, string methodName)
        {
            Key = key;
            Manifest = manifest;
            MethodName = methodName;
        }

        public TypeKey Key { get; }

        /// <summary>Fully-qualified display name of this member's type, derived from <see cref="Key"/> -- read only for emission.</summary>
        public string TypeFullName => Key.DisplayName ?? string.Empty;

        public string Manifest { get; }
        public string MethodName { get; }

        public bool Equals(ClosedSetMember? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Key.Equals(other.Key)
                && string.Equals(Manifest, other.Manifest, StringComparison.Ordinal)
                && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedSetMember);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Key.GetHashCode());
            hash = ValueEquality.Combine(hash, Manifest);
            hash = ValueEquality.Combine(hash, MethodName);
            return hash;
        }
    }

    /// <summary>
    /// An ORDERED, closed set of <see cref="ClosedSetMember"/>s -- today, a serializer's top-level
    /// dispatch set (<see cref="ResolvedSerializer.TopLevelMessages"/>) and one union field's
    /// declared member set (<see cref="UnionHelperPlan.Members"/>). Order is significant and
    /// preserved exactly as the pipeline has always produced it (declaration order for top-level
    /// messages, [AkkaUnion] declaration order for union members) -- emitted switch/dispatch text is
    /// ordered the same way, so re-ordering here would change emitted output. This is Decision 18's
    /// single representation for "a closed, explicitly-enumerated set of message types": later work
    /// on registration expansion is expected to build its own <see cref="ClosedSet"/>s the same way.
    /// </summary>
    internal sealed class ClosedSet : IEquatable<ClosedSet>
    {
        public static readonly ClosedSet Empty = new(ImmutableArray<ClosedSetMember>.Empty);

        public ClosedSet(ImmutableArray<ClosedSetMember> members)
        {
            Members = members;
        }

        public ImmutableArray<ClosedSetMember> Members { get; }

        public bool Equals(ClosedSet? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(Members, other.Members);
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedSet);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Members);
            return hash;
        }
    }

    /// <summary>
    /// One planned union dispatch helper (a Write/Read/SizeOf trio): its dedup identity
    /// (<see cref="Signature"/>, from <see cref="BuildUnionSignature"/>), its generated method-name
    /// suffix (<see cref="HelperName"/>), the union field's static type
    /// (<see cref="FieldTypeFullName"/> -- the helper trio's shared parameter/return type), and its
    /// declared member set already resolved to a <see cref="ClosedSet"/> (see
    /// <see cref="PlanUnionHelpers"/>) so generation never needs to re-consult the message dictionary.
    /// </summary>
    internal sealed class UnionHelperPlan : IEquatable<UnionHelperPlan>
    {
        public UnionHelperPlan(string signature, string helperName, string fieldTypeFullName, ClosedSet members)
        {
            Signature = signature;
            HelperName = helperName;
            FieldTypeFullName = fieldTypeFullName;
            Members = members;
        }

        /// <summary>The union's dedup identity: static type plus ordered member set. See <see cref="BuildUnionSignature"/>.</summary>
        public string Signature { get; }

        public string HelperName { get; }
        public string FieldTypeFullName { get; }
        public ClosedSet Members { get; }

        public bool Equals(UnionHelperPlan? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(Signature, other.Signature, StringComparison.Ordinal)
                && string.Equals(HelperName, other.HelperName, StringComparison.Ordinal)
                && string.Equals(FieldTypeFullName, other.FieldTypeFullName, StringComparison.Ordinal)
                && Members.Equals(other.Members);
        }

        public override bool Equals(object? obj) => Equals(obj as UnionHelperPlan);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Signature);
            hash = ValueEquality.Combine(hash, HelperName);
            hash = ValueEquality.Combine(hash, FieldTypeFullName);
            hash = ValueEquality.Combine(hash, Members.GetHashCode());
            return hash;
        }
    }

    /// <summary>
    /// One serializer's full union dispatch plan: every distinct union helper it needs to generate,
    /// ALREADY ordered by <see cref="UnionHelperPlan.HelperName"/> (the order
    /// <see cref="GenerateUnionHelpers"/> emits them in) -- replaces the pipeline's former
    /// string-keyed <c>ImmutableDictionary&lt;string, (string HelperName, FieldInfo Field)&gt;</c>,
    /// which was neither a named model nor cheaply value-equatable (a <see cref="FieldInfo"/> per
    /// entry pulled in every field of some arbitrary representative message).
    /// </summary>
    internal sealed class UnionPlan : IEquatable<UnionPlan>
    {
        public static readonly UnionPlan Empty = new(ImmutableArray<UnionHelperPlan>.Empty);

        public UnionPlan(ImmutableArray<UnionHelperPlan> helpers)
        {
            Helpers = helpers;
        }

        /// <summary>Ordered by <see cref="UnionHelperPlan.HelperName"/>. See <see cref="PlanUnionHelpers"/>.</summary>
        public ImmutableArray<UnionHelperPlan> Helpers { get; }

        public bool Equals(UnionPlan? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(Helpers, other.Helpers);
        }

        public override bool Equals(object? obj) => Equals(obj as UnionPlan);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Helpers);
            return hash;
        }
    }

    /// <summary>
    /// One serializer, fully resolved: the cached, value-equatable output of
    /// <see cref="ResolveSerializer"/>, and the pipeline's terminal per-serializer model --
    /// <see cref="EmitResolvedSerializer"/> reports its diagnostics and emits its source, PURELY from
    /// this type, with no further symbol/<see cref="Compilation"/> access. Two resolves over equal
    /// inputs produce an EQUAL <see cref="ResolvedSerializer"/> (see <see cref="GeneratorResolveSpec"/>),
    /// which is exactly what lets an unrelated serializer's <c>RegisterSourceOutput</c> report
    /// <see cref="Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached"/> instead of re-emitting
    /// when only ANOTHER serializer's message changed.
    /// </summary>
    /// <remarks>
    /// When <see cref="IsEmittable"/> is false (the serializer's own declaration failed
    /// <see cref="EvaluateGate"/>), every field below <see cref="GateDiagnostics"/> is empty/default:
    /// a serializer that never clears the gate has no message table to resolve in the first place --
    /// see <see cref="NotEmittable"/>.
    /// </remarks>
    internal sealed class ResolvedSerializer : IEquatable<ResolvedSerializer>
    {
        public ResolvedSerializer(
            SerializerInfo serializer,
            bool isEmittable,
            ImmutableArray<DiagnosticSpec> gateDiagnostics,
            ImmutableDictionary<TypeKey, MessageInfo> resolvedMessagesByType,
            ClosedSet topLevelMessages,
            ImmutableArray<MessageInfo> reachableMessages,
            ImmutableDictionary<TypeKey, MessageInfo> closedGenericSchemas,
            ImmutableArray<FormatterInfo> usedFormatters,
            UnionPlan unionPlan,
            ImmutableArray<DiagnosticSpec> validationDiagnostics)
        {
            Serializer = serializer;
            IsEmittable = isEmittable;
            GateDiagnostics = gateDiagnostics;
            ResolvedMessagesByType = resolvedMessagesByType;
            TopLevelMessages = topLevelMessages;
            ReachableMessages = reachableMessages;
            ClosedGenericSchemas = closedGenericSchemas;
            UsedFormatters = usedFormatters;
            UnionPlan = unionPlan;
            ValidationDiagnostics = validationDiagnostics;
        }

        /// <summary>The serializer declaration this model resolves. Never null, even when <see cref="IsEmittable"/> is false.</summary>
        public SerializerInfo Serializer { get; }

        /// <summary>The result of <see cref="EvaluateGate"/>: whether this serializer's own declaration is usable as a codegen target at all.</summary>
        public bool IsEmittable { get; }

        /// <summary>Diagnostics from <see cref="EvaluateGate"/>. Reported unconditionally (gate diagnostics fire whether or not the gate itself passes).</summary>
        public ImmutableArray<DiagnosticSpec> GateDiagnostics { get; }

        /// <summary>Every message this serializer knows about, with formatters resolved. Empty when <see cref="IsEmittable"/> is false.</summary>
        public ImmutableDictionary<TypeKey, MessageInfo> ResolvedMessagesByType { get; }

        /// <summary>This serializer's top-level dispatch set (Manifest/Serialize/Deserialize/SizeHint switches), in declaration order.</summary>
        public ClosedSet TopLevelMessages { get; }

        /// <summary>Every message reachable from a top-level message -- the ones that need generated Write/Read/SizeOf methods.</summary>
        public ImmutableArray<MessageInfo> ReachableMessages { get; }

        /// <summary>
        /// This serializer's own closed-generic registrations, resolved: <see cref="SerializerInfo.ClosedGenericSchemas"/>
        /// with formatters substituted, keyed by <see cref="TypeKey"/> -- the one schema table for this
        /// serializer's own registrations (see <see cref="ResolveClosedGenericSchemas"/>). Built the
        /// same SELF-SCOPED way as <see cref="UsedFormatters"/>: it depends only on this serializer's
        /// own <see cref="SerializerInfo.ClosedGenericSchemas"/> and <see cref="SerializerInfo.Formatters"/>,
        /// never on any other declared message, so an edit to an unrelated message can never change it
        /// (unlike <see cref="ResolvedMessagesByType"/>'s WHOLE-compilation source table, this one never
        /// needs narrowing to stay poison-free).
        /// </summary>
        public ImmutableDictionary<TypeKey, MessageInfo> ClosedGenericSchemas { get; }

        /// <summary>The distinct hand-written formatters actually used by <see cref="ReachableMessages"/>, sorted for deterministic field/constructor emission. See <see cref="CollectUsedFormatters"/>.</summary>
        public ImmutableArray<FormatterInfo> UsedFormatters { get; }

        /// <summary>This serializer's union dispatch helpers. See <see cref="PlanUnionHelpers"/>.</summary>
        public UnionPlan UnionPlan { get; }

        /// <summary>Diagnostics from validating <see cref="ReachableMessages"/>/<see cref="TopLevelMessages"/>. Empty when <see cref="IsEmittable"/> is false (there is nothing to validate).</summary>
        public ImmutableArray<DiagnosticSpec> ValidationDiagnostics { get; }

        /// <summary>A serializer whose own declaration failed <see cref="EvaluateGate"/>: nothing past the gate was ever computed.</summary>
        public static ResolvedSerializer NotEmittable(SerializerInfo serializer, ImmutableArray<DiagnosticSpec> gateDiagnostics)
        {
            return new ResolvedSerializer(
                serializer,
                isEmittable: false,
                gateDiagnostics: gateDiagnostics,
                resolvedMessagesByType: ImmutableDictionary<TypeKey, MessageInfo>.Empty,
                topLevelMessages: ClosedSet.Empty,
                reachableMessages: ImmutableArray<MessageInfo>.Empty,
                closedGenericSchemas: ImmutableDictionary<TypeKey, MessageInfo>.Empty,
                usedFormatters: ImmutableArray<FormatterInfo>.Empty,
                unionPlan: UnionPlan.Empty,
                validationDiagnostics: ImmutableArray<DiagnosticSpec>.Empty);
        }

        /// <summary>A serializer that cleared <see cref="EvaluateGate"/>, with its fully resolved message table and dispatch plans.</summary>
        public static ResolvedSerializer Emittable(
            SerializerInfo serializer,
            ImmutableArray<DiagnosticSpec> gateDiagnostics,
            ImmutableDictionary<TypeKey, MessageInfo> resolvedMessagesByType,
            ClosedSet topLevelMessages,
            ImmutableArray<MessageInfo> reachableMessages,
            ImmutableDictionary<TypeKey, MessageInfo> closedGenericSchemas,
            ImmutableArray<FormatterInfo> usedFormatters,
            UnionPlan unionPlan,
            ImmutableArray<DiagnosticSpec> validationDiagnostics)
        {
            return new ResolvedSerializer(
                serializer,
                isEmittable: true,
                gateDiagnostics,
                resolvedMessagesByType,
                topLevelMessages,
                reachableMessages,
                closedGenericSchemas,
                usedFormatters,
                unionPlan,
                validationDiagnostics);
        }

        public bool Equals(ResolvedSerializer? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return IsEmittable == other.IsEmittable
                && Serializer.Equals(other.Serializer)
                && TopLevelMessages.Equals(other.TopLevelMessages)
                && UnionPlan.Equals(other.UnionPlan)
                && ValueEquality.SequenceEquals(GateDiagnostics, other.GateDiagnostics)
                && ValueEquality.SequenceEquals(ReachableMessages, other.ReachableMessages)
                && ValueEquality.SequenceEquals(UsedFormatters, other.UsedFormatters)
                && ValueEquality.SequenceEquals(ValidationDiagnostics, other.ValidationDiagnostics)
                && ValueEquality.DictionaryEquals(ResolvedMessagesByType, other.ResolvedMessagesByType)
                && ValueEquality.DictionaryEquals(ClosedGenericSchemas, other.ClosedGenericSchemas);
        }

        public override bool Equals(object? obj) => Equals(obj as ResolvedSerializer);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, IsEmittable);
            hash = ValueEquality.Combine(hash, Serializer.GetHashCode());
            hash = ValueEquality.Combine(hash, TopLevelMessages.GetHashCode());
            hash = ValueEquality.Combine(hash, UnionPlan.GetHashCode());
            hash = ValueEquality.Combine(hash, GateDiagnostics);
            hash = ValueEquality.Combine(hash, ReachableMessages);
            hash = ValueEquality.Combine(hash, UsedFormatters);
            hash = ValueEquality.Combine(hash, ValidationDiagnostics);
            hash = ValueEquality.CombineDictionary(hash, ResolvedMessagesByType);
            hash = ValueEquality.CombineDictionary(hash, ClosedGenericSchemas);
            return hash;
        }
    }

    /// <summary>
    /// The S5 compilation-facts stage's cached output (see <see cref="ComputeCompilationFacts"/>):
    /// everything the pipeline needs to know about the WHOLE compilation, computed once per
    /// compilation change and shared by every serializer, rather than recomputed per serializer
    /// inside a diagnostics-only output that also consumed the live <see cref="Compilation"/>
    /// directly (the pre-S5 shape of <c>ValidateProtocolCoverage</c>). Symbol-free and
    /// value-equatable like every other cached pipeline model, so this stage's OUTPUT compares equal
    /// across two compilations that differ only by something none of these facts cares about (an
    /// edit to an unrelated file, a metadata reference that does not touch Akka.Serialization.V2),
    /// keeping the AKKASG029 coverage output -- and, per Decision 19 in
    /// openspec/changes/messagepack-sourcegen-validation/design.md, eventually the resolve stage too
    /// -- cached instead of re-diagnosing on every keystroke. See
    /// <see cref="AkkaSerializerGenerator.TrackingNames.CompilationFacts"/> for why this is NOT yet
    /// combined into <see cref="ResolvedSerializer"/>'s own inputs.
    /// </summary>
    internal sealed class CompilationFacts : IEquatable<CompilationFacts>
    {
        public static readonly CompilationFacts Empty = new(
            ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty,
            ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty,
            ImmutableArray<string>.Empty);

        public CompilationFacts(
            ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> localUnmarkedImplementorsByProtocol,
            ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> referencedAssemblyImplementorsByProtocol,
            ImmutableArray<string> referencedAssembliesUsingV2)
        {
            LocalUnmarkedImplementorsByProtocol = localUnmarkedImplementorsByProtocol;
            ReferencedAssemblyImplementorsByProtocol = referencedAssemblyImplementorsByProtocol;
            ReferencedAssembliesUsingV2 = referencedAssembliesUsingV2.IsDefault ? ImmutableArray<string>.Empty : referencedAssembliesUsingV2;
        }

        /// <summary>
        /// Per protocol key, the type keys of every non-abstract class/struct DECLARED IN THIS
        /// COMPILATION that implements the protocol interface without an <c>[AkkaSerializable]</c>
        /// attribute -- the AKKASG029 input (see <see cref="AkkaSerializerGenerator.ValidateProtocolCoverage"/>).
        /// Sorted by <see cref="TypeKey.MetadataName"/> (ordinal) for a deterministic diagnostic
        /// order. One entry per protocol key that at least one collected
        /// <c>[AkkaSerializer&lt;TProtocol&gt;]</c> declares, even when that entry's array is empty
        /// (a protocol with clean coverage) -- see <see cref="AkkaSerializerGenerator.ComputeLocalUnmarkedImplementorsByProtocol"/>.
        /// </summary>
        public ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> LocalUnmarkedImplementorsByProtocol { get; }

        /// <summary>
        /// Per protocol key, the type keys of every <c>[AkkaSerializable]</c>-marked implementor of
        /// that protocol (or of a marked union base, per Decision 21) DECLARED IN A REFERENCED
        /// ASSEMBLY -- Decision 19's input. EMPTY for every compilation today: the enumeration
        /// behind it is a skeleton that always returns no implementors (see
        /// <see cref="AkkaSerializerGenerator.EnumerateReferencedAssemblyImplementors"/>); only
        /// <see cref="ReferencedAssembliesUsingV2"/> (the filter that walk will use) is real yet.
        /// </summary>
        public ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> ReferencedAssemblyImplementorsByProtocol { get; }

        /// <summary>
        /// The sorted (ordinal) names of every referenced assembly that itself references
        /// Akka.Serialization.V2 -- the set the eventual Decision 19 implementor walk will need to
        /// visit. An assembly that does not reference V2 cannot declare an
        /// <c>[AkkaSerializable]</c> type (the attribute lives in V2), so narrowing to this set
        /// first is what keeps that future walk cheap.
        /// </summary>
        public ImmutableArray<string> ReferencedAssembliesUsingV2 { get; }

        public bool Equals(CompilationFacts? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(ReferencedAssembliesUsingV2, other.ReferencedAssembliesUsingV2)
                && ValueEquality.ArrayDictionaryEquals(LocalUnmarkedImplementorsByProtocol, other.LocalUnmarkedImplementorsByProtocol)
                && ValueEquality.ArrayDictionaryEquals(ReferencedAssemblyImplementorsByProtocol, other.ReferencedAssemblyImplementorsByProtocol);
        }

        public override bool Equals(object? obj) => Equals(obj as CompilationFacts);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, ReferencedAssembliesUsingV2);
            hash = ValueEquality.CombineArrayDictionary(hash, LocalUnmarkedImplementorsByProtocol);
            hash = ValueEquality.CombineArrayDictionary(hash, ReferencedAssemblyImplementorsByProtocol);
            return hash;
        }
    }
}
