//-----------------------------------------------------------------------
// <copyright file="GeneratorIncrementalScenariosSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GeneratorIncrementalScenariosSpec
{
    private const string FixtureSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace ScenarioSample;

        public interface IAlphaProtocol
        {
        }

        public interface IBetaProtocol
        {
        }

        [AkkaSerializable(Manifest = "alpha-one-v1")]
        public sealed record AlphaOne([property: AkkaField(1)] string Name) : IAlphaProtocol;

        [AkkaSerializable(Manifest = "alpha-two-v1")]
        public sealed record AlphaTwo([property: AkkaField(1)] int Count) : IAlphaProtocol;

        [AkkaSerializable(Manifest = "alpha-three-v1")]
        public sealed record AlphaThree([property: AkkaField(1)] bool Flag) : IAlphaProtocol;

        [AkkaSerializer<IAlphaProtocol>("scenario-alpha", 170001)]
        public sealed partial class AlphaSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        [AkkaSerializable(Manifest = "beta-one-v1")]
        public sealed record BetaOne([property: AkkaField(1)] string Label) : IBetaProtocol;

        [AkkaSerializable(Manifest = "beta-two-v1")]
        public sealed record BetaTwo([property: AkkaField(1)] long Value) : IBetaProtocol;

        [AkkaSerializable(Manifest = "beta-three-v1")]
        public sealed record BetaThree([property: AkkaField(1)] double Ratio) : IBetaProtocol;

        [AkkaSerializer<IBetaProtocol>("scenario-beta", 170002)]
        public sealed partial class BetaSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    private const string UnrelatedSourceBefore = """
        namespace ScenarioSample.Unrelated;

        public static class Untouched
        {
            public static int Value => 1;
        }
        """;

    private const string UnrelatedSourceAfter = """
        namespace ScenarioSample.Unrelated;

        public static class Untouched
        {
            public static int Value => 2;
        }
        """;

    // As trivial an edit as SourceText allows while still being a distinct tree: one appended
    // trailing blank line, no token anywhere is touched.
    private static readonly string UnrelatedSourceWithTrivialKeystroke = UnrelatedSourceBefore + Environment.NewLine;

    private static readonly string FixtureWithAlphaTwoFieldRenamed = FixtureSource.Replace(
        "[property: AkkaField(1)] int Count) : IAlphaProtocol;",
        "[property: AkkaField(1)] int Total) : IAlphaProtocol;");

    private static readonly string FixtureWithCommentInsideBetaTwo = FixtureSource.Replace(
        "[property: AkkaField(1)] long Value) : IBetaProtocol;",
        "[property: AkkaField(1)] /* a trailing comment, no semantic change */ long Value) : IBetaProtocol;");

    private const string ExtraReferenceSource = """
        namespace ScenarioSample.ExtraReference;

        public static class Unused
        {
        }
        """;

    [Fact(DisplayName = "Tracked pipeline stages should match TrackingNames.All exactly (a new stage must update this spec)")]
    public void TrackingNames_should_match_expected_stage_count()
    {
        // Pins today's stage count. Adding a sixth (or removing one) named pipeline stage is a
        // deliberate architectural change -- this assertion makes it fail loudly here instead of
        // only surfacing as a silent gap in the scenarios below. ResolvedSerializers (the
        // per-serializer resolve stage from the S3 architecture pass) is the fifth: it was ADDED
        // downstream of the four stages above, which is why scenarios (a)/(c)/(d)/(e) below still
        // pin the exact same reasons for those four as before.
        AkkaSerializerGenerator.TrackingNames.All.Should().HaveCount(5);
        AkkaSerializerGenerator.TrackingNames.All.Should().BeEquivalentTo(new[]
        {
            AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            AkkaSerializerGenerator.TrackingNames.CollectedSerializers,
            AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            AkkaSerializerGenerator.TrackingNames.CollectedMessages,
            AkkaSerializerGenerator.TrackingNames.ResolvedSerializers
        });
    }

    // ------------------------------------------------------------------------------------------
    // The core finding, common to every scenario below: ForAttributeWithMetadataName's per-node
    // transform is joined against the Compilation (it needs a semantic model to confirm the
    // attribute's full metadata name), so its cache key is COMPILATION-WIDE, not per-file or
    // per-node. Replacing ANY syntax tree in the compilation -- even one the generator never looks
    // at -- gives every attributed node a new input identity, so EVERY node's extraction transform
    // re-executes. A node whose resulting model still compares equal to its previous run reports
    // Unchanged (recomputed, but the same value), never Cached (skipped without recomputing); only
    // a node whose model actually differs reports Modified. Nothing at the per-node
    // (ExtractedSerializers/ExtractedMessages) level is ever Cached in any scenario below -- the
    // exact same "every node reruns" profile shows up whether the edit lands in the tracked file,
    // an unrelated file, or is a single added reference. Only downstream, at the Collect() level,
    // does the pipeline recover a Cached result -- and only when EVERY individual element that
    // feeds it is Unchanged/Cached; one truly Modified element (scenario (b)) is enough to make the
    // whole collected array report Modified instead.
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Scenario (a): editing an unrelated file reuses every tracked stage and re-emits no file")]
    public void Scenario_a_unrelated_file_edited()
    {
        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceBefore) },
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceAfter) });

        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // ResolvedSerializers' own input (serializers.Combine(messages)) is unchanged (both
        // component values are the SAME cached references as before), so the SelectMany transform
        // is skipped entirely for both serializers -- Cached, not merely Unchanged.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // TARGET: none -- this IS the caching discipline PR 1 pins as the baseline; PRs 2, 3, 5, 6
        // must not regress it. No hint name's text should change.
        ChangedHintNames(result).Should().BeEmpty();
    }

    [Fact(DisplayName = "Scenario (b): renaming one message's field re-emits only the serializer that owns it")]
    public void Scenario_b_message_field_renamed()
    {
        var result = GeneratorTestHarness.RunIncremental(FixtureSource, FixtureWithAlphaTwoFieldRenamed);

        // Same compilation-wide rerun as every other scenario; the only difference is that
        // AlphaTwo's OWN extracted model genuinely differs (its field name changed), so its node
        // alone reports Modified. That is the ONE cascading effect that reaches CollectedMessages:
        // an array with even one Modified element cannot be recognized as Cached, so the whole
        // array reports Modified -- unlike (a)/(c)/(d)/(e), where every element is merely Unchanged
        // and Collect() lands on Cached.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Modified, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Modified);

        // THE SCENARIO FLIP (S3): CollectedMessages changing forces the ResolvedSerializers
        // SelectMany transform to rerun for EVERY serializer (its input, serializers.Combine(messages),
        // changed) -- but AlphaSerializer is declared first and BetaSerializer second, and only
        // AlphaSerializer's resolve actually OWNS AlphaTwo, so only ITS resulting ResolvedSerializer
        // differs (Modified). BetaSerializer's resolved model, though recomputed, compares EQUAL to
        // its previous run (Unchanged) -- ResolvedSerializer.ResolvedMessagesByType is deliberately
        // scoped to each serializer's OWN reachable messages (see BuildResolvedMessageTable), so it
        // is never poisoned by an unrelated serializer's message.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Modified, IncrementalStepRunReason.Unchanged);

        // Because RegisterSourceOutput is now registered directly on the ResolvedSerializers values
        // provider (one independent output per serializer, not one output over the whole collected
        // pair), BetaSerializer's Unchanged resolved model means its AddSource callback is never
        // re-invoked at all (Cached) -- unlike the pre-S3 architecture, where EmitSerializers's
        // single RegisterSourceOutput re-executed for BOTH serializers on any message edit and
        // merely happened to re-emit byte-identical text for Beta. Only AlphaSerializer's generated
        // file changes here.
        ChangedHintNames(result).Should().BeEquivalentTo(new[] { "AlphaSerializer.AkkaSerialization.g.cs" });
    }

    [Fact(DisplayName = "Scenario (c): a comment added inside one message's declaration reuses every tracked stage and re-emits no file")]
    public void Scenario_c_comment_added_inside_message_body()
    {
        var result = GeneratorTestHarness.RunIncremental(FixtureSource, FixtureWithCommentInsideBetaTwo);

        // A comment carries no semantic information, so BetaTwo's recomputed model -- like every
        // other node's -- compares EQUAL to its previous run. This scenario's tracked-step profile
        // is therefore indistinguishable from scenario (a)'s (an edit to a wholly unrelated file):
        // proof that the "everything reruns" behavior described above is driven by the Compilation
        // changing identity, not by whether the edit is textually "inside" a tracked declaration.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): ResolvedSerializers' input is unchanged, so both serializers' resolve
        // steps are skipped entirely (Cached).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // TARGET: unchanged from today -- a comment-only edit re-emitting nothing is already the
        // desired behavior; no migration PR needs to touch this.
        ChangedHintNames(result).Should().BeEmpty();
    }

    [Fact(DisplayName = "Scenario (d): a trivial keystroke in an unrelated file still re-runs the whole-compilation coverage scan")]
    public void Scenario_d_trivial_keystroke_in_unrelated_file()
    {
        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceBefore) },
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceWithTrivialKeystroke) });

        // Same profile as (a)/(c): a single appended blank line in a file the generator never looks
        // at is still enough to make every node's extraction transform recompute (Unchanged), and
        // Collect() still lands on Cached since nothing actually differs.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): ResolvedSerializers' input is unchanged, so both serializers' resolve
        // steps are skipped entirely (Cached).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        ChangedHintNames(result).Should().BeEmpty();

        // TARGET: the coverage scan (AKKASG029, ReportProtocolCoverage) is registered on
        // resolvedSerializers.Combine(context.CompilationProvider) with NO tracking name of its
        // own -- it is not gated by the same equality contract as the five named stages above, and
        // is not observable through GetRunResult().Results[0].TrackedSteps at all. OBSERVED today:
        // it produces the SAME (empty) diagnostics both before and after this trivial edit -- the
        // fixture is coverage-clean -- which is the externally-visible half of "it still re-runs
        // every time": the CompilationProvider input this output depends on changes identity on
        // every edit, so the scan re-executes on this one too, it just has nothing new to report.
        // As of S3, this scan's per-serializer GATE check is read straight off the cached
        // ResolvedSerializer instead of being recomputed -- only the whole-compilation scan itself
        // still re-runs every time, which is unavoidable (it genuinely needs the live Compilation).
        result.Before.CompileDiagnostics.Where(d => d.Id == "AKKASG029").Should().BeEmpty();
        result.After.CompileDiagnostics.Where(d => d.Id == "AKKASG029").Should().BeEmpty();
        result.Before.RunResult.Diagnostics.Where(d => d.Id == "AKKASG029").Should().BeEmpty();
        result.After.RunResult.Diagnostics.Where(d => d.Id == "AKKASG029").Should().BeEmpty();
    }

    [Fact(DisplayName = "Scenario (e): adding an unused metadata reference reuses every tracked stage and re-emits no file")]
    public void Scenario_e_metadata_reference_added()
    {
        var extraReference = GeneratorTestHarness.CompileToReference(ExtraReferenceSource, "ScenarioExtraReferenceAssembly");

        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource) },
            new[] { new SourceFile("Main.cs", FixtureSource) },
            beforeExtraReferences: Array.Empty<MetadataReference>(),
            afterExtraReferences: new[] { extraReference });

        // Same profile again: adding a reference nothing in the fixture uses still changes the
        // Compilation's identity, so every node recomputes (Unchanged) and Collect() lands on
        // Cached since no extracted model actually differs.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): ResolvedSerializers' input is unchanged, so both serializers' resolve
        // steps are skipped entirely (Cached).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // TARGET: unchanged from today -- an unused added reference re-emitting nothing is already
        // the desired behavior; no migration PR needs to touch this.
        ChangedHintNames(result).Should().BeEmpty();
    }

    private static void AssertReasons(IncrementalGeneratorRunResult result, string trackingName, params IncrementalStepRunReason[] expected)
    {
        var actual = ReasonsFor(result, trackingName);
        actual.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering(),
            $"tracked step '{trackingName}' reasons were [{string.Join(", ", actual)}]");
    }

    private static List<IncrementalStepRunReason> ReasonsFor(IncrementalGeneratorRunResult result, string trackingName)
    {
        var trackedSteps = result.After.RunResult.Results.Single().TrackedSteps;
        if (!trackedSteps.TryGetValue(trackingName, out var steps))
            return new List<IncrementalStepRunReason>();

        return steps.SelectMany(step => step.Outputs.Select(output => output.Reason)).ToList();
    }

    private static ImmutableHashSet<string> ChangedHintNames(IncrementalGeneratorRunResult result)
    {
        var allNames = result.Before.GeneratedSources.Keys.Concat(result.After.GeneratedSources.Keys).ToImmutableHashSet();
        return allNames.Where(name =>
                !result.Before.GeneratedSources.TryGetValue(name, out var beforeText) ||
                !result.After.GeneratedSources.TryGetValue(name, out var afterText) ||
                !string.Equals(beforeText, afterText, StringComparison.Ordinal))
            .ToImmutableHashSet();
    }
}
