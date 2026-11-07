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
using System.Runtime.CompilerServices;
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

    private const string GenericSerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializerIncludesAttribute`1";

    private const string FormatterAttributeFullName = "Akka.Serialization.V2.AkkaSerializerFormatterAttribute`2";

    private const string ExtendedActorSystemFullName = "Akka.Actor.ExtendedActorSystem";

    private const string AkkaSerializerBaseTypeFullName = "Akka.Serialization.V2.AkkaSerializer";

    private static ExtractedSerializer ExtractSerializer(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
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
        var knownTypes = GetKnownTypes(compilation);

        // One shared builder for every location entry this serializer contributes: its own
        // attribute, each formatter/closed-generic registration attribute (appended below by
        // BuildSerializerLocationBag), AND every [AkkaField] property of each closed-generic SCHEMA
        // this serializer registers (appended inline by ExtractSerializerCore -> ExtractClosedGenericRegistrations
        // -> ExtractMessageCore, the same inlining ExtractMessage below uses for ordinary messages).
        var locationEntries = ImmutableArray.CreateBuilder<LocationEntry>();
        var info = ExtractSerializerCore(symbol, attribute, compilation, knownTypes, cancellationToken, locationEntries);
        BuildSerializerLocationBag(locationEntries, symbol, attribute, info.Key, knownTypes, cancellationToken);
        return new ExtractedSerializer(info, new LocationBag(locationEntries.ToImmutable()));
    }

    /// <summary>
    /// Test-only entry point: extracts a <see cref="SerializerInfo"/> straight from a symbol and its
    /// <see cref="Compilation"/>, with no <see cref="GeneratorAttributeSyntaxContext"/> syntax hook
    /// to drive it -- the serializer-side counterpart of <see cref="ParseMessageForTests"/>. Finds
    /// the <c>[AkkaSerializer&lt;TProtocol&gt;]</c> attribute directly off the symbol (the only
    /// thing <see cref="GeneratorAttributeSyntaxContext.Attributes"/> would otherwise supply) and
    /// delegates everything else to the same, unmodified <see cref="ExtractSerializerCore"/> routine
    /// the real pipeline uses -- so a snapshot built from this reflects REAL extraction, not a
    /// hand-typed approximation of it (see GeneratorResolvedSerializerSnapshotSpec.cs).
    /// </summary>
    internal static SerializerInfo? ExtractSerializerForTests(INamedTypeSymbol symbol, Compilation compilation)
    {
        var serializerAttributeType = compilation.GetTypeByMetadataName(SerializerAttributeFullName);
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass != null && SymbolEqualityComparer.Default.Equals(attr.AttributeClass.OriginalDefinition, serializerAttributeType));
        if (attribute == null)
            return null;

        return ExtractSerializerCore(symbol, attribute, compilation, GetKnownTypes(compilation), CancellationToken.None);
    }

    private static SerializerInfo ExtractSerializerCore(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        Compilation compilation,
        KnownTypes knownTypes,
        CancellationToken cancellationToken,
        ImmutableArray<LocationEntry>.Builder? locationEntries = null)
    {
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
        // downstream is its key (dispatch/grouping) and whether it is an interface (AKKASG033).
        // Retaining the INamedTypeSymbol in the cached model would defeat incremental caching
        // outright -- symbols never compare equal across compilations.
        var protocolType = attribute.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        var protocolTypeKey = protocolType != null ? TypeKey.FromSymbol(protocolType) : default;
        var protocolTypeIsInterface = protocolType?.TypeKind == TypeKind.Interface;

        var extendedActorSystemType = compilation.GetTypeByMetadataName(ExtendedActorSystemFullName);
        var formatters = ExtractFormatters(symbol, knownTypes.FormatterAttribute, extendedActorSystemType);
        var serializerKey = TypeKey.FromSymbol(symbol);
        var (closedGenericRegistrations, closedGenericSchemas, prefixExpansions, explicitManifestsByTarget) =
            ExtractClosedGenericRegistrations(symbol, compilation, knownTypes, protocolType, cancellationToken, locationEntries);

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            serializerKey,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolTypeKey,
            protocolTypeIsInterface,
            symbol.DeclaredAccessibility,
            formatters,
            closedGenericRegistrations,
            closedGenericSchemas,
            IsPartial(symbol),
            symbol.IsGenericType,
            DerivesFromAkkaSerializerBase(symbol, compilation),
            compilationAssemblyName: compilation.AssemblyName ?? string.Empty,
            prefixExpansions: prefixExpansions,
            explicitManifestsByTarget: explicitManifestsByTarget);
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
    /// Extracts <c>[AkkaSerializerIncludes&lt;T&gt;]</c> registrations from the serializer class, as light
    /// specs (see <see cref="ClosedGenericRegistrationInfo"/>) plus the full schema for every VALID
    /// literal target (see <see cref="SerializerInfo.ClosedGenericSchemas"/>). Decision 18: a
    /// registration ADOPTS <typeparamref name="TMessage"/> into this serializer -- a closed generic
    /// construction is the common case, not the only one, so a valid target is any type (generic or
    /// not, closed: no unbound generics, no type parameters anywhere in its arguments) whose
    /// definition is annotated <c>[AkkaSerializable]</c>; its schema is built through the SAME
    /// <see cref="ExtractMessageCore"/> routine every other schema goes through, so all field types
    /// arrive already substituted. An invalid target gets a registration with no matching schema
    /// entry, so AKKASG020 fires instead of the registration silently vanishing.
    /// <para>
    /// A registration that also sets <c>ManifestPrefix</c> additionally expands over the closed
    /// member set of one or more of the target's own type arguments (the serializer's protocol
    /// interface, or any other union base), synthesizing one construction --
    /// with its own manifest, from the Decision 18 formula -- per combination. As of S7, THIS METHOD
    /// no longer performs that expansion itself: it classifies each type-argument position from data
    /// the argument's own symbol carries (no walk of the compilation's declared types) and, when the
    /// registration classifies successfully, records a light <see cref="PrefixExpansionSpec"/>
    /// instead. The actual cartesian product, symbol construction, and schema extraction move to
    /// <see cref="ComputeClosedGenericExpansions"/> (AkkaSerializerGenerator.Expansion.cs), a
    /// per-compilation stage that shares <see cref="CompilationFacts"/>' own closed-set buckets
    /// instead of re-walking the compilation once per registration. See
    /// <see cref="BuildPrefixExpansionSpec"/>.
    /// </para>
    /// </summary>
    private static (ImmutableArray<ClosedGenericRegistrationInfo> Registrations, ImmutableArray<MessageInfo> Schemas, ImmutableArray<PrefixExpansionSpec> PrefixExpansions, ImmutableArray<ExplicitTargetManifest> ExplicitManifestsByTarget) ExtractClosedGenericRegistrations(
        INamedTypeSymbol symbol,
        Compilation compilation,
        KnownTypes knownTypes,
        INamedTypeSymbol? protocolType,
        CancellationToken cancellationToken,
        ImmutableArray<LocationEntry>.Builder? locationEntries = null)
    {
        if (knownTypes.GenericSerializableAttribute == null)
            return (ImmutableArray<ClosedGenericRegistrationInfo>.Empty, ImmutableArray<MessageInfo>.Empty, ImmutableArray<PrefixExpansionSpec>.Empty, ImmutableArray<ExplicitTargetManifest>.Empty);

        var attributes = symbol.GetAttributes()
            .Where(attr => attr.AttributeClass is { IsGenericType: true } ac && SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, knownTypes.GenericSerializableAttribute))
            .ToImmutableArray();
        if (attributes.IsEmpty)
            return (ImmutableArray<ClosedGenericRegistrationInfo>.Empty, ImmutableArray<MessageInfo>.Empty, ImmutableArray<PrefixExpansionSpec>.Empty, ImmutableArray<ExplicitTargetManifest>.Empty);

        // First pass: every EXPLICITLY registered target's own manifest, keyed by TypeKey --
        // Decision 18's nested-construction case ("G<H<M>>" needs H<M> registered separately") looks
        // an argument's manifest up here when the argument is itself a generic construction, so
        // declaration order on the serializer class never matters. Read regardless of the target's
        // own validity (matching the pre-S7 shape exactly): captured below as
        // SerializerInfo.ExplicitManifestsByTarget too, so the S7 expansion stage can rebuild this
        // SAME dictionary for Decision 18's override rule without re-reading the serializer's own
        // attributes itself.
        var explicitManifestByTarget = new Dictionary<TypeKey, string>();
        var explicitManifestsBuilder = ImmutableArray.CreateBuilder<ExplicitTargetManifest>();
        foreach (var attribute in attributes)
        {
            var manifest = ReadStringNamedArgument(attribute, "Manifest");
            if (string.IsNullOrEmpty(manifest))
                continue;

            // Only a closed generic construction takes its manifest from the registration; a
            // non-generic target's manifest is its own (and a Manifest set here is AKKASG053).
            if (attribute.AttributeClass!.TypeArguments[0] is INamedTypeSymbol { IsGenericType: true } explicitTarget)
            {
                var explicitTargetKey = TypeKey.FromSymbol(explicitTarget);
                explicitManifestByTarget[explicitTargetKey] = manifest!;
                explicitManifestsBuilder.Add(new ExplicitTargetManifest(explicitTargetKey, manifest!));
            }
        }

        var registrationsBuilder = ImmutableArray.CreateBuilder<ClosedGenericRegistrationInfo>(attributes.Length);
        var schemasBuilder = ImmutableArray.CreateBuilder<MessageInfo>();
        var prefixExpansionsBuilder = ImmutableArray.CreateBuilder<PrefixExpansionSpec>();
        foreach (var attribute in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var registrationManifest = ReadStringNamedArgument(attribute, "Manifest") ?? string.Empty;
            var manifestPrefix = ReadStringNamedArgument(attribute, "ManifestPrefix") ?? string.Empty;
            var target = attribute.AttributeClass!.TypeArguments[0] as INamedTypeSymbol;

            // A non-generic type describes itself: its manifest comes from its own
            // [AkkaSerializable(Manifest = ...)], never from the registration, so there is exactly
            // one place to declare it. A Manifest set here anyway is kept aside for AKKASG053. Only a
            // closed generic construction, which has no declaration of its own, takes its manifest
            // from the registration.
            var isNonGenericTarget = target is { IsGenericType: false };
            var misplacedManifest = isNonGenericTarget ? registrationManifest : string.Empty;
            var manifest = isNonGenericTarget ? GetOwnManifest(target!, knownTypes) ?? string.Empty : registrationManifest;
            var serializableAttribute = target?.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));

            // Decision 18: a target no longer needs to be generic to be adopted -- only that its
            // definition (itself, for a non-generic type) carries [AkkaSerializable] and every type
            // argument (if any) is closed.
            var isValidTarget = target is { IsUnboundGenericType: false }
                && IsFullyClosed(target)
                && serializableAttribute != null;

            // "Manifest alone" or "neither property" registers (or attempts to register) the literal
            // target, exactly as before Decision 18. "ManifestPrefix alone" does NOT register the
            // literal construction -- only its expansion, below.
            if (registrationManifest.Length > 0 || manifestPrefix.Length == 0)
            {
                if (!isValidTarget)
                {
                    var targetKey = TypeKey.FromSymbol(attribute.AttributeClass!.TypeArguments[0]);
                    registrationsBuilder.Add(new ClosedGenericRegistrationInfo(targetKey, manifest: string.Empty, allowEmpty: false, manifestPrefix: manifestPrefix, misplacedManifest: misplacedManifest));
                }
                else
                {
                    // AllowEmpty travels with the definition's [AkkaSerializable]. The manifest is the
                    // type's own for a non-generic target, the registration's for a construction.
                    var allowEmpty = serializableAttribute!.NamedArguments
                        .Any(argument => argument.Key == "AllowEmpty" && argument.Value.Value is true);
                    var targetTypeKey = TypeKey.FromSymbol(target!);
                    var message = ExtractMessageCore(
                        target!,
                        targetTypeKey,
                        manifest,
                        allowEmpty,
                        knownTypes,
                        compilation,
                        definitionFullName: GetFullyQualifiedTypeName(target!.OriginalDefinition),
                        locationEntries);
                    registrationsBuilder.Add(new ClosedGenericRegistrationInfo(targetTypeKey, manifest, allowEmpty, manifestPrefix: manifestPrefix, misplacedManifest: misplacedManifest));
                    schemasBuilder.Add(message);
                }
            }

            if (manifestPrefix.Length == 0)
                continue;

            // ManifestPrefix on a target with no type argument (not generic at all, so certainly no
            // closed-set argument), or on an otherwise-invalid target, has nothing to expand: record
            // the failure (AKKASG040) instead of silently skipping the expansion. A base entry with
            // ManifestPrefix set was already recorded above only when Manifest was ALSO set; add one
            // here too when it was not, so AKKASG040/AKKASG042 always have a registration entry to
            // attach to.
            if (!isValidTarget || target is not { IsGenericType: true })
            {
                if (registrationManifest.Length == 0)
                {
                    var targetKey = TypeKey.FromSymbol(attribute.AttributeClass!.TypeArguments[0]);
                    registrationsBuilder.Add(new ClosedGenericRegistrationInfo(targetKey, manifest: string.Empty, allowEmpty: false, manifestPrefix: manifestPrefix,
                        expansionError: "the registration's target is not a valid closed generic construction (see AKKASG020)"));
                }

                continue;
            }

            BuildPrefixExpansionSpec(
                target!, manifestPrefix, protocolType, knownTypes,
                explicitManifestByTarget, cancellationToken,
                registrationsBuilder, schemasBuilder, prefixExpansionsBuilder);
        }

        return (registrationsBuilder.ToImmutable(), schemasBuilder.ToImmutable(), prefixExpansionsBuilder.ToImmutable(), explicitManifestsBuilder.ToImmutable());
    }

    private static string? ReadStringNamedArgument(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string value)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Builds the light <see cref="PrefixExpansionSpec"/> for one <c>ManifestPrefix</c> registration,
    /// classifying <paramref name="target"/>'s own type arguments (every one of them, not only the
    /// first -- a multi-argument generic expands to the product of its arguments' sets) with
    /// <see cref="TryClassifyPrefixArgumentPosition"/>, WITHOUT walking the compilation's declared
    /// types: a <see cref="PrefixArgumentKind.DiscoveredClosedSet"/> position defers its own member
    /// set to <see cref="ComputeClosedGenericExpansions"/> (AkkaSerializerGenerator.Expansion.cs),
    /// which shares <see cref="CompilationFacts"/>' own closed-set buckets instead of re-walking the
    /// compilation once per registration -- the S7 hoist this method exists for. An invalid target, or
    /// a position that classifies to neither a closed set nor a fixed manifest, is still resolved
    /// entirely HERE and recorded as an AKKASG040 error, exactly as before: those failures depend only
    /// on the target's and its arguments' own symbols, never on a whole-compilation walk.
    /// </summary>
    private static void BuildPrefixExpansionSpec(
        INamedTypeSymbol target,
        string manifestPrefix,
        INamedTypeSymbol? protocolType,
        KnownTypes knownTypes,
        Dictionary<TypeKey, string> explicitManifestByTarget,
        CancellationToken cancellationToken,
        ImmutableArray<ClosedGenericRegistrationInfo>.Builder registrationsBuilder,
        ImmutableArray<MessageInfo>.Builder schemasBuilder,
        ImmutableArray<PrefixExpansionSpec>.Builder prefixExpansionsBuilder)
    {
        var definitionAttribute = target.OriginalDefinition.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
        var baseTargetKey = TypeKey.FromSymbol(target);
        var baseTargetDisplayName = baseTargetKey.DisplayName ?? string.Empty;

        if (definitionAttribute == null)
        {
            registrationsBuilder.Add(new ClosedGenericRegistrationInfo(baseTargetKey, manifest: string.Empty, allowEmpty: false, manifestPrefix: manifestPrefix,
                expansionError: $"'{ToDisplayName(GetFullyQualifiedTypeName(target.OriginalDefinition))}' is not [AkkaSerializable]"));
            return;
        }

        var allowEmpty = definitionAttribute.NamedArguments.Any(a => a.Key == "AllowEmpty" && a.Value.Value is true);

        // Per type-argument position: classify up front so a failure on any position reports
        // AKKASG040 once, before any position is even considered closed-set-flavored.
        var positions = ImmutableArray.CreateBuilder<PrefixArgumentPosition>(target.TypeArguments.Length);
        var anyClosedSet = false;
        foreach (var argument in target.TypeArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryClassifyPrefixArgumentPosition(argument, protocolType, knownTypes, explicitManifestByTarget, out var position))
            {
                registrationsBuilder.Add(new ClosedGenericRegistrationInfo(baseTargetKey, manifest: string.Empty, allowEmpty: false, manifestPrefix: manifestPrefix,
                    expansionError: $"type argument '{ToDisplayName(argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))}' has no closed member set, and is not itself a registered [AkkaSerializable] type with its own manifest"));
                return;
            }

            if (position.Kind != PrefixArgumentKind.Fixed)
                anyClosedSet = true;

            positions.Add(position);
        }

        if (!anyClosedSet)
        {
            registrationsBuilder.Add(new ClosedGenericRegistrationInfo(baseTargetKey, manifest: string.Empty, allowEmpty: false, manifestPrefix: manifestPrefix,
                expansionError: $"none of '{ToDisplayName(baseTargetDisplayName)}''s type arguments has a closed member set; use the serializer's protocol interface, or an interface, abstract class, or closed hierarchy whose [AkkaSerializable] implementors make up the set"));
            return;
        }

        // The insertion points this spec's own expansion members must be spliced back in at -- see
        // PrefixExpansionSpec.RegistrationInsertionIndex's own doc comment. Captured HERE, at the
        // point this attribute's classification succeeded, so they reflect exactly how many literal
        // registrations/schemas precede this one in class declaration order -- reproducing F2/F3's
        // inline, per-attribute expansion order byte-for-byte after the S7 hoist.
        prefixExpansionsBuilder.Add(new PrefixExpansionSpec(
            TypeKey.FromSymbol(target.OriginalDefinition),
            baseTargetDisplayName,
            manifestPrefix,
            allowEmpty,
            positions.ToImmutable(),
            registrationInsertionIndex: registrationsBuilder.Count,
            schemaInsertionIndex: schemasBuilder.Count));
    }

    /// <summary>
    /// Classifies one <c>ManifestPrefix</c> target's own type-argument position into a
    /// <see cref="PrefixArgumentPosition"/>, using ONLY the argument's own symbol -- no walk of the
    /// compilation's declared types. Mirrors F2's <c>TryGetClosedSetMembers</c>/<c>TryResolveFixedArgumentManifest</c>
    /// fallthrough exactly (a position that is neither a union base nor a resolvable fixed manifest
    /// fails here the same way): the serializer's own protocol interface, or any other union base
    /// (<see cref="IsInferredUnionBase"/>), classifies as
    /// <see cref="PrefixArgumentKind.DiscoveredClosedSet"/> -- its member set is every
    /// <c>[AkkaSerializable]</c> type this compilation can see that derives from or implements it,
    /// resolved later from <see cref="CompilationFacts"/> by <see cref="ComputeClosedGenericExpansions"/>,
    /// not here. Anything else falls back to <see cref="TryResolveFixedArgumentManifest"/> -- an ordinary concrete
    /// type's own manifest, or a nested generic construction's sibling registration.
    /// </summary>
    private static bool TryClassifyPrefixArgumentPosition(
        ITypeSymbol argument,
        INamedTypeSymbol? protocolType,
        KnownTypes knownTypes,
        Dictionary<TypeKey, string> explicitManifestByTarget,
        out PrefixArgumentPosition position)
    {
        if (argument is INamedTypeSymbol namedArgument)
        {
            if (protocolType != null && SymbolEqualityComparer.Default.Equals(namedArgument, protocolType))
            {
                position = new PrefixArgumentPosition(PrefixArgumentKind.DiscoveredClosedSet, TypeKey.FromSymbol(namedArgument), ImmutableArray<PrefixArgumentCandidate>.Empty);
                return true;
            }

            // An open or closed hierarchy base expands the SAME way the protocol interface does
            // above -- every visible [AkkaSerializable] type that derives from or implements it,
            // local and referenced-assembly, discovered later.
            if (IsInferredUnionBase(namedArgument, MapType(namedArgument, knownTypes), knownTypes))
            {
                position = new PrefixArgumentPosition(PrefixArgumentKind.DiscoveredClosedSet, TypeKey.FromSymbol(namedArgument), ImmutableArray<PrefixArgumentCandidate>.Empty);
                return true;
            }
        }

        if (argument is INamedTypeSymbol fixedArgument && TryResolveFixedArgumentManifest(fixedArgument, knownTypes, explicitManifestByTarget, out var fixedManifest))
        {
            position = new PrefixArgumentPosition(PrefixArgumentKind.Fixed, default, ImmutableArray.Create(new PrefixArgumentCandidate(TypeKey.FromSymbol(fixedArgument), fixedManifest)));
            return true;
        }

        position = default!;
        return false;
    }

    /// <summary>A type's own top-level manifest, from its own (non-generic-registration) <c>[AkkaSerializable(Manifest = ...)]</c> attribute. Null when it has none.</summary>
    private static string? GetOwnManifest(INamedTypeSymbol type, KnownTypes knownTypes)
    {
        var attribute = type.GetAttributes().FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
        return attribute == null ? null : ReadStringNamedArgument(attribute, "Manifest");
    }

    /// <summary>
    /// The manifest for a FIXED (non-closed-set) Decision 18 expansion argument position: an ordinary
    /// concrete <c>[AkkaSerializable]</c> type's own manifest, or -- for a nested generic construction
    /// ("G&lt;H&lt;M&gt;&gt;") -- the manifest of a SIBLING literal registration of that exact
    /// construction on this same serializer (a generic definition's own Manifest is always ignored,
    /// AKKASG037, so there is nowhere else H&lt;M&gt;'s manifest could come from).
    /// </summary>
    private static bool TryResolveFixedArgumentManifest(
        INamedTypeSymbol argument, KnownTypes knownTypes, Dictionary<TypeKey, string> explicitManifestByTarget, out string manifest)
    {
        manifest = string.Empty;

        if (argument.IsGenericType)
        {
            if (!IsFullyClosed(argument))
                return false;

            return explicitManifestByTarget.TryGetValue(TypeKey.FromSymbol(argument), out manifest!) && manifest.Length > 0;
        }

        var own = GetOwnManifest(argument, knownTypes);
        if (string.IsNullOrEmpty(own))
            return false;

        manifest = own!;
        return true;
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
        // AkkaSerializerIncludesAttribute<TMessage> in ExtractClosedGenericRegistrations).
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
            var targetTypeKey = TypeKey.FromSymbol(targetTypeSymbol);

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
                targetTypeKey,
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

    private static ExtractedMessage ExtractMessage(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var knownTypes = GetKnownTypes(compilation);
        var manifest = string.Empty;
        var allowEmpty = false;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
            else if (argument.Key == "AllowEmpty" && argument.Value.Value is bool allowEmptyValue)
                allowEmpty = allowEmptyValue;
        }

        // A top-level C# union needs its own manifest, separate from its cases. Keyed on syntax
        // so a hand-written [AkkaSerializable] struct implementing IUnion keeps its own schema.
        if (context.TargetNode is not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax) && string.IsNullOrWhiteSpace(manifest))
        {
            var unionKey = TypeKey.FromSymbol(symbol);
            var unionLocations = ImmutableArray.CreateBuilder<LocationEntry>();
            AddLocationEntry(unionLocations, new LocationKey(unionKey, string.Empty), FirstLocationOrNull(symbol.Locations));
            var unionDiagnostic = new DiagnosticSpec(DiagnosticKey.SerializableOnCSharpUnion, new LocationKey(unionKey, string.Empty),
                ToDisplayName(GetFullyQualifiedTypeName(symbol)));
            return new ExtractedMessage(null, new LocationBag(unionLocations.ToImmutable()), ImmutableArray.Create(unionDiagnostic));
        }

        // A generic [AkkaSerializable] DEFINITION is never a message itself -- a source generator
        // cannot reify an open generic. Only its registered closed constructions serialize
        // ([AkkaSerializerIncludes<T>], AKKASG020/022). Extract it as a flagged placeholder
        // carrying its protocols (for the AKKASG022 check) but no fields: a T-typed field would map
        // as Unsupported and produce a misleading AKKASG003 against the definition.
        if (symbol.IsGenericType)
        {
            var definitionKey = BuildGenericDefinitionKey(symbol);
            var definitionInfo = new MessageInfo(
                symbol.Name,
                definitionKey,
                manifest,
                ImmutableArray<FieldInfo>.Empty,
                GetProtocolNames(symbol),
                allowEmpty: true,
                isGenericDefinition: true,
                definitionFullName: GetFullyQualifiedTypeName(symbol),
                invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty);

            var declarationLocations = ImmutableArray.CreateBuilder<LocationEntry>();
            AddLocationEntry(declarationLocations, new LocationKey(definitionKey, string.Empty), FirstLocationOrNull(symbol.Locations));
            var declarationDiagnostics = IsDeclaredAsUnion(symbol)
                ? ImmutableArray<DiagnosticSpec>.Empty
                : ExtractDeclarationDiagnostics(symbol, definitionKey, allowEmpty, knownTypes, declarationLocations);
            return new ExtractedMessage(definitionInfo, new LocationBag(declarationLocations.ToImmutable()), declarationDiagnostics);
        }

        var key = TypeKey.FromSymbol(symbol);

        // Field-level location capture is INLINED into ExtractMessageCore's own property walk (see
        // this file's own header comment for why a second full re-walk here would be wasteful); only
        // the type's own declaration entry is added here, once, up front.
        var locationEntries = ImmutableArray.CreateBuilder<LocationEntry>();
        AddLocationEntry(locationEntries, new LocationKey(key, string.Empty), FirstLocationOrNull(symbol.Locations));
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        var info = ExtractMessageCore(symbol, key, manifest, allowEmpty, knownTypes, compilation,
            definitionFullName: string.Empty, locationEntries, diagnostics);
        diagnostics.AddRange(ValidateMessageShape(info.Key, new LocationKey(info.Key, string.Empty),
            info.Fields.Select(field => field.Index), info.AllowEmpty, info.InaccessibleReason, info.InvalidFields));
        return new ExtractedMessage(info, new LocationBag(locationEntries.ToImmutable()), diagnostics.ToImmutable());
    }

    private static ImmutableArray<DiagnosticSpec> ExtractDeclarationDiagnostics(
        INamedTypeSymbol symbol, TypeKey key, bool allowEmpty, KnownTypes knownTypes,
        ImmutableArray<LocationEntry>.Builder locations)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        var indices = new List<int>();
        var invalidFields = ImmutableArray.CreateBuilder<InvalidFieldInfo>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var attribute = property.GetAttributes().FirstOrDefault(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (attribute == null)
            {
                AddOmittedPropertyDiagnostic(property, key, locations, diagnostics);
                continue;
            }
            if (attribute.ConstructorArguments.Length != 1)
                continue;

            AddLocationEntry(locations, new LocationKey(key, property.Name), FirstLocationOrNull(property.Locations));
            var invalidReason = GetInvalidFieldReason(property);
            if (invalidReason.Length > 0)
                invalidFields.Add(new InvalidFieldInfo(property.Name, invalidReason));
            else
                indices.Add((int)attribute.ConstructorArguments[0].Value!);
        }

        diagnostics.AddRange(ValidateMessageShape(key, new LocationKey(key, string.Empty), indices, allowEmpty,
            DescribeInaccessibleDeclaration(symbol), invalidFields.ToImmutable()));
        return diagnostics.ToImmutable();
    }

    private static void AddOmittedPropertyDiagnostic(
        IPropertySymbol property, TypeKey key, ImmutableArray<LocationEntry>.Builder locations,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        if (property.IsStatic || property.IsIndexer || property.IsAbstract || property.GetMethod?.IsExtern == true
            || property.DeclaredAccessibility != Accessibility.Public)
            return;
        var implementation = property.PartialImplementationPart ?? property;
        var hasStorage = implementation.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() switch
        {
            ParameterSyntax => true,
            PropertyDeclarationSyntax declaration => declaration.AccessorList?.Accessors.Any(accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.Body == null && accessor.ExpressionBody == null) == true,
            _ => false
        });
        if (!hasStorage)
            return;

        var at = new LocationKey(key, property.Name);
        AddLocationEntry(locations, at, FirstLocationOrNull(property.Locations));
        diagnostics.Add(new DiagnosticSpec(DiagnosticKey.PropertyNotSerialized, at, property.Name, ToDisplayName(key.DisplayName)));
    }

    private static string GetInvalidFieldReason(IPropertySymbol property)
    {
        if (property.IsStatic)
            return "is static; [AkkaField] requires an instance property";
        return property.GetMethod == null || !IsAccessibleFromGeneratedCode(property.GetMethod.DeclaredAccessibility)
            ? "has no accessible getter"
            : string.Empty;
    }

    /// <summary>
    /// The key for a generic <c>[AkkaSerializable]</c> DEFINITION placeholder (e.g. <c>Wrapper&lt;T&gt;</c>
    /// itself, as opposed to one of its registered closed constructions). This placeholder is never
    /// stored in any message dictionary (<see cref="ResolveSerializerMessages"/> excludes every
    /// <see cref="MessageInfo.IsGenericDefinition"/> entry), so its <see cref="TypeKey.MetadataName"/>/
    /// <see cref="TypeKey.TypeArguments"/> are never actually looked up against -- what DOES matter is
    /// that its <see cref="TypeKey.DisplayName"/> keeps rendering the same ARITY-LESS text
    /// <see cref="GetFullyQualifiedTypeName"/> has always produced for it (e.g. "Wrapper", never
    /// "Wrapper&lt;T&gt;"), since <see cref="MessageInfo.FullyQualifiedName"/> is compared against
    /// <see cref="MessageInfo.DefinitionFullName"/> (AKKASG022's registration-coverage check) and
    /// rendered directly into AKKASG037's message text -- both of which predate this key entirely.
    /// A plain <see cref="TypeKey.FromSymbol"/> would instead render the type-parameter list (e.g.
    /// "Wrapper&lt;T&gt;"), since <c>ToDisplayString</c> shows a generic type DEFINITION's own type
    /// parameters.
    /// </summary>
    private static TypeKey BuildGenericDefinitionKey(INamedTypeSymbol symbol)
    {
        var key = TypeKey.FromSymbol(symbol);
        return new TypeKey(key.MetadataName, key.TypeArguments, GetFullyQualifiedTypeName(symbol));
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

        var knownTypes = GetKnownTypes(compilation);
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
                BuildGenericDefinitionKey(type),
                manifest,
                ImmutableArray<FieldInfo>.Empty,
                GetProtocolNames(type),
                allowEmpty: true,
                isGenericDefinition: true,
                definitionFullName: GetFullyQualifiedTypeName(type),
                invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty);
        }

        return ExtractMessageCore(type, TypeKey.FromSymbol(type), manifest, allowEmpty, knownTypes, compilation, definitionFullName: string.Empty);
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
        TypeKey key,
        string manifest,
        bool allowEmpty,
        KnownTypes knownTypes,
        Compilation compilation,
        string definitionFullName,
        ImmutableArray<LocationEntry>.Builder? locationEntries = null,
        ImmutableArray<DiagnosticSpec>.Builder? declarationDiagnostics = null)
    {
        if (IsDeclaredAsUnion(symbol))
        {
            var unionField = new FieldInfo(0, string.Empty, key.DisplayName ?? string.Empty,
                new TypeMapping(FieldKind.Union, key, isValueType: true), false,
                unionMembers: ExtractCSharpUnionCases(symbol, knownTypes));
            return new MessageInfo(symbol.Name, key, manifest, ImmutableArray.Create(unionField),
                GetProtocolNames(symbol), allowEmpty: true, invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty, definitionFullName: definitionFullName,
                isSealed: true, isValueType: true, foreignAssemblyName: GetForeignAssemblyName(symbol, knownTypes),
                inaccessibleReason: DescribeInaccessibleDeclaration(symbol));
        }

        var fields = new List<FieldInfo>();
        var fieldSymbols = new List<IPropertySymbol>();
        var invalidFields = ImmutableArray.CreateBuilder<InvalidFieldInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var fieldAttribute = member.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (fieldAttribute == null)
            {
                if (locationEntries != null && declarationDiagnostics != null)
                    AddOmittedPropertyDiagnostic(member, key, locationEntries, declarationDiagnostics);
                continue;
            }
            if (fieldAttribute.ConstructorArguments.Length != 1)
                continue;

            // S6 "locations": captured here, reusing the SAME [AkkaField] check above instead of a
            // second GetAttributes() walk over every property -- see this file's own header comment.
            // Every [AkkaField] property gets an entry, valid or structurally invalid alike (the
            // static/getter checks below can still reject it), so AKKASG028 always finds its site.
            // For a closed generic construction's SUBSTITUTED member, Locations still resolves back
            // to the GENERIC DEFINITION's own declared property -- the right site, since the
            // construction itself has no separate syntax.
            if (locationEntries != null)
                AddLocationEntry(locationEntries, new LocationKey(key, member.Name), FirstLocationOrNull(member.Locations));

            // A static or getter-inaccessible [AkkaField] property can never be read by the
            // generated Write path (`message.Property`) -- record it as invalid (AKKASG028) instead
            // of emitting uncompilable code, and exclude it from both ordinary field extraction and
            // constructor selection below.
            var invalidReason = GetInvalidFieldReason(member);
            if (invalidReason.Length > 0)
            {
                invalidFields.Add(new InvalidFieldInfo(member.Name, invalidReason));
                continue;
            }

            var index = (int)fieldAttribute.ConstructorArguments[0].Value!;
            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(member.Type);
            // A property whose static type is `object` (nullable or not), after generic
            // substitution, is always the envelope-payload boundary: the type alone carries that
            // meaning, with no attribute involved.
            var isEnvelopePayload = member.Type.SpecialType == SpecialType.System_Object;
            // Precedence: an `object`-typed field is always the envelope boundary (matching its
            // documented precedence over formatter registrations). A C# 15 union field (TUnion or
            // Nullable<TUnion>) is a union whose cases are the union's own; a Union mapping with
            // IsValueType marks it (see FieldInfo.IsCSharpUnion). A field typed as an open or closed
            // hierarchy base (see IsInferredUnionBase) is a union whose members are resolved later,
            // once per compilation, from every [AkkaSerializable] type that derives from or
            // implements it: an empty UnionMembers on a Union mapping is that "resolve later" signal.
            var unionMembers = ImmutableArray<UnionMemberInfo>.Empty;
            TypeMapping mapping;
            if (isEnvelopePayload)
            {
                mapping = new TypeMapping(FieldKind.EnvelopePayload);
            }
            else if (TryGetCSharpUnionType(member.Type, knownTypes) is { } csharpUnionType)
            {
                unionMembers = ExtractCSharpUnionCases(csharpUnionType, knownTypes);
                mapping = new TypeMapping(FieldKind.Union, TypeKey.FromSymbol(csharpUnionType), isValueType: true);
            }
            else
            {
                mapping = MapType(member.Type, knownTypes);
                if (member.Type is INamedTypeSymbol namedFieldType && IsInferredUnionBase(namedFieldType, mapping, knownTypes))
                    mapping = new TypeMapping(FieldKind.Union, TypeKey.FromSymbol(namedFieldType));
            }

            fields.Add(new FieldInfo(
                index,
                member.Name,
                member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                mapping,
                isNullable,
                unionMembers: unionMembers));
            fieldSymbols.Add(member);
        }

        var constructionPlan = SelectConstructor(symbol, fields, fieldSymbols, compilation);

        return new MessageInfo(
            symbol.Name,
            key,
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            GetProtocolNames(symbol),
            allowEmpty,
            invalidFields: invalidFields.ToImmutable(),
            constructionPlan: constructionPlan,
            isGenericDefinition: false,
            definitionFullName: definitionFullName,
            isSealed: symbol.IsSealed || symbol.TypeKind == TypeKind.Struct,
            isAbstract: symbol.IsAbstract,
            isValueType: symbol.IsValueType,
            foreignAssemblyName: GetForeignAssemblyName(symbol, knownTypes),
            baseTypeNames: GetBaseTypeNames(symbol),
            inaccessibleReason: DescribeInaccessibleDeclaration(symbol));
    }

    /// <summary>
    /// Finds why generated code cannot name <paramref name="symbol"/>: it, or a type that contains
    /// it, is declared private, protected, or private protected. The generated serializer is a
    /// separate class that neither nests nor derives from the message, so it needs the whole
    /// declaration chain to be public, internal, or protected internal. Returns an empty string when
    /// the type is nameable. Only a type declared in this compilation can fail here: a
    /// referenced-assembly type reaches ExtractMessageCore only after the AKKASG039 accessibility
    /// check passed.
    /// </summary>
    private static string DescribeInaccessibleDeclaration(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? type = symbol; type != null; type = type.ContainingType)
        {
            if (!IsAccessibleFromGeneratedCode(type.DeclaredAccessibility))
            {
                var accessibility = type.DeclaredAccessibility == Accessibility.ProtectedAndInternal
                    ? "private protected"
                    : type.DeclaredAccessibility.ToString().ToLowerInvariant();
                return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + " is " + accessibility;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Fully-qualified display names of every base class (direct and transitive, excluding
    /// <c>object</c>) of <paramref name="symbol"/> -- Decision 21's class-hierarchy counterpart of
    /// <see cref="GetProtocolNames"/>, since a union base can be an abstract or closed
    /// class, and <see cref="ITypeSymbol.AllInterfaces"/> never reports a class. Empty for a struct
    /// (structs have no base class of their own) or a class whose only base is <c>object</c>.
    /// </summary>
    private static ImmutableArray<string> GetBaseTypeNames(INamedTypeSymbol symbol)
    {
        if (symbol.BaseType == null || symbol.BaseType.SpecialType == SpecialType.System_Object)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        for (var baseType = symbol.BaseType; baseType != null && baseType.SpecialType != SpecialType.System_Object; baseType = baseType.BaseType)
            builder.Add(baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.ToImmutable();
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
    /// Whether a field (or <c>ManifestPrefix</c> type argument) typed <paramref name="type"/> is a
    /// union over every <c>[AkkaSerializable]</c> type that derives from or implements it, with no
    /// attribute: an interface, an abstract class, or a C# 15 <c>closed</c> class or record (found by
    /// its modifier, or by <c>IsClosedTypeAttribute</c> on a referenced type). <paramref name="mapping"/>
    /// is the type's ordinary mapping: only a type the generator could not otherwise encode qualifies,
    /// so a known shape (a supported collection interface, <c>IActorRef</c>) or an
    /// <c>[AkkaSerializable]</c> type keeps its own encoding. A concrete class never qualifies, even one
    /// whose constructors are all private: it can always hand out instances of itself (a
    /// <c>static readonly</c> default, a factory), which no union of its subclasses could write.
    /// Written as a nested value instead, inferring a union for it would also change its wire format
    /// the moment someone added a subclass. Collection-like types (anything implementing
    /// <c>IEnumerable</c>) and framework types (the <c>System</c> namespace, and Akka.NET's own core
    /// assembly: <c>ActorPath</c>, <c>ICanTell</c>, ...) never qualify: no one means them as a closed
    /// set of their own messages, and they must keep their AKKASG003 error rather than turn into an
    /// empty union.
    /// </summary>
    private static bool IsInferredUnionBase(INamedTypeSymbol type, TypeMapping mapping, KnownTypes knownTypes)
    {
        if (mapping.Kind is not (FieldKind.Unsupported or FieldKind.MissingSerializableDefinition))
            return false;

        if (IsInSystemNamespace(type) || IsEnumerable(type) || IsAkkaCoreType(type, knownTypes))
            return false;

        if (type.TypeKind == TypeKind.Interface)
            return true;

        if (type.TypeKind != TypeKind.Class || type.IsSealed || type.IsStatic)
            return false;

        return type.IsAbstract || HasClosedModifier(type, CancellationToken.None);
    }

    private static bool IsAkkaCoreType(INamedTypeSymbol type, KnownTypes knownTypes)
        => knownTypes.ActorRef != null && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, knownTypes.ActorRef.ContainingAssembly);

    private static bool IsInSystemNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
            ns = ns.ContainingNamespace;

        return ns is { IsGlobalNamespace: false } && string.Equals(ns.Name, "System", StringComparison.Ordinal);
    }

    private static bool IsEnumerable(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
            return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (implemented.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
        }

        return false;
    }

    private const string IUnionFullName = "System.Runtime.CompilerServices.IUnion";

    /// <summary>
    /// The C# 15 union struct behind <paramref name="type"/> (unwrapping <c>Nullable&lt;T&gt;</c>), or
    /// null. See <see cref="IsCSharpUnionStruct"/> for what counts.
    /// </summary>
    private static INamedTypeSymbol? TryGetCSharpUnionType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (TryGetNullableValueType(type, out var underlying))
            type = underlying;

        return type is INamedTypeSymbol named && IsCSharpUnionStruct(named, knownTypes) ? named : null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> has the shape a C# 15 union lowers to: a struct implementing
    /// <c>System.Runtime.CompilerServices.IUnion</c>, with a public instance <c>object? Value</c>
    /// property (generated code reads it directly) and at least one public single-parameter
    /// constructor (the cases). The interface is the signal: Roslyn does not return the
    /// compiler-made <c>[Union]</c> attribute from a source symbol's <c>GetAttributes()</c>, so an
    /// attribute check would never match a union declared in source. Matched by full metadata name,
    /// so the net11.0 BCL type and a net10.0 consumer polyfill both count. A struct carrying
    /// <c>[AkkaSerializable]</c> has its own field schema and is never treated as a union. Union
    /// classes are not supported yet.
    /// </summary>
    private static bool IsCSharpUnionStruct(INamedTypeSymbol type, KnownTypes knownTypes)
    {
        if (type.TypeKind != TypeKind.Struct)
            return false;

        var implementsIUnion = false;
        foreach (var implemented in type.AllInterfaces)
        {
            if (string.Equals(implemented.MetadataName, "IUnion", StringComparison.Ordinal)
                && string.Equals(implemented.ToDisplayString(), IUnionFullName, StringComparison.Ordinal))
            {
                implementsIUnion = true;
                break;
            }
        }

        if (!implementsIUnion)
            return false;

        // An [AkkaSerializable] struct keeps its own schema, except a real union declaration.
        if (knownTypes.SerializableAttribute != null
            && type.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute))
            && !IsDeclaredAsUnion(type))
            return false;

        var hasPublicValue = type.GetMembers("Value").OfType<IPropertySymbol>().Any(property =>
            property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic && property.GetMethod != null
            && property.Type.SpecialType == SpecialType.System_Object);
        if (!hasPublicValue)
            return false;

        return type.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 1);
    }

    /// <summary>
    /// A C# union's cases: the parameter types of its public single-parameter instance constructors,
    /// in declaration order. Each case then goes through the same member rules as any other
    /// union member (AKKASG015-017, AKKASG025, AKKASG036).
    /// </summary>
    private static ImmutableArray<UnionMemberInfo> ExtractCSharpUnionCases(INamedTypeSymbol unionType, KnownTypes knownTypes)
    {
        var builder = ImmutableArray.CreateBuilder<UnionMemberInfo>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var constructor in unionType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public || constructor.Parameters.Length != 1)
                continue;

            // A nullable value-type case (union U(VS?, ...)) holds a boxed VS when set, so it is the
            // VS case on the wire; the VS value converts back to VS? when the union is rebuilt.
            var caseType = constructor.Parameters[0].Type;
            if (TryGetNullableValueType(caseType, out var underlyingCase))
                caseType = underlyingCase;

            if (!seen.Add(caseType))
                continue;

            if (caseType is not INamedTypeSymbol namedCase)
            {
                var displayName = caseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Add(new UnionMemberInfo(new TypeKey(displayName, ImmutableArray<TypeKey>.Empty, displayName), caseType.IsValueType, isSupported: false, isSealed: true, isAbstract: false));
                continue;
            }

            builder.Add(new UnionMemberInfo(
                TypeKey.FromSymbol(namedCase),
                namedCase.IsValueType,
                isSupported: true,
                isSealed: namedCase.IsSealed || namedCase.IsValueType,
                isAbstract: namedCase.IsAbstract,
                foreignAssemblyName: GetForeignAssemblyName(namedCase, knownTypes)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Whether <paramref name="type"/> is declared in source with a type declaration other than a
    /// class, struct, record or interface, or carries the compiler-emitted UnionAttribute in metadata.
    /// </summary>
    private static bool IsDeclaredAsUnion(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax and not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax))
                return true;
        }

        return type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.UnionAttribute");
    }

    private static TypeMapping MapType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (TryGetNullableValueType(type, out var underlyingType))
            return MapType(underlyingType, knownTypes);

        // Only attach the fallback underlying-type key for NON-GENERIC named types: stamping a key
        // onto a generic field type (e.g. Result<int>) would let it match a formatter registered for
        // a same-named non-generic type (Result) and emit ill-typed code -- TypeKey's metadata name
        // carries the arity suffix, so this guard still matters even though the key is no longer a
        // bare arity-less string. Generic field types keep a default (empty) key, can never match a
        // formatter, and still fail with AKKASG003.
        return WithFallbackKey(MapTypeCore(type, knownTypes), type);
    }

    /// <summary>
    /// Stamps a keyless mapping with the type's own <see cref="TypeKey"/> when the type is a plain,
    /// non-generic named type, so a formatter registered for it can match (the formatter dictionary is
    /// keyed by <see cref="TypeKey"/>). Shared by FIELD position (<see cref="MapType"/>) and collection
    /// ELEMENT/KEY/VALUE position (<see cref="MapCollectionElement"/>), which is what makes formatter
    /// resolution position-independent. A mapping that already names its type (Object, Enum, ...) keeps
    /// the key it has; a generic or array type stays keyless and can never match a formatter.
    /// </summary>
    private static TypeMapping WithFallbackKey(TypeMapping mapping, ITypeSymbol type)
    {
        if (mapping.TypeFullName.Length == 0 && type is INamedTypeSymbol { IsGenericType: false } namedType)
            return mapping.WithKey(TypeKey.FromSymbol(namedType));

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
                    TypeKey.FromSymbol(enumType),
                    enumUnderlyingTypeName: underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return new TypeMapping(FieldKind.Enum, TypeKey.FromSymbol(enumType));
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return new TypeMapping(FieldKind.ByteArray);

        // OriginalDefinition covers both shapes: for a non-generic type it is the type itself; for a
        // closed generic construction (Wrapper<Foo>) the [AkkaSerializable] attribute lives on the
        // definition. The mapping key is TypeKey.FromSymbol, whose metadata name plus type arguments
        // are arity-aware and construction-aware, so a closed construction resolves to its registered
        // [AkkaSerializerIncludes<T>] message -- or, if unregistered, fails AKKASG023 instead of silently
        // dropping its type arguments.
        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute)))
            return new TypeMapping(
                FieldKind.Object,
                TypeKey.FromSymbol(namedType),
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
            SpecialType.System_Single => new TypeMapping(FieldKind.Single),
            SpecialType.System_Int16 => new TypeMapping(FieldKind.Int16),
            SpecialType.System_SByte => new TypeMapping(FieldKind.SByte),
            SpecialType.System_Byte => new TypeMapping(FieldKind.Byte),
            SpecialType.System_UInt16 => new TypeMapping(FieldKind.UInt16),
            SpecialType.System_UInt32 => new TypeMapping(FieldKind.UInt32),
            SpecialType.System_UInt64 => new TypeMapping(FieldKind.UInt64),
            SpecialType.System_Char => new TypeMapping(FieldKind.Char),
            SpecialType.System_Decimal => new TypeMapping(FieldKind.Decimal),
            SpecialType.System_DateTime => new TypeMapping(FieldKind.DateTime),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.Guid) => new TypeMapping(FieldKind.Guid),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.DateTimeOffset) => new TypeMapping(FieldKind.DateTimeOffset),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.TimeSpan) => new TypeMapping(FieldKind.TimeSpan),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.ActorRef) => new TypeMapping(FieldKind.ActorRef),
            _ => new TypeMapping(FieldKind.Unsupported)
        };

        if (mapping.Kind != FieldKind.Unsupported)
            return mapping;

        if (TryMapCollection(type, knownTypes, out var collectionMapping))
            return collectionMapping;

        if (type is INamedTypeSymbol { IsGenericType: false, TypeKind: TypeKind.Class or TypeKind.Struct } missingNestedType)
            return new TypeMapping(FieldKind.MissingSerializableDefinition, TypeKey.FromSymbol(missingNestedType), foreignAssemblyName: GetForeignAssemblyName(missingNestedType, knownTypes));

        // An interface, abstract class, or type parameter: a property typed this way becomes a
        // union (IsInferredUnionBase), so this flag only survives into AKKASG003 for a collection
        // element, key, or value, or an unsubstituted type parameter -- where the hint points
        // authors at the union rule and at object instead of leaving them to guess.
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
    /// A collection keeps its structure even when an element/key/value is not natively representable
    /// (<see cref="FieldKind.Unsupported"/>, <see cref="FieldKind.MissingSerializableDefinition"/>, or an
    /// enum with an unsupported underlying type): whether a registered formatter can serve that position
    /// is only known later, once the serializer's formatters are in hand (<see cref="ResolveMessages"/>).
    /// Validation then reports whatever is still unusable -- AKKASG003 with the full field type, or
    /// AKKASG014 naming the enum -- see <see cref="FindUnusableLeaf"/>. An
    /// element/value typed <c>object</c> maps to <see cref="FieldKind.EnvelopePayload"/> instead (see
    /// <see cref="MapCollectionElement"/>); a dictionary KEY typed <c>object</c> maps to
    /// <see cref="FieldKind.Unsupported"/>.
    /// <c>byte[]</c> is never seen here (it is intercepted earlier as <see cref="FieldKind.ByteArray"/>).
    /// </summary>
    private static bool TryMapCollection(ITypeSymbol type, KnownTypes knownTypes, out TypeMapping mapping)
    {
        mapping = default;

        if (type is IArrayTypeSymbol { Rank: 1 } arrayType && arrayType.ElementType.SpecialType != SpecialType.System_Byte)
        {
            var element = MapCollectionElement(arrayType.ElementType, knownTypes);
            mapping = new TypeMapping(FieldKind.Array, typeArguments: ImmutableArray.Create(element));
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
        return new TypeMapping(kind, typeArguments: ImmutableArray.Create(element));
    }

    private static TypeMapping MapKeyValueCollection(FieldKind kind, INamedTypeSymbol namedType, KnownTypes knownTypes)
    {
        var key = MapCollectionElement(namedType.TypeArguments[0], knownTypes, isKeyPosition: true);
        var value = MapCollectionElement(namedType.TypeArguments[1], knownTypes);
        return new TypeMapping(kind, typeArguments: ImmutableArray.Create(key, value));
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

    /// <summary>
    /// Maps a single collection element/key/value type. <paramref name="isKeyPosition"/> is true only
    /// for a dictionary KEY (see <see cref="MapKeyValueCollection"/>); every other caller (an
    /// array/list element, a set member, or a dictionary value) leaves it false.
    /// </summary>
    private static TypeMapping MapCollectionElement(ITypeSymbol type, KnownTypes knownTypes, bool isKeyPosition = false)
    {
        var declaredTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (TryGetNullableValueType(type, out var underlyingType))
            return WithFallbackKey(MapTypeCore(underlyingType, knownTypes), underlyingType).AsCollectionElement(declaredTypeName, isNullable: true);

        var isNullable = type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;

        // An element/value whose static type is `object` (or `object?`) is always the
        // envelope-payload boundary, mirroring the property-level rule in ExtractMessageCore -- the
        // type alone carries that meaning, with no attribute involved, at every position a supported
        // collection can hold it: an array/list element, a set member, or a dictionary value.
        //
        // A dictionary KEY typed `object` is rejected instead (AKKASG003: the key stays a plain
        // Unsupported mapping, which FindUnusableLeaf reports for the whole field):
        // Dictionary&lt;TKey,TValue&gt; throws on a null key at runtime, so a nullable `object?` key
        // would crash the first time an envelope legitimately decodes to null; and even a non-null
        // envelope payload has no stable identity across a round trip (ReadEnvelopePayload always
        // materializes a brand-new instance on read), so hash/equality-based key lookups could never
        // reliably rediscover a deserialized entry. Neither failure mode is worth the surface area.
        if (type.SpecialType == SpecialType.System_Object)
        {
            return isKeyPosition
                ? new TypeMapping(FieldKind.Unsupported)
                : new TypeMapping(FieldKind.EnvelopePayload).AsCollectionElement(declaredTypeName, isNullable);
        }

        return WithFallbackKey(MapTypeCore(type, knownTypes), type).AsCollectionElement(declaredTypeName, isNullable);
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
        // Fast path: a non-nested type -- by far the common case -- needs no containing-type walk
        // (or Stack allocation) at all; the general path below produces the identical string for
        // this shape too, just at needless extra cost.
        if (symbol.ContainingType == null)
        {
            var ns0 = GetNamespace(symbol);
            return ns0.Length == 0 ? "global::" + symbol.Name : "global::" + ns0 + "." + symbol.Name;
        }

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
        var ns = symbol.ContainingNamespace;
        if (ns == null || ns.IsGlobalNamespace)
            return string.Empty;

        // Fast path: a single-segment namespace -- by far the common case -- needs no Stack/Join.
        if (ns.ContainingNamespace == null || ns.ContainingNamespace.IsGlobalNamespace)
            return ns.Name;

        var parts = new Stack<string>();
        while (ns != null && !ns.IsGlobalNamespace)
        {
            parts.Push(ns.Name);
            ns = ns.ContainingNamespace;
        }

        return string.Join(".", parts);
    }

    /// <summary>
    /// Caches one <see cref="KnownTypes"/> per <see cref="Compilation"/> instance. Before S5, both
    /// attribute transforms (<see cref="ExtractClosedGenericRegistrations"/>, called from
    /// <see cref="ExtractSerializerCore"/>, and <see cref="ExtractMessage"/>) called
    /// <see cref="KnownTypes.From"/> independently -- once per ATTRIBUTED TYPE per compilation
    /// change, each paying for about fifteen <see cref="Compilation.GetTypeByMetadataName(string)"/>
    /// lookups -- even though every call within one compilation change produces an identical
    /// result. A <see cref="ConditionalWeakTable{TKey,TValue}"/> ties each cached
    /// <see cref="KnownTypes"/> to the exact <see cref="Compilation"/> instance that produced it,
    /// with no broader lifetime and no cross-compilation leakage: once a <see cref="Compilation"/>
    /// is collected, its entry goes with it, and a brand-new <see cref="Compilation"/> (the next
    /// edit) always misses and recomputes exactly once, on first use.
    /// </summary>
    private static readonly ConditionalWeakTable<Compilation, KnownTypes> KnownTypesCache = new();

    private static KnownTypes GetKnownTypes(Compilation compilation)
    {
        return KnownTypesCache.GetValue(compilation, static c => KnownTypes.From(c));
    }

    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            CompilationAssembly = compilation.Assembly;
            FieldAttribute = compilation.GetTypeByMetadataName(FieldAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            SerializerAttribute = compilation.GetTypeByMetadataName(SerializerAttributeFullName);
            FormatterAttribute = compilation.GetTypeByMetadataName(FormatterAttributeFullName);
            GenericSerializableAttribute = compilation.GetTypeByMetadataName(GenericSerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            TimeSpan = compilation.GetTypeByMetadataName("System.TimeSpan");
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
        public INamedTypeSymbol? SerializableAttribute { get; }

        /// <summary>The open <c>AkkaSerializerAttribute&lt;TProtocol&gt;</c> definition -- resolved once per compilation, used by the Decision 19 upstream-serializer-binding walk (<see cref="AkkaSerializerGenerator.ComputeUpstreamSerializerBindings"/>).</summary>
        public INamedTypeSymbol? SerializerAttribute { get; }

        /// <summary>The open <c>AkkaSerializerFormatterAttribute&lt;TTarget, TFormatter&gt;</c> definition -- resolved once per compilation, shared by <see cref="ExtractFormatters"/> and <see cref="BuildSerializerLocationBag"/>.</summary>
        public INamedTypeSymbol? FormatterAttribute { get; }

        /// <summary>The open <c>AkkaSerializerIncludesAttribute&lt;TMessage&gt;</c> definition -- resolved once per compilation, shared by <see cref="ExtractClosedGenericRegistrations"/> and <see cref="BuildSerializerLocationBag"/>.</summary>
        public INamedTypeSymbol? GenericSerializableAttribute { get; }

        public INamedTypeSymbol? Guid { get; }
        public INamedTypeSymbol? DateTimeOffset { get; }
        public INamedTypeSymbol? TimeSpan { get; }
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
