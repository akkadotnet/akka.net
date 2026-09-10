//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Extraction.cs" company="Akka.NET Project">
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
    private const string SerializerAttributeFullName = "Akka.Serialization.V2.AkkaSerializerAttribute`1";

    private const string SerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute";

    private const string FieldAttributeFullName = "Akka.Serialization.V2.AkkaFieldAttribute";

    private const string UnionAttributeFullName = "Akka.Serialization.V2.AkkaUnionAttribute";

    private const string GenericSerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute`1";

    private const string FormatterAttributeFullName = "Akka.Serialization.V2.AkkaSerializerFormatterAttribute`2";

    private const string ExtendedActorSystemFullName = "Akka.Actor.ExtendedActorSystem";

    private const string AkkaSerializerBaseTypeFullName = "Akka.Serialization.V2.AkkaSerializer";

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        // Reading only the first attribute is safe: AkkaSerializerAttribute<TProtocol> declares
        // AllowMultiple = false, and empirically (see AkkaSerializerGeneratorDiagnosticsSpec)
        // the C# compiler enforces that against the OPEN generic attribute definition, not each
        // closed construction -- [AkkaSerializer<IA>][AkkaSerializer<IB>] on the same class is
        // rejected with CS0579 ("Duplicate 'AkkaSerializer<>' attribute") even though IA and IB
        // differ, so at most one [AkkaSerializer<T>] ever reaches this method. No AKKASG030 is
        // needed for this case.
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        string? name = null;
        var serializerId = 0;

        // AkkaSerializerAttribute<TProtocol>(string name, int serializerId): both arguments are
        // mandatory POSITIONAL constructor arguments now -- the attribute has no settable
        // properties left, so `[AkkaSerializer<T>(Name = "x", SerializerId = 1)]` named-property
        // syntax cannot compile and NamedArguments can never carry either value. A length other
        // than 2 cannot occur for a successfully-compiled use of this attribute.
        if (attribute.ConstructorArguments.Length == 2)
        {
            name = attribute.ConstructorArguments[0].Value as string;
            if (attribute.ConstructorArguments[1].Value is int id)
                serializerId = id;
        }

        // The protocol type symbol is consumed HERE and only here: everything the pipeline needs
        // downstream is its fully-qualified name (dispatch/grouping keys) and whether it is an
        // interface (AKKASG033). Retaining the INamedTypeSymbol in the cached model would defeat
        // incremental caching outright -- symbols never compare equal across compilations.
        var protocolType = attribute.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        var protocolTypeFullName = protocolType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
        var protocolTypeIsInterface = protocolType?.TypeKind == TypeKind.Interface;

        var formatterAttributeType = compilation.GetTypeByMetadataName(FormatterAttributeFullName);
        var extendedActorSystemType = compilation.GetTypeByMetadataName(ExtendedActorSystemFullName);
        var formatters = ExtractFormatters(symbol, formatterAttributeType, extendedActorSystemType);
        var closedGenericRegistrations = ExtractClosedGenericRegistrations(symbol, compilation);

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolTypeFullName,
            protocolTypeIsInterface,
            symbol.DeclaredAccessibility,
            formatters,
            closedGenericRegistrations,
            IsPartial(symbol),
            symbol.IsGenericType,
            DerivesFromAkkaSerializerBase(symbol, compilation));
    }

    /// <summary>
    /// Whether EVERY syntax declaration of <paramref name="symbol"/> carries the 'partial'
    /// modifier -- a class with a single non-partial declaration, or with any one part missing
    /// 'partial', is already a compile error (CS0260) once the generator emits its own partial
    /// declaration of the same class; AKKASG032 replaces that cryptic error with a direct one.
    /// A declaring reference that is not a <see cref="ClassDeclarationSyntax"/> cannot occur here
    /// in practice (the extraction pipeline only targets <see cref="ClassDeclarationSyntax"/>
    /// nodes, and every partial part of one class shares the same declaration kind) and is
    /// conservatively treated as satisfying the check rather than asserted against.
    /// </summary>
    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is ClassDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> derives (directly or transitively) from
    /// <see cref="AkkaSerializerBaseTypeFullName"/> -- the generated overrides (<c>Identifier</c>,
    /// <c>Manifest</c>, <c>Serialize</c>, <c>Deserialize</c>, <c>SizeHint</c>) require it as a base,
    /// or they fail to compile as overrides (CS0115) against whatever base the class actually has.
    /// </summary>
    private static bool DerivesFromAkkaSerializerBase(INamedTypeSymbol symbol, Compilation compilation)
    {
        var akkaSerializerBaseType = compilation.GetTypeByMetadataName(AkkaSerializerBaseTypeFullName);
        if (akkaSerializerBaseType == null)
            return false;

        for (var baseType = symbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, akkaSerializerBaseType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts <c>[AkkaSerializable&lt;T&gt;]</c> registrations from the serializer class. A
    /// valid target is a CLOSED generic construction (no unbound generics, no type parameters
    /// anywhere in its arguments) whose definition is annotated <c>[AkkaSerializable]</c>; its
    /// <see cref="MessageInfo"/> is built from the constructed symbol, so all field types arrive
    /// already substituted. Invalid targets are recorded with a null message so AKKASG020 fires.
    /// </summary>
    private static ImmutableArray<ClosedGenericRegistrationInfo> ExtractClosedGenericRegistrations(INamedTypeSymbol symbol, Compilation compilation)
    {
        var genericSerializableAttribute = compilation.GetTypeByMetadataName(GenericSerializableAttributeFullName);
        if (genericSerializableAttribute == null)
            return ImmutableArray<ClosedGenericRegistrationInfo>.Empty;

        var attributes = symbol.GetAttributes()
            .Where(attr => attr.AttributeClass is { IsGenericType: true } ac && SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, genericSerializableAttribute))
            .ToImmutableArray();
        if (attributes.IsEmpty)
            return ImmutableArray<ClosedGenericRegistrationInfo>.Empty;

        var knownTypes = KnownTypes.From(compilation);
        var builder = ImmutableArray.CreateBuilder<ClosedGenericRegistrationInfo>(attributes.Length);
        foreach (var attribute in attributes)
        {
            var manifest = string.Empty;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Manifest" && argument.Value.Value is string value)
                    manifest = value;
            }

            var target = attribute.AttributeClass!.TypeArguments[0] as INamedTypeSymbol;
            var serializableAttribute = target?.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            var isValidTarget = target is { IsGenericType: true, IsUnboundGenericType: false }
                && IsFullyClosed(target)
                && serializableAttribute != null;

            if (!isValidTarget)
            {
                var displayName = attribute.AttributeClass!.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Add(new ClosedGenericRegistrationInfo(displayName, message: null));
                continue;
            }

            // AllowEmpty travels with the definition's [AkkaSerializable]; the manifest is
            // per-construction (each closed form needs its own identity) and comes from the
            // registration attribute.
            var allowEmpty = serializableAttribute!.NamedArguments
                .Any(argument => argument.Key == "AllowEmpty" && argument.Value.Value is true);
            var message = ExtractMessageCore(
                target!,
                GetMessageDictionaryKey(target!),
                manifest,
                allowEmpty,
                knownTypes,
                compilation,
                definitionFullName: GetFullyQualifiedTypeName(target!.OriginalDefinition));
            builder.Add(new ClosedGenericRegistrationInfo(message.FullyQualifiedName, message));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Whether every type argument of a construction (recursively) is a concrete type -- i.e. no
    /// type parameter appears anywhere. <c>Wrapper&lt;Foo&gt;</c> and
    /// <c>Wrapper&lt;Pair&lt;Foo, Bar&gt;&gt;</c> qualify; <c>Wrapper&lt;T&gt;</c> inside another
    /// generic declaration does not.
    /// </summary>
    private static bool IsFullyClosed(INamedTypeSymbol type)
    {
        foreach (var argument in type.TypeArguments)
        {
            switch (argument)
            {
                case ITypeParameterSymbol:
                    return false;
                case INamedTypeSymbol { IsGenericType: true } nested when !IsFullyClosed(nested):
                    return false;
            }
        }

        return true;
    }

    private static ImmutableArray<FormatterInfo> ExtractFormatters(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? formatterAttributeType,
        INamedTypeSymbol? extendedActorSystemType)
    {
        if (formatterAttributeType == null)
            return ImmutableArray<FormatterInfo>.Empty;

        // AkkaSerializerFormatterAttribute<TTarget, TFormatter> where TFormatter :
        // IAkkaMessagePackFormatter<TTarget> -- a constructed generic attribute, so matching
        // requires comparing against OriginalDefinition (the same pattern used for
        // AkkaSerializableAttribute<TMessage> in ExtractClosedGenericRegistrations).
        var formatterAttributes = symbol.GetAttributes()
            .Where(attr => attr.AttributeClass is { IsGenericType: true } ac && SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, formatterAttributeType))
            .ToImmutableArray();

        if (formatterAttributes.IsEmpty)
            return ImmutableArray<FormatterInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<FormatterInfo>(formatterAttributes.Length);
        foreach (var attribute in formatterAttributes)
        {
            // TTarget and TFormatter come from the constructed attribute's own type arguments, not
            // ConstructorArguments -- there is no constructor argument carrying either type
            // anymore. Both slots are always present for a successfully-compiled two-arity
            // construction: neither a null target nor an unbound generic target/formatter can be
            // written here (the compiler rejects both at the attribute usage site), so unlike the
            // former Type-typed constructor arguments, these can never be "missing" or "not a
            // type" -- AKKASG011's former null-target check is unreachable and has been removed.
            var targetTypeSymbol = attribute.AttributeClass!.TypeArguments[0];
            var formatterTypeSymbol = attribute.AttributeClass!.TypeArguments[1];

            // Formatter targets must still be plain named types: arrays are not INamedTypeSymbol,
            // and CLOSED generic targets (e.g. List<int>) -- now directly expressible as TTarget --
            // would still collide on the arity-less fully-qualified name used for field matching.
            // Both remain recorded with IsTargetSupported = false so AKKASG011 fires.
            var targetNamedType = targetTypeSymbol as INamedTypeSymbol;
            var isTargetSupported = targetNamedType is { IsGenericType: false };
            var targetTypeFullName = isTargetSupported
                ? GetFullyQualifiedTypeName(targetNamedType!)
                : targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var formatterNamedType = formatterTypeSymbol as INamedTypeSymbol;
            var formatterTypeFullName = formatterNamedType != null
                ? GetFullyQualifiedTypeName(formatterNamedType)
                : formatterTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // The `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint is enforced by
            // the compiler at the attribute usage site, so AKKASG008's former interface-conformance
            // check can never fire here and has been removed. A generic constraint cannot express
            // "and not abstract", though: an abstract TFormatter still satisfies the constraint
            // (it has no `new()` clause to rule that out either -- formatters with an
            // ExtendedActorSystem-only constructor are legitimate), so AKKASG008 now guards
            // abstractness alone.
            var isAbstract = formatterNamedType?.IsAbstract ?? false;

            var ctorKind = formatterNamedType != null
                ? GetFormatterCtorKind(formatterNamedType, extendedActorSystemType)
                : FormatterCtorKind.None;

            builder.Add(new FormatterInfo(
                targetTypeFullName,
                targetTypeSymbol.IsValueType,
                formatterTypeFullName,
                isAbstract,
                ctorKind,
                isTargetSupported));
        }

        return builder.ToImmutable();
    }

    private static FormatterCtorKind GetFormatterCtorKind(INamedTypeSymbol formatterType, INamedTypeSymbol? extendedActorSystemType)
    {
        var hasParameterlessCtor = false;
        var hasSystemCtor = false;
        foreach (var ctor in formatterType.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (ctor.Parameters.Length == 0)
                hasParameterlessCtor = true;
            else if (ctor.Parameters.Length == 1 && extendedActorSystemType != null &&
                     SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, extendedActorSystemType))
                hasSystemCtor = true;
        }

        // Prefer the ExtendedActorSystem constructor when both are present: the generated
        // serializer always has the system in hand, and system context (transport addresses,
        // provider state) is why a formatter declares that constructor in the first place.
        if (hasSystemCtor)
            return FormatterCtorKind.System;

        return hasParameterlessCtor ? FormatterCtorKind.Parameterless : FormatterCtorKind.None;
    }

    private static MessageInfo? ExtractMessage(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var knownTypes = KnownTypes.From(compilation);
        var manifest = string.Empty;
        var allowEmpty = false;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
            else if (argument.Key == "AllowEmpty" && argument.Value.Value is bool allowEmptyValue)
                allowEmpty = allowEmptyValue;
        }

        // A generic [AkkaSerializable] DEFINITION is never a message itself -- a source generator
        // cannot reify an open generic. Only its registered closed constructions serialize
        // ([AkkaSerializable<T>], AKKASG020/022). Extract it as a flagged placeholder
        // carrying its protocols (for the AKKASG022 check) but no fields: a T-typed field would map
        // as Unsupported and produce a misleading AKKASG003 against the definition.
        if (symbol.IsGenericType)
        {
            return new MessageInfo(
                symbol.Name,
                GetFullyQualifiedTypeName(symbol),
                manifest,
                ImmutableArray<FieldInfo>.Empty,
                GetProtocolNames(symbol),
                allowEmpty: true,
                isGenericDefinition: true,
                definitionFullName: GetFullyQualifiedTypeName(symbol),
                invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty);
        }

        return ExtractMessageCore(symbol, GetFullyQualifiedTypeName(symbol), manifest, allowEmpty, knownTypes, compilation, definitionFullName: string.Empty);
    }

    /// <summary>
    /// Test-only entry point: extracts a <see cref="MessageInfo"/> straight from a symbol and its
    /// <see cref="Compilation"/>, with no <see cref="GeneratorAttributeSyntaxContext"/> syntax hook
    /// to drive it. Mirrors <see cref="ExtractMessage"/>'s attribute-argument handling exactly (the
    /// [AkkaSerializable] Manifest/AllowEmpty named-argument read and the generic-definition
    /// placeholder branch) -- the only extraction logic this duplicates is the small read of those
    /// named arguments, which <see cref="ExtractMessage"/> can only obtain from
    /// <see cref="GeneratorAttributeSyntaxContext.Attributes"/>, something this overload has no
    /// syntax context to supply the equivalent of. Everything else delegates to the same, unmodified
    /// <see cref="ExtractMessageCore"/> routine the real pipeline uses.
    /// </summary>
    internal static MessageInfo? ParseMessageForTests(INamedTypeSymbol type, Compilation compilation)
    {
        var serializableAttributeType = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
        var attribute = type.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, serializableAttributeType));
        if (attribute == null)
            return null;

        var knownTypes = KnownTypes.From(compilation);
        var manifest = string.Empty;
        var allowEmpty = false;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
            else if (argument.Key == "AllowEmpty" && argument.Value.Value is bool allowEmptyValue)
                allowEmpty = allowEmptyValue;
        }

        if (type.IsGenericType)
        {
            return new MessageInfo(
                type.Name,
                GetFullyQualifiedTypeName(type),
                manifest,
                ImmutableArray<FieldInfo>.Empty,
                GetProtocolNames(type),
                allowEmpty: true,
                isGenericDefinition: true,
                definitionFullName: GetFullyQualifiedTypeName(type),
                invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty);
        }

        return ExtractMessageCore(type, GetFullyQualifiedTypeName(type), manifest, allowEmpty, knownTypes, compilation, definitionFullName: string.Empty);
    }

    /// <summary>
    /// Builds the <see cref="MessageInfo"/> for a concrete serializable type: either an ordinary
    /// non-generic <c>[AkkaSerializable]</c> declaration or a registered closed generic
    /// construction. For a closed construction, <see cref="INamedTypeSymbol.GetMembers"/> returns
    /// SUBSTITUTED members -- a property declared as <c>T Payload</c> surfaces here with its
    /// concrete type argument -- so ordinary field inference applies with no type-parameter
    /// special-casing.
    /// </summary>
    private static MessageInfo ExtractMessageCore(
        INamedTypeSymbol symbol,
        string fullyQualifiedName,
        string manifest,
        bool allowEmpty,
        KnownTypes knownTypes,
        Compilation compilation,
        string definitionFullName)
    {
        var fields = new List<FieldInfo>();
        var fieldSymbols = new List<IPropertySymbol>();
        var invalidFields = ImmutableArray.CreateBuilder<InvalidFieldInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var fieldAttribute = member.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (fieldAttribute == null || fieldAttribute.ConstructorArguments.Length != 1)
                continue;

            // A static or getter-inaccessible [AkkaField] property can never be read by the
            // generated Write path (`message.Property`) -- record it as invalid (AKKASG028) instead
            // of emitting uncompilable code, and exclude it from both ordinary field extraction and
            // constructor selection below.
            if (member.IsStatic)
            {
                invalidFields.Add(new InvalidFieldInfo(member.Name, "is static; [AkkaField] requires an instance property"));
                continue;
            }

            if (member.GetMethod == null || !IsAccessibleFromGeneratedCode(member.GetMethod.DeclaredAccessibility))
            {
                invalidFields.Add(new InvalidFieldInfo(member.Name, "has no accessible getter"));
                continue;
            }

            var index = (int)fieldAttribute.ConstructorArguments[0].Value!;
            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(member.Type);
            // A property whose static type is `object` (nullable or not), after generic
            // substitution, is always the envelope-payload boundary: the type alone carries that
            // meaning, with no attribute involved.
            var isEnvelopePayload = member.Type.SpecialType == SpecialType.System_Object;
            var unionMembers = ExtractUnionMembers(member, knownTypes, compilation, out var hasUnionAttribute, out var unionDeclaredOnField);

            // Precedence: an `object`-typed field always wins (matching its documented precedence
            // over formatter registrations), then [AkkaUnion], then ordinary inference. A field
            // typed `object` that ALSO carries a field-level [AkkaUnion] is contradictory author
            // intent, not a harmless no-op -- AKKASG038 (error) fires on it below. A TYPE-LEVEL
            // [AkkaUnion] is irrelevant here: `object`'s own type never carries one.
            var mapping = isEnvelopePayload ? new TypeMapping(FieldKind.EnvelopePayload)
                : hasUnionAttribute ? new TypeMapping(FieldKind.Union)
                : MapType(member.Type, knownTypes);
            fields.Add(new FieldInfo(
                index,
                member.Name,
                member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                mapping,
                isNullable,
                unionMembers: isEnvelopePayload ? default : unionMembers,
                unionDeclaredOnObjectField: isEnvelopePayload && unionDeclaredOnField));
            fieldSymbols.Add(member);
        }

        var constructionPlan = SelectConstructor(symbol, fields, fieldSymbols, compilation);

        return new MessageInfo(
            symbol.Name,
            fullyQualifiedName,
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            GetProtocolNames(symbol),
            allowEmpty,
            isGenericDefinition: false,
            definitionFullName: definitionFullName,
            invalidFields: invalidFields.ToImmutable(),
            constructionPlan: constructionPlan);
    }

    /// <summary>
    /// Whether a member with this accessibility, declared on the message type, can be referenced
    /// from the generated serializer partial class. The generated class is never nested inside the
    /// message type and never derives from it, so only Public/Internal/ProtectedOrInternal (the
    /// internal-or-protected union, satisfied by same-assembly access) are reachable -- Protected and
    /// PrivateProtected both require a subtype relationship the generated code does not have.
    /// </summary>
    private static bool IsAccessibleFromGeneratedCode(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// Selects the constructor used to reconstruct <paramref name="symbol"/> on deserialize and
    /// plans how each valid [AkkaField] property is supplied: as a NAMED constructor argument (when
    /// it maps to a parameter of the chosen constructor) or as an object-initializer assignment
    /// (when it does not, provided it has an accessible 'set'/'init' accessor). See
    /// <see cref="ConstructionPlan"/>. <paramref name="fields"/>/<paramref name="fieldSymbols"/> are
    /// parallel: <c>fieldSymbols[i]</c> is the property backing <c>fields[i]</c>.
    /// </summary>
    private static ConstructionPlan SelectConstructor(
        INamedTypeSymbol symbol,
        IReadOnlyList<FieldInfo> fields,
        IReadOnlyList<IPropertySymbol> fieldSymbols,
        Compilation compilation)
    {
        var candidates = new List<(IMethodSymbol Ctor, ImmutableArray<ConstructorArgumentPlan> Arguments, ImmutableArray<string> UncoveredDefaulted, int DeclarationOrder)>();
        var declarationOrder = 0;
        foreach (var ctor in symbol.InstanceConstructors)
        {
            var order = declarationOrder++;
            if (!IsAccessibleFromGeneratedCode(ctor.DeclaredAccessibility))
                continue;

            var argumentsBuilder = ImmutableArray.CreateBuilder<ConstructorArgumentPlan>();
            var uncoveredDefaultedBuilder = ImmutableArray.CreateBuilder<string>();
            var eligible = true;
            foreach (var parameter in ctor.Parameters)
            {
                var matchIndex = MatchParameterToFieldIndex(parameter, fields, fieldSymbols, compilation);
                if (matchIndex >= 0)
                {
                    argumentsBuilder.Add(new ConstructorArgumentPlan(parameter.Name, fields[matchIndex].Name));
                    continue;
                }

                // A parameter without a default value MUST map to a field, or the constructor
                // cannot reconstruct the type at all -- not eligible. A defaulted, unmapped
                // parameter keeps the constructor eligible but is remembered for AKKASG027: its
                // value silently resets to the default on every deserialize.
                if (!parameter.HasExplicitDefaultValue)
                {
                    eligible = false;
                    break;
                }

                uncoveredDefaultedBuilder.Add(parameter.Name);
            }

            if (!eligible)
                continue;

            candidates.Add((ctor, argumentsBuilder.ToImmutable(), uncoveredDefaultedBuilder.ToImmutable(), order));
        }

        if (candidates.Count == 0)
        {
            return new ConstructionPlan(
                ImmutableArray<ConstructorArgumentPlan>.Empty,
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create("no accessible constructor maps every non-default parameter to an [AkkaField] property by name with an assignable type"));
        }

        // Most field-mapped parameters wins (fewer leftover properties needing initializer
        // assignment); ties break on fewest total parameters, then declaration order, for a
        // deterministic choice across identical-shaped candidates.
        var chosen = candidates
            .OrderByDescending(candidate => candidate.Arguments.Length)
            .ThenBy(candidate => candidate.Ctor.Parameters.Length)
            .ThenBy(candidate => candidate.DeclarationOrder)
            .First();

        var mappedFieldNames = new HashSet<string>(chosen.Arguments.Select(argument => argument.FieldName), StringComparer.Ordinal);
        var initializerFieldNames = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<string>();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (mappedFieldNames.Contains(field.Name))
                continue;

            var property = fieldSymbols[i];
            var hasAccessibleSetter = property.SetMethod != null && IsAccessibleFromGeneratedCode(property.SetMethod.DeclaredAccessibility);
            if (!hasAccessibleSetter)
            {
                errors.Add($"property '{field.Name}' is not covered by the selected constructor and has no accessible 'set' or 'init' accessor");
                continue;
            }

            initializerFieldNames.Add(field.Name);
        }

        return new ConstructionPlan(chosen.Arguments, initializerFieldNames.ToImmutable(), chosen.UncoveredDefaulted, errors.ToImmutable());
    }

    /// <summary>
    /// Matches a constructor parameter to a field by name -- ordinal (case-sensitive) first, then a
    /// UNIQUE case-insensitive match; an ambiguous case-insensitive match (more than one field name
    /// differs only by case) counts as no match at all rather than guessing. A name match still
    /// requires the property's type to be implicitly convertible to the parameter's type. Returns
    /// the matched field's index into <paramref name="fields"/>, or -1 for no match.
    /// </summary>
    private static int MatchParameterToFieldIndex(
        IParameterSymbol parameter,
        IReadOnlyList<FieldInfo> fields,
        IReadOnlyList<IPropertySymbol> fieldSymbols,
        Compilation compilation)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, parameter.Name, StringComparison.Ordinal))
                return compilation.HasImplicitConversion(fieldSymbols[i].Type, parameter.Type) ? i : -1;
        }

        var matchIndex = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (matchIndex >= 0)
                return -1;

            matchIndex = i;
        }

        if (matchIndex < 0)
            return -1;

        return compilation.HasImplicitConversion(fieldSymbols[matchIndex].Type, parameter.Type) ? matchIndex : -1;
    }

    /// <summary>
    /// Extracts the declared member set of a union field. The member set comes from the field's
    /// own <c>[AkkaUnion]</c> when present (a per-field override), otherwise from an
    /// <c>[AkkaUnion]</c> on the field's STATIC TYPE -- the natural declaration site, where the
    /// union is stated once for every field of that interface/abstract base. For a field declared
    /// as a type parameter inside a registered closed construction, the type-level lookup runs
    /// against the SUBSTITUTED type argument, so <c>T Body</c> with <c>T := IOrderEvent</c> picks
    /// up the union declared on <c>IOrderEvent</c>.
    /// Symbol-dependent facts (assignability to the field's static type, unbound-generic detection)
    /// are captured here; facts that need the whole message set (serializability, manifests) are
    /// validated later in <see cref="ValidateMessages"/> against the serializer's message
    /// dictionary. Malformed arguments (null, not a type, unbound generic) are recorded as
    /// unsupported entries so a diagnostic fires instead of the member silently vanishing.
    /// </summary>
    private static ImmutableArray<UnionMemberInfo> ExtractUnionMembers(
        IPropertySymbol member,
        KnownTypes knownTypes,
        Compilation compilation,
        out bool hasUnionAttribute,
        out bool unionDeclaredOnField)
    {
        hasUnionAttribute = false;
        unionDeclaredOnField = false;
        if (knownTypes.UnionAttribute == null)
            return ImmutableArray<UnionMemberInfo>.Empty;

        // Field-level override wins; otherwise inherit the type-level declaration from the field's
        // static type. OriginalDefinition covers a generic union base, where the attribute lives on
        // the definition. Whether the declaration sits on the FIELD itself is reported separately
        // (unionDeclaredOnField) -- needed ONLY to tell an object-typed field's own [AkkaUnion]
        // (AKKASG038, contradictory author intent) apart from a type-level [AkkaUnion] it merely
        // inherited (irrelevant for `object`, since `object`'s own type never carries one, but kept
        // symmetric with the general lookup below).
        var unionAttribute = member.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        unionDeclaredOnField = unionAttribute != null;
        if (unionAttribute == null && member.Type is INamedTypeSymbol fieldType)
        {
            unionAttribute = fieldType.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        }

        // AkkaUnionAttribute(Type first, params Type[] rest): TWO constructor arguments now, not
        // one -- [0] is the mandatory `first` member, [1] is the `params` array holding the rest.
        // The full declared member set is the concatenation of both; reading only
        // ConstructorArguments[0].Values (the pre-Seed-2 shape, when the whole set arrived as a
        // single `params Type[] memberTypes` array) would silently see only `first` and drop every
        // other declared member.
        if (unionAttribute == null || unionAttribute.ConstructorArguments.Length != 2)
            return ImmutableArray<UnionMemberInfo>.Empty;

        hasUnionAttribute = true;
        var restArguments = unionAttribute.ConstructorArguments[1].Values;
        var arguments = ImmutableArray.CreateBuilder<TypedConstant>(1 + restArguments.Length);
        arguments.Add(unionAttribute.ConstructorArguments[0]);
        arguments.AddRange(restArguments);

        var builder = ImmutableArray.CreateBuilder<UnionMemberInfo>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (argument.Value is not INamedTypeSymbol memberType || memberType.IsUnboundGenericType)
            {
                var displayName = argument.Value is ITypeSymbol typeSymbol
                    ? typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : "<null>";
                builder.Add(new UnionMemberInfo(displayName, isValueType: false, isAssignable: false, isSupported: false, isSealed: false, isAbstract: false));
                continue;
            }

            builder.Add(new UnionMemberInfo(
                GetMessageDictionaryKey(memberType),
                memberType.IsValueType,
                compilation.HasImplicitConversion(memberType, member.Type),
                isSupported: true,
                isSealed: memberType.IsSealed || memberType.IsValueType,
                isAbstract: memberType.IsAbstract,
                foreignAssemblyName: GetForeignAssemblyName(memberType, knownTypes)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The key a type is looked up under in the serializer's message dictionary. Non-generic types
    /// use the arity-less <see cref="GetFullyQualifiedTypeName"/> (the existing key for every
    /// <c>[AkkaSerializable]</c> message); closed generic constructions use the full,
    /// fully-qualified display string (e.g. <c>Ns.Wrapper&lt;Ns.Foo&gt;</c>) so distinct
    /// constructions stay distinct.
    /// </summary>
    private static string GetMessageDictionaryKey(INamedTypeSymbol type)
    {
        return type.IsGenericType
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : GetFullyQualifiedTypeName(type);
    }

    private static TypeMapping MapType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (TryGetNullableValueType(type, out var underlyingType))
            return MapType(underlyingType, knownTypes);

        // Only attach the fallback underlying-type name for NON-GENERIC named types:
        // GetFullyQualifiedTypeName is arity-less, so stamping it onto a generic field type
        // (e.g. Result<int>) would let it match a formatter registered for a same-named
        // non-generic type (Result) and emit ill-typed code. Generic field types keep an empty
        // mapping name, can never match a formatter, and still fail with AKKASG003.
        var mapping = MapTypeCore(type, knownTypes);
        if (mapping.TypeFullName.Length == 0 && type is INamedTypeSymbol { IsGenericType: false } namedType)
            return mapping.WithTypeFullName(GetFullyQualifiedTypeName(namedType));

        return mapping;
    }

    private static TypeMapping MapTypeCore(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (type is INamedTypeSymbol enumType && type.TypeKind == TypeKind.Enum)
        {
            // Enums encode as int32 on the wire ("writer.Write((int)value)" / "(E)reader.ReadInt32()"),
            // so an underlying type whose values are not all int32-representable (uint, long, ulong)
            // would silently truncate. Reject at compile time (AKKASG014) instead.
            var underlyingType = enumType.EnumUnderlyingType;
            if (underlyingType != null && !IsEnumUnderlyingTypeSupported(underlyingType.SpecialType))
            {
                return new TypeMapping(
                    FieldKind.UnsupportedEnumUnderlyingType,
                    GetFullyQualifiedTypeName(enumType),
                    enumUnderlyingTypeName: underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return new TypeMapping(FieldKind.Enum, GetFullyQualifiedTypeName(enumType));
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return new TypeMapping(FieldKind.ByteArray);

        // OriginalDefinition covers both shapes: for a non-generic type it is the type itself; for a
        // closed generic construction (Wrapper<Foo>) the [AkkaSerializable] attribute lives on the
        // definition. The mapping name is arity-aware (GetMessageDictionaryKey) so a closed
        // construction resolves to its registered [AkkaSerializable<T>] message -- or, if
        // unregistered, fails AKKASG023 instead of silently dropping its type arguments.
        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute)))
            return new TypeMapping(
                FieldKind.Object,
                GetMessageDictionaryKey(namedType),
                namedType.IsValueType,
                foreignAssemblyName: GetForeignAssemblyName(namedType, knownTypes),
                isGenericConstruction: namedType.IsGenericType);

        var mapping = type.SpecialType switch
        {
            SpecialType.System_String => new TypeMapping(FieldKind.String),
            SpecialType.System_Int32 => new TypeMapping(FieldKind.Int32),
            SpecialType.System_Int64 => new TypeMapping(FieldKind.Int64),
            SpecialType.System_Boolean => new TypeMapping(FieldKind.Boolean),
            SpecialType.System_Double => new TypeMapping(FieldKind.Double),
            SpecialType.System_Decimal => new TypeMapping(FieldKind.Decimal),
            SpecialType.System_DateTime => new TypeMapping(FieldKind.DateTime),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.Guid) => new TypeMapping(FieldKind.Guid),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.DateTimeOffset) => new TypeMapping(FieldKind.DateTimeOffset),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.ActorRef) => new TypeMapping(FieldKind.ActorRef),
            _ => new TypeMapping(FieldKind.Unsupported)
        };

        if (mapping.Kind != FieldKind.Unsupported)
            return mapping;

        if (TryMapCollection(type, knownTypes, out var collectionMapping))
            return collectionMapping;

        if (type is INamedTypeSymbol { IsGenericType: false, TypeKind: TypeKind.Class or TypeKind.Struct } missingNestedType)
            return new TypeMapping(FieldKind.MissingSerializableDefinition, GetFullyQualifiedTypeName(missingNestedType), foreignAssemblyName: GetForeignAssemblyName(missingNestedType, knownTypes));

        // AKKASG003 on an interface, an abstract class, or a type parameter is usually a forgotten
        // [AkkaUnion] declaration, or a field that should simply be typed `object`, rather than a
        // genuinely unrepresentable type -- flag it so ValidateMessages can point authors at both
        // fixes instead of leaving them to guess.
        var suggestsEnvelopeOrUnion = type.TypeKind == TypeKind.TypeParameter
            || (type is INamedTypeSymbol { IsAbstract: true } && type.TypeKind is TypeKind.Interface or TypeKind.Class);
        return suggestsEnvelopeOrUnion ? new TypeMapping(FieldKind.Unsupported, suggestsEnvelopeOrUnion: true) : mapping;
    }

    private static string GetForeignAssemblyName(ISymbol symbol, KnownTypes knownTypes)
    {
        var assembly = symbol.ContainingAssembly;
        return assembly != null && !SymbolEqualityComparer.Default.Equals(assembly, knownTypes.CompilationAssembly)
            ? assembly.Name
            : string.Empty;
    }

    /// <summary>
    /// Maps the ten natively-supported collection shapes to their collection <see cref="FieldKind"/>,
    /// recursively mapping element/key/value types so collections compose. Single-type-argument shapes
    /// (<c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableList&lt;T&gt;</c>, <c>ImmutableHashSet&lt;T&gt;</c>) and
    /// key/value shapes (<c>Dictionary&lt;TKey,TValue&gt;</c>, <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>,
    /// <c>ImmutableDictionary&lt;TKey,TValue&gt;</c>) are matched by <see cref="TryMatchSingleArgumentKind"/>
    /// and <see cref="TryMatchKeyValueKind"/> against the field's OWN declared generic type definition (not
    /// an "is-assignable" relationship, so e.g. a field declared <c>IReadOnlyList&lt;T&gt;</c> never matches
    /// the <c>IReadOnlyCollection&lt;T&gt;</c> shape even though the former extends the latter).
    /// A collection whose element/key/value is itself unsupported collapses to
    /// <see cref="FieldKind.Unsupported"/> so AKKASG003 fires with the full field type -- except an
    /// enum element with an unsupported underlying type, which propagates as
    /// <see cref="FieldKind.UnsupportedEnumUnderlyingType"/> so AKKASG014 fires naming the enum.
    /// <c>byte[]</c> is never seen here (it is intercepted earlier as <see cref="FieldKind.ByteArray"/>).
    /// </summary>
    private static bool TryMapCollection(ITypeSymbol type, KnownTypes knownTypes, out TypeMapping mapping)
    {
        mapping = default;

        if (type is IArrayTypeSymbol { Rank: 1 } arrayType && arrayType.ElementType.SpecialType != SpecialType.System_Byte)
        {
            var element = MapCollectionElement(arrayType.ElementType, knownTypes);
            mapping = TryCollapseBadElement(element, out var collapsed)
                ? collapsed
                : new TypeMapping(FieldKind.Array, typeArguments: ImmutableArray.Create(element));
            return true;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } namedType)
            return false;

        var definition = namedType.OriginalDefinition;

        if (TryMatchSingleArgumentKind(definition, knownTypes, out var singleArgumentKind))
        {
            mapping = MapSingleArgumentCollection(singleArgumentKind, namedType, knownTypes);
            return true;
        }

        if (TryMatchKeyValueKind(definition, knownTypes, out var keyValueKind))
        {
            mapping = MapKeyValueCollection(keyValueKind, namedType, knownTypes);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Matches <paramref name="definition"/> against every single-type-argument collection shape's OWN
    /// generic type definition symbol. Order is irrelevant: each shape's definition symbol is distinct
    /// (interface inheritance, e.g. <c>IReadOnlyList&lt;T&gt; : IReadOnlyCollection&lt;T&gt;</c>, does not
    /// make the two definitions equal), so at most one branch can ever match.
    /// </summary>
    private static bool TryMatchSingleArgumentKind(INamedTypeSymbol definition, KnownTypes knownTypes, out FieldKind kind)
    {
        if (knownTypes.ListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ListOfT))
        {
            kind = FieldKind.List;
            return true;
        }

        if (knownTypes.ReadOnlyListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyListOfT))
        {
            kind = FieldKind.ReadOnlyList;
            return true;
        }

        if (knownTypes.ReadOnlyCollectionOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyCollectionOfT))
        {
            kind = FieldKind.ReadOnlyCollection;
            return true;
        }

        if (knownTypes.ImmutableArrayOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableArrayOfT))
        {
            kind = FieldKind.ImmutableArray;
            return true;
        }

        if (knownTypes.ImmutableListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableListOfT))
        {
            kind = FieldKind.ImmutableList;
            return true;
        }

        if (knownTypes.ImmutableHashSetOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableHashSetOfT))
        {
            kind = FieldKind.ImmutableHashSet;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Matches <paramref name="definition"/> against every key/value collection shape's OWN generic type definition symbol. See <see cref="TryMatchSingleArgumentKind"/> for why match order does not matter.</summary>
    private static bool TryMatchKeyValueKind(INamedTypeSymbol definition, KnownTypes knownTypes, out FieldKind kind)
    {
        if (knownTypes.DictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.DictionaryOfKeyValue))
        {
            kind = FieldKind.Dictionary;
            return true;
        }

        if (knownTypes.ReadOnlyDictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyDictionaryOfKeyValue))
        {
            kind = FieldKind.ReadOnlyDictionary;
            return true;
        }

        if (knownTypes.ImmutableDictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableDictionaryOfKeyValue))
        {
            kind = FieldKind.ImmutableDictionary;
            return true;
        }

        kind = default;
        return false;
    }

    private static TypeMapping MapSingleArgumentCollection(FieldKind kind, INamedTypeSymbol namedType, KnownTypes knownTypes)
    {
        var element = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
        return TryCollapseBadElement(element, out var collapsed)
            ? collapsed
            : new TypeMapping(kind, typeArguments: ImmutableArray.Create(element));
    }

    private static TypeMapping MapKeyValueCollection(FieldKind kind, INamedTypeSymbol namedType, KnownTypes knownTypes)
    {
        var key = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
        var value = MapCollectionElement(namedType.TypeArguments[1], knownTypes);
        return TryCollapseBadElement(key, out var collapsedKey) ? collapsedKey
            : TryCollapseBadElement(value, out var collapsedValue) ? collapsedValue
            : new TypeMapping(kind, typeArguments: ImmutableArray.Create(key, value));
    }

    /// <summary>
    /// Collapses a bad collection element/key/value mapping into the mapping the containing field
    /// should carry. An enum with an unsupported underlying type keeps its identity (enum name plus
    /// backing type) so AKKASG014 can name it even through arbitrarily deep nesting; every other bad
    /// element collapses to plain <see cref="FieldKind.Unsupported"/> for AKKASG003.
    /// </summary>
    private static bool TryCollapseBadElement(TypeMapping element, out TypeMapping collapsed)
    {
        if (element.Kind == FieldKind.UnsupportedEnumUnderlyingType)
        {
            collapsed = new TypeMapping(
                FieldKind.UnsupportedEnumUnderlyingType,
                element.TypeFullName,
                enumUnderlyingTypeName: element.EnumUnderlyingTypeName);
            return true;
        }

        if (element.Kind is FieldKind.Unsupported or FieldKind.MissingSerializableDefinition)
        {
            collapsed = new TypeMapping(FieldKind.Unsupported);
            return true;
        }

        collapsed = default;
        return false;
    }

    /// <summary>
    /// Whether every value of an enum with this underlying type is exactly representable as an int32
    /// (the wire encoding for <see cref="FieldKind.Enum"/>). uint, long, and ulong are rejected: their
    /// out-of-int32-range values would silently truncate through the <c>(int)</c> cast.
    /// </summary>
    private static bool IsEnumUnderlyingTypeSupported(SpecialType underlyingType)
    {
        return underlyingType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32;
    }

    private static TypeMapping MapCollectionElement(ITypeSymbol type, KnownTypes knownTypes)
    {
        var declaredTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (TryGetNullableValueType(type, out var underlyingType))
            return MapTypeCore(underlyingType, knownTypes).AsCollectionElement(declaredTypeName, isNullable: true);

        var isNullable = type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
        return MapTypeCore(type, knownTypes).AsCollectionElement(declaredTypeName, isNullable);
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return TryGetNullableValueType(type, out _);
    }

    private static bool TryGetNullableValueType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlyingType = namedType.TypeArguments[0];
            return true;
        }

        underlyingType = type;
        return false;
    }

    /// <summary>
    /// The fully-qualified display names of every interface (direct and transitive) implemented by
    /// <paramref name="symbol"/>, in <see cref="ITypeSymbol.AllInterfaces"/> order. These are the
    /// cached, symbol-free stand-in for the former <c>ImmutableArray&lt;INamedTypeSymbol&gt;</c>
    /// protocol list: within one compilation a fully-qualified name identifies exactly one type, so
    /// ordinal comparison against <see cref="SerializerInfo.ProtocolTypeFullName"/> (produced by the
    /// same <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>) is equivalent to the former
    /// <see cref="SymbolEqualityComparer.Default"/> matching.
    /// </summary>
    private static ImmutableArray<string> GetProtocolNames(INamedTypeSymbol symbol)
    {
        var interfaces = symbol.AllInterfaces;
        if (interfaces.IsDefaultOrEmpty)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>(interfaces.Length);
        foreach (var implemented in interfaces)
            builder.Add(implemented.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.MoveToImmutable();
    }

    private static string GetFullyQualifiedTypeName(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        ISymbol? current = symbol;
        while (current is INamedTypeSymbol named)
        {
            parts.Push(named.Name);
            current = named.ContainingType;
        }

        var ns = GetNamespace(symbol);
        return string.IsNullOrEmpty(ns) ? "global::" + string.Join(".", parts) : "global::" + ns + "." + string.Join(".", parts);
    }

    private static string GetNamespace(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        var ns = symbol.ContainingNamespace;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            parts.Push(ns.Name);
            ns = ns.ContainingNamespace;
        }

        return string.Join(".", parts);
    }

    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            CompilationAssembly = compilation.Assembly;
            FieldAttribute = compilation.GetTypeByMetadataName(FieldAttributeFullName);
            UnionAttribute = compilation.GetTypeByMetadataName(UnionAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            ActorRef = compilation.GetTypeByMetadataName("Akka.Actor.IActorRef");
            ListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            ReadOnlyListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            ReadOnlyCollectionOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyCollection`1");
            DictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
            ReadOnlyDictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
            ImmutableArrayOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1");
            ImmutableListOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableList`1");
            ImmutableHashSetOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableHashSet`1");
            ImmutableDictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableDictionary`2");
        }

        /// <summary>
        /// The assembly of the compilation this generator run is producing output for. Used only to
        /// tell apart a type declared locally from one declared in a referenced assembly (the
        /// AKKASG007/AKKASG015 cross-assembly hint) -- never carried into any extracted model, so it
        /// does not affect incremental caching.
        /// </summary>
        public IAssemblySymbol CompilationAssembly { get; }

        public INamedTypeSymbol? FieldAttribute { get; }
        public INamedTypeSymbol? UnionAttribute { get; }
        public INamedTypeSymbol? SerializableAttribute { get; }
        public INamedTypeSymbol? Guid { get; }
        public INamedTypeSymbol? DateTimeOffset { get; }
        public INamedTypeSymbol? ActorRef { get; }
        public INamedTypeSymbol? ListOfT { get; }
        public INamedTypeSymbol? ReadOnlyListOfT { get; }
        public INamedTypeSymbol? ReadOnlyCollectionOfT { get; }
        public INamedTypeSymbol? DictionaryOfKeyValue { get; }
        public INamedTypeSymbol? ReadOnlyDictionaryOfKeyValue { get; }

        /// <summary>
        /// <c>System.Collections.Immutable.ImmutableArray&lt;T&gt;</c> -- a VALUE type (struct), unlike
        /// every other recognized collection kind. <c>default(ImmutableArray&lt;T&gt;)</c> is a distinct
        /// "uninitialized" state (<c>IsDefault</c>) from <c>ImmutableArray&lt;T&gt;.Empty</c>; see the
        /// write/read handling gated on <see cref="FieldKind.ImmutableArray"/> throughout this file.
        /// </summary>
        public INamedTypeSymbol? ImmutableArrayOfT { get; }
        public INamedTypeSymbol? ImmutableListOfT { get; }
        public INamedTypeSymbol? ImmutableHashSetOfT { get; }
        public INamedTypeSymbol? ImmutableDictionaryOfKeyValue { get; }

        public static KnownTypes From(Compilation compilation)
        {
            return new KnownTypes(compilation);
        }
    }
}
