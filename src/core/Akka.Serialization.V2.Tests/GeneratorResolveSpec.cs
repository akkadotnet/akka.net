//-----------------------------------------------------------------------
// <copyright file="GeneratorResolveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Exercises <see cref="AkkaSerializerGenerator.ResolveSerializerForTests"/> -- the test-only entry
/// point for <see cref="AkkaSerializerGenerator.ResolveSerializer"/>, the S3 architecture pass's
/// per-serializer resolve stage -- directly on models, with no <see cref="SourceProductionContext"/>,
/// driver, or generator run. Message models come from <see cref="AkkaSerializerGenerator.ParseMessageForTests"/>
/// (same as <see cref="GeneratorValidatorSpec"/>); <see cref="AkkaSerializerGenerator.SerializerInfo"/>
/// values are hand-built. Covers the shape of the resolved model itself (closed set order, top-level
/// set, reachable set, union plan) and the equality property the whole S3 caching story depends on:
/// two resolves over equal inputs are equal, and a change to a message an unrelated serializer does
/// not own leaves that serializer's own resolved model unaffected.
/// </summary>
public sealed class GeneratorResolveSpec
{
    [Fact(DisplayName = "ResolveSerializer should build the top-level closed set in declaration order")]
    public void ResolveSerializer_should_build_top_level_closed_set_in_declaration_order()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "third-v1")]
            public sealed record Third([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializable(Manifest = "first-v1")]
            public sealed record First([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializable(Manifest = "second-v1")]
            public sealed record Second([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var third = ParseMessage(compilation, "ResolveSample.Third");
        var first = ParseMessage(compilation, "ResolveSample.First");
        var second = ParseMessage(compilation, "ResolveSample.Second");

        // Declared in the order Third, First, Second -- the closed set must preserve exactly that
        // order (the same order the emitted Manifest/Serialize/Deserialize switches have always
        // used), not alphabetical or manifest order.
        var resolved = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(third, first, second));

        resolved.IsEmittable.Should().BeTrue();
        resolved.TopLevelMessages.Members.Select(m => m.TypeFullName).Should().ContainInOrder(
            third.FullyQualifiedName, first.FullyQualifiedName, second.FullyQualifiedName);
        resolved.TopLevelMessages.Members.Select(m => m.Manifest).Should().ContainInOrder("third-v1", "first-v1", "second-v1");
        resolved.TopLevelMessages.Members.Select(m => m.MethodName).Should().ContainInOrder("Third", "First", "Second");
    }

    [Fact(DisplayName = "ResolveSerializer should compute the reachable set, including a nested message that is not itself top-level")]
    public void ResolveSerializer_should_compute_reachable_set()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Nested([property: AkkaField(1)] string Value);

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Nested Inner) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var outer = ParseMessage(compilation, "ResolveSample.Outer");
        var nested = ParseMessage(compilation, "ResolveSample.Nested");

        var resolved = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(outer, nested));

        // Nested is reachable (Outer's field references it) but is NOT top-level -- it does not
        // implement IProtocol.
        resolved.TopLevelMessages.Members.Select(m => m.TypeFullName).Should().Equal(outer.FullyQualifiedName);
        resolved.ReachableMessages.Select(m => m.FullyQualifiedName).Should().BeEquivalentTo(new[] { outer.FullyQualifiedName, nested.FullyQualifiedName });
        resolved.ResolvedMessagesByType.Keys.Should().BeEquivalentTo(new[] { outer.FullyQualifiedName, nested.FullyQualifiedName });
    }

    [Fact(DisplayName = "ResolveSerializer should plan one union helper with members in declared order")]
    public void ResolveSerializer_should_plan_union_helper()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IProtocol
            {
            }

            [AkkaUnion(typeof(MemberB), typeof(MemberA))]
            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "member-b-v1")]
            public sealed record MemberB([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "member-a-v1")]
            public sealed record MemberA([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] IEvent Event) : IProtocol;
            """;

        var compilation = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilation));
        var outer = ParseMessage(compilation, "ResolveSample.Outer");
        var memberA = ParseMessage(compilation, "ResolveSample.MemberA");
        var memberB = ParseMessage(compilation, "ResolveSample.MemberB");

        var resolved = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(outer, memberA, memberB));

        resolved.UnionPlan.Helpers.Should().HaveCount(1);
        var helper = resolved.UnionPlan.Helpers[0];
        helper.HelperName.Should().Be("Union_IEvent");
        helper.FieldTypeFullName.Should().Be(outer.Fields.Single().TypeFullName);

        // [AkkaUnion(typeof(MemberB), typeof(MemberA))] declares MemberB first -- the plan's member
        // order must match the attribute's declaration order, not source-declaration or alphabetical
        // order.
        helper.Members.Members.Select(m => m.TypeFullName).Should().ContainInOrder(memberB.FullyQualifiedName, memberA.FullyQualifiedName);
        helper.Members.Members.Select(m => m.Manifest).Should().ContainInOrder("member-b-v1", "member-a-v1");
        helper.Members.Members.Select(m => m.MethodName).Should().ContainInOrder("MemberB", "MemberA");
    }

    [Fact(DisplayName = "Two resolves over equal inputs should be equal")]
    public void ResolveSerializer_should_be_value_equatable_across_equal_inputs()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        // Two SEPARATE compilations of the identical source: proves the resolved model's equality
        // is genuinely structural (never relies on symbol/object identity surviving from one
        // compilation into the next), exactly what the incremental pipeline needs across two
        // generator runs.
        var compilationOne = Compile(source);
        var compilationTwo = Compile(source);
        var serializer = BuildSerializerInfo(ProtocolFullName(compilationOne));

        var resolvedOne = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(ParseMessage(compilationOne, "ResolveSample.Outer")));
        var resolvedTwo = AkkaSerializerGenerator.ResolveSerializerForTests(serializer, ImmutableArray.Create(ParseMessage(compilationTwo, "ResolveSample.Outer")));

        resolvedOne.Equals(resolvedTwo).Should().BeTrue();
        resolvedOne.GetHashCode().Should().Be(resolvedTwo.GetHashCode());
    }

    [Fact(DisplayName = "A change to an unrelated message's field should yield an equal resolved model for a serializer that does not own it")]
    public void ResolveSerializer_should_be_unaffected_by_an_unowned_message_change()
    {
        const string sourceBefore = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IAlphaProtocol
            {
            }

            public interface IBetaProtocol
            {
            }

            [AkkaSerializable(Manifest = "alpha-v1")]
            public sealed record AlphaMessage([property: AkkaField(1)] int Count) : IAlphaProtocol;

            [AkkaSerializable(Manifest = "beta-v1")]
            public sealed record BetaMessage([property: AkkaField(1)] string Label) : IBetaProtocol;
            """;

        // AlphaMessage's field renamed -- the edit scenario this resolve stage exists to isolate.
        // BetaMessage is untouched.
        const string sourceAfter = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ResolveSample;

            public interface IAlphaProtocol
            {
            }

            public interface IBetaProtocol
            {
            }

            [AkkaSerializable(Manifest = "alpha-v1")]
            public sealed record AlphaMessage([property: AkkaField(1)] int Total) : IAlphaProtocol;

            [AkkaSerializable(Manifest = "beta-v1")]
            public sealed record BetaMessage([property: AkkaField(1)] string Label) : IBetaProtocol;
            """;

        var compilationBefore = Compile(sourceBefore);
        var compilationAfter = Compile(sourceAfter);

        var betaSerializer = BuildSerializerInfo(ProtocolFullName(compilationBefore, "ResolveSample.IBetaProtocol"), className: "BetaSerializer");

        var messagesBefore = ImmutableArray.Create(
            ParseMessage(compilationBefore, "ResolveSample.AlphaMessage"),
            ParseMessage(compilationBefore, "ResolveSample.BetaMessage"));
        var messagesAfter = ImmutableArray.Create(
            ParseMessage(compilationAfter, "ResolveSample.AlphaMessage"),
            ParseMessage(compilationAfter, "ResolveSample.BetaMessage"));

        // Sanity check: AlphaMessage really did change between the two message arrays -- otherwise
        // this test would trivially pass for the wrong reason.
        messagesBefore[0].Should().NotBe(messagesAfter[0]);

        var betaResolvedBefore = AkkaSerializerGenerator.ResolveSerializerForTests(betaSerializer, messagesBefore);
        var betaResolvedAfter = AkkaSerializerGenerator.ResolveSerializerForTests(betaSerializer, messagesAfter);

        betaResolvedBefore.Equals(betaResolvedAfter).Should().BeTrue(
            "BetaSerializer does not own AlphaMessage, so its resolved model must not change shape when AlphaMessage does");
        betaResolvedBefore.GetHashCode().Should().Be(betaResolvedAfter.GetHashCode());
    }

    private static Compilation Compile(string source)
    {
        return GeneratorTestHarness.Run(source).OutputCompilation;
    }

    private static string ProtocolFullName(Compilation compilation, string metadataName = "ResolveSample.IProtocol")
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{metadataName}' in the harness compilation.");
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static AkkaSerializerGenerator.MessageInfo ParseMessage(Compilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Could not resolve '{metadataName}' in the harness compilation.");
        return AkkaSerializerGenerator.ParseMessageForTests(symbol, compilation)
            ?? throw new InvalidOperationException($"'{metadataName}' was not recognized as [AkkaSerializable].");
    }

    private static AkkaSerializerGenerator.SerializerInfo BuildSerializerInfo(string protocolTypeFullName, string className = "TestSerializer")
    {
        return new AkkaSerializerGenerator.SerializerInfo(
            ns: "ResolveSample",
            className: className,
            fullyQualifiedName: $"global::ResolveSample.{className}",
            name: "test-serializer",
            serializerId: 1,
            protocolTypeFullName: protocolTypeFullName,
            protocolTypeIsInterface: true,
            declaredAccessibility: Accessibility.Public,
            formatters: ImmutableArray<AkkaSerializerGenerator.FormatterInfo>.Empty,
            closedGenericRegistrations: ImmutableArray<AkkaSerializerGenerator.ClosedGenericRegistrationInfo>.Empty,
            isPartial: true,
            isGeneric: false,
            derivesFromAkkaSerializerBase: true);
    }
}
