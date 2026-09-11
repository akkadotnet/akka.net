//-----------------------------------------------------------------------
// <copyright file="TypeKeySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Exercises <see cref="AkkaSerializerGenerator.TypeKey"/> directly -- the S4 architecture pass's
/// typed replacement for every former string-keyed dictionary lookup in the generator. Covers value
/// equality (metadata identity only; <see cref="AkkaSerializerGenerator.TypeKey.DisplayName"/> is
/// carried but never compared), <see cref="AkkaSerializerGenerator.TypeKey.Fold"/>, and the two
/// collision cases the metadata-name scheme exists to close: a nested type versus a
/// namespace-qualified type that render the same fully-qualified DISPLAY string, and two closed
/// constructions of the same generic definition over different type arguments.
/// </summary>
public sealed class TypeKeySpec
{
    [Fact(DisplayName = "TypeKey equality should compare only MetadataName and TypeArguments, ignoring DisplayName")]
    public void Equality_should_ignore_DisplayName()
    {
        var left = new AkkaSerializerGenerator.TypeKey("Ns.Foo", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::Ns.Foo");
        var right = new AkkaSerializerGenerator.TypeKey("Ns.Foo", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "an entirely different display string");

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.DisplayName.Should().Be("global::Ns.Foo");
        right.DisplayName.Should().Be("an entirely different display string");
        left.ToString().Should().Be("global::Ns.Foo");
    }

    [Fact(DisplayName = "TypeKey equality should require matching TypeArguments even when MetadataName matches")]
    public void Equality_should_require_matching_type_arguments()
    {
        var intArg = new AkkaSerializerGenerator.TypeKey("System.Int32", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "int");
        var stringArg = new AkkaSerializerGenerator.TypeKey("System.String", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "string");

        var wrapperOfInt = new AkkaSerializerGenerator.TypeKey("Ns.Wrapper`1", ImmutableArray.Create(intArg), "global::Ns.Wrapper<int>");
        var wrapperOfIntAgain = new AkkaSerializerGenerator.TypeKey("Ns.Wrapper`1", ImmutableArray.Create(intArg), "global::Ns.Wrapper<int>");
        var wrapperOfString = new AkkaSerializerGenerator.TypeKey("Ns.Wrapper`1", ImmutableArray.Create(stringArg), "global::Ns.Wrapper<string>");

        wrapperOfInt.Equals(wrapperOfIntAgain).Should().BeTrue();
        wrapperOfInt.GetHashCode().Should().Be(wrapperOfIntAgain.GetHashCode());
        wrapperOfInt.Equals(wrapperOfString).Should().BeFalse();
    }

    [Fact(DisplayName = "Fold should apply the same compact generated-member-name folding the generator has always produced")]
    public void Fold_should_match_existing_folding_behavior()
    {
        var orderRequest = new AkkaSerializerGenerator.TypeKey("Ns.OrderRequest", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::Ns.OrderRequest");
        var wrapperOfOrderRequest = new AkkaSerializerGenerator.TypeKey("Ns.Wrapper`1", ImmutableArray.Create(orderRequest), "global::Ns.Wrapper<global::Ns.OrderRequest>");

        wrapperOfOrderRequest.Fold().Should().Be("WrapperOrderRequest");
    }

    [Fact(DisplayName = "A nested type key and a namespace-qualified type key that render the same display text should be unequal")]
    public void Nested_type_key_should_not_collide_with_namespace_qualified_type_key()
    {
        // Both render "A.Outer.Inner" as a fully-qualified DISPLAY string -- the exact collision the
        // old string-keyed scheme could not tell apart. TypeKey's MetadataName carries '+' for a
        // nested type's containment and '.' for a namespace, so the two never compare equal even
        // though their DisplayName is identical.
        var nested = new AkkaSerializerGenerator.TypeKey("A.Outer+Inner", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::A.Outer.Inner");
        var namespaceQualified = new AkkaSerializerGenerator.TypeKey("A.Outer.Inner", ImmutableArray<AkkaSerializerGenerator.TypeKey>.Empty, "global::A.Outer.Inner");

        nested.DisplayName.Should().Be(namespaceQualified.DisplayName, "both must render the same display text for this to be a real collision case");
        nested.Equals(namespaceQualified).Should().BeFalse();
        nested.GetHashCode().Should().NotBe(namespaceQualified.GetHashCode());
    }

    [Fact(DisplayName = "TypeKey.FromSymbol should build '+'-joined metadata names for a real nested type, closing the collision against a real namespace-qualified sibling")]
    public void FromSymbol_should_close_the_nested_type_collision_for_real_symbols()
    {
        const string source = """
            namespace A
            {
                public class Outer
                {
                    public class Inner
                    {
                    }
                }
            }

            namespace A.NsQualified
            {
                public class Inner
                {
                }
            }
            """;

        var compilation = GeneratorTestHarness.Run(source).OutputCompilation;
        var nestedSymbol = compilation.GetTypeByMetadataName("A.Outer+Inner")
            ?? throw new InvalidOperationException("Could not resolve 'A.Outer+Inner' in the harness compilation.");
        var namespaceQualifiedSymbol = compilation.GetTypeByMetadataName("A.NsQualified.Inner")
            ?? throw new InvalidOperationException("Could not resolve 'A.NsQualified.Inner' in the harness compilation.");

        var nestedKey = AkkaSerializerGenerator.TypeKey.FromSymbol(nestedSymbol);
        var namespaceQualifiedKey = AkkaSerializerGenerator.TypeKey.FromSymbol(namespaceQualifiedSymbol);

        nestedKey.MetadataName.Should().Be("A.Outer+Inner");
        nestedKey.Equals(namespaceQualifiedKey).Should().BeFalse();
    }

    [Fact(DisplayName = "Wrapper<int> and Wrapper<string> keys should be unequal, while two Wrapper<int> keys should be equal")]
    public void Closed_generic_construction_keys_should_differ_by_type_argument()
    {
        const string source = """
            namespace GenericsSample
            {
                public sealed class Wrapper<T>
                {
                }
            }
            """;

        var compilation = GeneratorTestHarness.Run(source).OutputCompilation;
        var wrapperDefinition = compilation.GetTypeByMetadataName("GenericsSample.Wrapper`1")
            ?? throw new InvalidOperationException("Could not resolve 'GenericsSample.Wrapper`1' in the harness compilation.");
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);

        var wrapperOfIntOne = AkkaSerializerGenerator.TypeKey.FromSymbol(wrapperDefinition.Construct(intType));
        var wrapperOfIntTwo = AkkaSerializerGenerator.TypeKey.FromSymbol(wrapperDefinition.Construct(intType));
        var wrapperOfString = AkkaSerializerGenerator.TypeKey.FromSymbol(wrapperDefinition.Construct(stringType));

        wrapperOfIntOne.Equals(wrapperOfIntTwo).Should().BeTrue();
        wrapperOfIntOne.GetHashCode().Should().Be(wrapperOfIntTwo.GetHashCode());
        wrapperOfIntOne.Equals(wrapperOfString).Should().BeFalse();
    }

    [Fact(DisplayName = "FromSymbol with includeDisplayName=false should build a comparison-only key with an empty DisplayName that still compares equal to the full key")]
    public void FromSymbol_with_includeDisplayName_false_should_omit_display_name()
    {
        const string source = """
            namespace ComparisonSample
            {
                public interface IProtocol
                {
                }
            }
            """;

        var compilation = GeneratorTestHarness.Run(source).OutputCompilation;
        var symbol = compilation.GetTypeByMetadataName("ComparisonSample.IProtocol")
            ?? throw new InvalidOperationException("Could not resolve 'ComparisonSample.IProtocol' in the harness compilation.");

        var full = AkkaSerializerGenerator.TypeKey.FromSymbol(symbol);
        var comparisonOnly = AkkaSerializerGenerator.TypeKey.FromSymbol(symbol, includeDisplayName: false);

        full.DisplayName.Should().Be("global::ComparisonSample.IProtocol");
        comparisonOnly.DisplayName.Should().BeEmpty();
        full.Equals(comparisonOnly).Should().BeTrue("equality never reads DisplayName -- this is exactly what lets the AKKASG029 scan skip building one");
    }
}
