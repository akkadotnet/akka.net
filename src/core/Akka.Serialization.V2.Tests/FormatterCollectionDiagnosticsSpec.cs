//-----------------------------------------------------------------------
// <copyright file="FormatterCollectionDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Akka.Serialization.V2.Tests.Harness.DiagnosticSource;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Diagnostics for the shapes that stay unsupported once a formatter can serve a collection's element, key
/// or value (G-1): a collection shape the generator does not support, a member nothing handles, and the
/// diagnostic a collection reports when one member is unusable.
/// </summary>
public sealed class FormatterCollectionDiagnosticsSpec
{
    [Theory(DisplayName = "Should_ReportAKKASG003_When_CollectionShapeIsStillUnsupportedEvenWithAFormatter")]
    [InlineData("IImmutableDictionary<string, Foreign>")]
    [InlineData("IImmutableList<Foreign>")]
    [InlineData("IImmutableSet<Foreign>")]
    [InlineData("ImmutableSortedSet<Foreign>")]
    [InlineData("ImmutableSortedDictionary<string, Foreign>")]
    [InlineData("HashSet<Foreign>")]
    [InlineData("ISet<Foreign>")]
    [InlineData("IEnumerable<Foreign>")]
    [InlineData("Foreign[,]")]
    public void Should_ReportAKKASG003_When_CollectionShapeIsStillUnsupportedEvenWithAFormatter(string fieldType)
    {
        var run = Run(ForeignFormatterAttribute, $"[property: AkkaField(1)] {fieldType} Items");

        var diagnostic = run.Errors.Should().ContainSingle(d => d.Id == "AKKASG003").Subject;
        diagnostic.GetMessage(null).Should().Contain("'Items'").And.Contain("DiagnosticSample.Foreign");
        run.Result.GeneratedSources.Should().BeEmpty("a serializer with an error generates nothing");
    }

    [Theory(DisplayName = "Should_ReportAKKASG003WithTheFullFieldType_When_ACollectionHoldsATypeNoFormatterHandles")]
    [InlineData("List<Foreign>", "List<")]
    [InlineData("Foreign[]", "Foreign[]")]
    [InlineData("ImmutableArray<Foreign>", "ImmutableArray<")]
    [InlineData("Dictionary<string, Foreign>", "Dictionary<string, ")]
    [InlineData("Dictionary<Foreign, int>", "Dictionary<")]
    [InlineData("Dictionary<string, List<Foreign>>", "Dictionary<string, global::System.Collections.Generic.List<")]
    public void Should_ReportAKKASG003WithTheFullFieldType_When_ACollectionHoldsATypeNoFormatterHandles(string fieldType, string expectedTypeFragment)
    {
        // No formatter registered, no [AkkaSerializable] on Foreign: the diagnostic is the same one a
        // List<Foreign> got before collection positions learned about formatters.
        var run = Run(string.Empty, $"[property: AkkaField(1)] {fieldType} Items");

        var diagnostic = run.Errors.Should().ContainSingle(d => d.Id == "AKKASG003").Subject;
        diagnostic.GetMessage(null).Should().Contain("'Items'").And.Contain(expectedTypeFragment).And.Contain("DiagnosticSample.Foreign");
        run.Errors.Should().NotContain(d => d.Id == "AKKASG007", "the whole collection field is reported once, not its element separately");
    }

    [Fact(DisplayName = "Should_ReportAKKASG003_When_DictionaryKeyIsObjectEvenIfValueHasAFormatter")]
    public void Should_ReportAKKASG003_When_DictionaryKeyIsObjectEvenIfValueHasAFormatter()
    {
        var run = Run(ForeignFormatterAttribute, "[property: AkkaField(1)] Dictionary<object, Foreign> Items");

        run.Errors.Should().ContainSingle(d => d.Id == "AKKASG003");
    }

    [Fact(DisplayName = "Should_ReportAKKASG014_When_AnEnumWithAnUnsupportedUnderlyingTypeIsACollectionMember")]
    public void Should_ReportAKKASG014_When_AnEnumWithAnUnsupportedUnderlyingTypeIsACollectionMember()
    {
        var run = Run(ForeignFormatterAttribute, "[property: AkkaField(1)] Dictionary<Foreign, WideEnum> Items");

        // The enum is declared next to the message; the formatter on Foreign does not rescue the value.
        var diagnostic = run.Errors.Should().ContainSingle(d => d.Id == "AKKASG014").Subject;
        diagnostic.GetMessage(null).Should().Contain("WideEnum").And.Contain("long");
    }

    [Fact(DisplayName = "Should_ReportOnlyTheFirstUnusableMember_When_AKeyAndAValueAreBothUnusable")]
    public void Should_ReportOnlyTheFirstUnusableMember_When_AKeyAndAValueAreBothUnusable()
    {
        // Key first, as before: the unregistered key type decides (AKKASG003), the wide enum value does not add a second diagnostic.
        var run = Run(string.Empty, "[property: AkkaField(1)] Dictionary<Foreign, WideEnum> Items");

        run.Errors.Select(d => d.Id).Should().Equal("AKKASG003");
    }

    [Fact(DisplayName = "Should_AcceptTheFormatter_When_AFormatterIsRegisteredForAnUnsupportedForeignType")]
    public void Should_AcceptTheFormatter_When_AFormatterIsRegisteredForAnUnsupportedForeignType()
    {
        var run = Run(ForeignFormatterAttribute, "[property: AkkaField(1)] Foreign A, [property: AkkaField(2)] List<Foreign> B");

        run.Errors.Should().BeEmpty();
        run.Result.GeneratedSources.Keys.Should().Contain("SampleSerializer.AkkaSerialization.g.cs");
    }
}
