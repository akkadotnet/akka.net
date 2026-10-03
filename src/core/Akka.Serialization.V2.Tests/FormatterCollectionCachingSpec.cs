//-----------------------------------------------------------------------
// <copyright file="FormatterCollectionCachingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Cache-safety for formatter-handled collection members: a collection mapping now carries the
/// <c>FormatterInfo</c> of each formatted element, key or value, so that model must stay symbol-free
/// and value-equatable, and a formatter registration change must still re-resolve the collection.
/// </summary>
public sealed class FormatterCollectionCachingSpec
{
    [Fact(DisplayName = "Should_ReuseEveryCachedStage_When_AnUnrelatedTypeIsAddedToTheSameFile")]
    public void Should_ReuseEveryCachedStage_When_AnUnrelatedTypeIsAddedToTheSameFile()
    {
        var before = GeneratorGoldenFormatterCollectionSpec.Source;
        var after = before + "\npublic static class Untouched { public static int Value => 1; }\n";

        var run = GeneratorTestHarness.RunIncremental(before, after);

        run.Before.GeneratedSources.Should().NotBeEmpty("the baseline must actually generate, or this guard passes vacuously");
        run.After.GeneratedSources.Should().Equal(run.Before.GeneratedSources);

        var trackedSteps = run.After.RunResult.Results.Single().TrackedSteps;
        foreach (var trackingName in AkkaSerializerGenerator.TrackingNames.All)
        {
            foreach (var (_, reason) in trackedSteps[trackingName].SelectMany(step => step.Outputs))
            {
                (reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged)
                    .Should().BeTrue($"step '{trackingName}' must not recompute, but reported '{reason}'");
            }
        }
    }

    [Fact(DisplayName = "Should_ReportAKKASG003_When_TheFormatterOfACollectionMemberIsRemovedOnARerun")]
    public void Should_ReportAKKASG003_When_TheFormatterOfACollectionMemberIsRemovedOnARerun()
    {
        var before = GeneratorGoldenFormatterCollectionSpec.Source;
        var after = before.Replace("[AkkaSerializerFormatter<Tag, TagFormatter>]", string.Empty);

        var run = GeneratorTestHarness.RunIncremental(before, after);

        run.Before.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.After.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG003" && d.GetMessage(null).Contains("'MapOfTagLists'"));
        run.After.GeneratedSources.Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_GenerateAgain_When_TheFormatterOfACollectionMemberIsRestoredOnARerun")]
    public void Should_GenerateAgain_When_TheFormatterOfACollectionMemberIsRestoredOnARerun()
    {
        var withFormatter = GeneratorGoldenFormatterCollectionSpec.Source;
        var withoutFormatter = withFormatter.Replace("[AkkaSerializerFormatter<Tag, TagFormatter>]", string.Empty);

        var run = GeneratorTestHarness.RunIncremental(withoutFormatter, withFormatter);

        run.Before.GeneratedSources.Should().BeEmpty();
        run.After.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.After.GeneratedSources.Keys.Should().Contain("FormatterPositionsSerializer.AkkaSerialization.g.cs");
    }
}
