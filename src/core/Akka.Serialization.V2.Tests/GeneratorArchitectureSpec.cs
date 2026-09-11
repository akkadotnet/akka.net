//-----------------------------------------------------------------------
// <copyright file="GeneratorArchitectureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Structural regression guard for the generator's incremental-caching discipline -- the
/// counterpart of <see cref="GeneratorIncrementalCachingSpec"/>'s BEHAVIORAL guard (which proves the
/// pipeline actually reuses cache across a real run). This spec inspects the MODEL TYPES
/// THEMSELVES, by reflection:
/// <list type="number">
/// <item>No model type's field or property may hold a live Roslyn symbol, <see cref="Compilation"/>,
/// syntax node/tree, or <see cref="Location"/> -- any of those breaks structural equality across
/// compilations (a fresh <see cref="Compilation"/> is never <c>==</c> to the last one) and silently
/// defeats incremental caching, even though the code compiles and runs fine.</item>
/// <item>Every model type has real value equality: either a hand-written <c>Equals(object)</c>/
/// <c>GetHashCode()</c> override pair, or it is a record/record struct (whose compiler-generated
/// equality is already trustworthy), or an enum (whose built-in equality is a plain value compare).</item>
/// </list>
/// Model types are discovered by reflection -- every type nested directly under
/// <see cref="AkkaSerializerGenerator"/> with <c>internal</c> visibility, in the generator's own
/// namespace -- rather than hardcoded by name, so a newly added model is covered automatically
/// without anyone remembering to update this file.
/// </summary>
public sealed class GeneratorArchitectureSpec
{
    /// <summary>
    /// A type assignable to any of these can never be part of a cached pipeline model: each is tied
    /// to one specific compilation/parse and is never equal to its counterpart from the next
    /// incremental run, even when it represents "the same" declaration. <see cref="TextSpan"/> is
    /// included alongside <see cref="Location"/> (S6 "locations"): a raw span is exactly as
    /// whitespace-sensitive as a full <see cref="Location"/>, so a cached model must carry neither --
    /// only the value-equatable <c>LocationSpec</c>/<c>LocationKey</c> pair belongs beside a model,
    /// never inside one. See <see cref="Akka.Serialization.V2.Generators.AkkaSerializerGenerator"/>'s
    /// own AkkaSerializerGenerator.Locations.cs file header for the full rule.
    /// </summary>
    private static readonly Type[] ForbiddenBaseTypes =
    {
        typeof(ISymbol),
        typeof(Compilation),
        typeof(SyntaxNode),
        typeof(SyntaxTree),
        typeof(Location),
        typeof(SyntaxReference),
        typeof(TextSpan)
    };

    private static IReadOnlyList<Type> ModelTypes { get; } = DiscoverModelTypes();

    [Fact(DisplayName = "Model type discovery should find the generator's internal pipeline models")]
    public void Should_discover_model_types()
    {
        // Not an exact-count pin (contrast GeneratorIncrementalScenariosSpec's tracking-step count,
        // which deliberately IS one) -- this only guards against the discovery mechanism itself
        // silently regressing to zero (for example if AkkaSerializerGenerator.Models.cs models ever
        // moved to a different namespace or stopped being nested under the generator class).
        ModelTypes.Should().HaveCountGreaterOrEqualTo(10);
    }

    [Fact(DisplayName = "No generator model type should hold a live Roslyn symbol, Compilation, syntax node/tree, or Location")]
    public void Model_types_should_be_symbol_free()
    {
        var violations = new List<string>();
        var visited = new HashSet<Type>();

        foreach (var modelType in ModelTypes)
            CheckSymbolFree(modelType, modelType.Name, visited, violations);

        violations.Should().BeEmpty(
            "every cached pipeline model must stay symbol-free for incremental caching to work, but found:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Every generator model type should have real value equality (an Equals/GetHashCode override, a record, or an enum)")]
    public void Model_types_should_have_value_equality()
    {
        var violations = new List<string>();

        foreach (var modelType in ModelTypes)
        {
            // Enums get correct, symbol-free, cross-compilation-stable equality for free from
            // System.Enum -- there is nothing for a hand-written override to improve on, and the
            // compiler will not let an enum declare one anyway.
            if (modelType.IsEnum)
                continue;

            // A record class/struct's compiler-generated Equals(object)/GetHashCode() ARE declared
            // directly on the type (DeclaredOnly reflection finds them, same as a hand-written
            // override), so no separate "is this a record" branch is needed here: the checks below
            // already accept either origin.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var declaresEquals = modelType.GetMethod(nameof(Equals), flags, binder: null, types: new[] { typeof(object) }, modifiers: null) != null;
            var declaresGetHashCode = modelType.GetMethod(nameof(GetHashCode), flags, binder: null, types: Type.EmptyTypes, modifiers: null) != null;

            if (!declaresEquals || !declaresGetHashCode)
            {
                violations.Add(
                    $"{modelType.Name} is not a record/enum and does not declare both Equals(object) and GetHashCode() " +
                    $"(Equals override present: {declaresEquals}, GetHashCode override present: {declaresGetHashCode}).");
            }
        }

        violations.Should().BeEmpty(string.Join(Environment.NewLine, violations));
    }

    private static void CheckSymbolFree(Type type, string path, HashSet<Type> visited, List<string> violations)
    {
        if (!visited.Add(type))
            return;

        foreach (var (memberName, memberType) in GetDataMembers(type))
        {
            var memberPath = $"{path}.{memberName}";

            foreach (var candidate in UnwrapCandidates(memberType))
            {
                if (ForbiddenBaseTypes.Any(forbidden => forbidden.IsAssignableFrom(candidate)))
                {
                    violations.Add($"{memberPath} : {memberType} carries a Roslyn type ({candidate}).");
                    continue;
                }

                // Recurse into any candidate that is itself one of the generator's own model types
                // (covers a model referencing another model, e.g. FieldInfo.Mapping : TypeMapping,
                // or ClosedGenericRegistrationInfo.Message : MessageInfo?) so a violation buried two
                // or more levels deep is still caught and reported at its own path.
                if (candidate.IsNestedAssembly && candidate.DeclaringType == typeof(AkkaSerializerGenerator))
                    CheckSymbolFree(candidate, memberPath, visited, violations);
            }
        }
    }

    /// <summary>Every instance field/property this type itself declares, skipping auto-property backing fields (the property covers the same type already).</summary>
    private static IEnumerable<(string Name, Type Type)> GetDataMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0)
                yield return (property.Name, property.PropertyType);
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                continue;

            yield return (field.Name, field.FieldType);
        }
    }

    /// <summary>
    /// A member's own type, plus (recursively) its unwrapped element type when the member is
    /// <see cref="Nullable{T}"/> or <see cref="ImmutableArray{T}"/> -- the two generic wrappers
    /// every model in this pipeline uses. A raw Roslyn type could otherwise hide inside either
    /// (for example <c>ImmutableArray&lt;ISymbol&gt;</c>) without ever appearing as a member's
    /// DECLARED type directly.
    /// </summary>
    private static IEnumerable<Type> UnwrapCandidates(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Nullable<>) || definition == typeof(ImmutableArray<>))
            {
                foreach (var candidate in UnwrapCandidates(type.GetGenericArguments()[0]))
                    yield return candidate;
            }
        }
    }

    private static List<Type> DiscoverModelTypes()
    {
        var generatorType = typeof(AkkaSerializerGenerator);
        return generatorType
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsNestedAssembly && t.Namespace == generatorType.Namespace)
            .ToList();
    }
}
