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

    // Unlike ExtraReferenceSource above, this one USES a type from Akka.Serialization.V2
    // ([AkkaSerializable] on MarkerMessage), so the compiled assembly's own AssemblyRef table
    // genuinely names Akka.Serialization.V2 -- the shape CompilationFacts.ReferencedAssembliesUsingV2
    // is meant to detect. MarkerMessage implements neither IAlphaProtocol nor IBetaProtocol, so
    // adding this reference cannot also perturb either protocol's local-implementor bucket.
    private const string ExtraReferenceSourceUsingV2 = """
        using Akka.Serialization.V2;

        namespace ScenarioSample.ExtraReferenceWithV2;

        [AkkaSerializable(Manifest = "extra-v2-marker-v1")]
        public sealed class MarkerMessage
        {
        }
        """;

    [Fact(DisplayName = "Tracked pipeline stages should match TrackingNames.All exactly (a new stage must update this spec)")]
    public void TrackingNames_should_match_expected_stage_count()
    {
        // Pins today's stage count. Adding a ninth (or removing one) named pipeline stage is a
        // deliberate architectural change -- this assertion makes it fail loudly here instead of
        // only surfacing as a silent gap in the scenarios below. ResolvedSerializers (the
        // per-serializer resolve stage from the S3 architecture pass) and CompilationFacts (the
        // whole-compilation facts stage from the S5 architecture pass) predate S6. S6 "locations"
        // adds two: SerializerSchemas/MessageSchemas split ExtractedSerializers'/ExtractedMessages'
        // raw output (schema vs location bag) -- see each constant's own doc comment in
        // AkkaSerializerGenerator.cs. A locations-only counterpart to each (SerializerLocations/
        // MessageLocations) existed briefly during development but was removed: a per-node Select
        // for a value read back only once, batched, cost more than it saved -- the merged location
        // bag (allLocations, in Initialize) now collects the raw ExtractedSerializer/ExtractedMessage
        // values directly instead.
        AkkaSerializerGenerator.TrackingNames.All.Should().HaveCount(8);
        AkkaSerializerGenerator.TrackingNames.All.Should().BeEquivalentTo(new[]
        {
            AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            AkkaSerializerGenerator.TrackingNames.CollectedSerializers,
            AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            AkkaSerializerGenerator.TrackingNames.CollectedMessages,
            AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            AkkaSerializerGenerator.TrackingNames.CompilationFacts
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
    // a node whose model actually differs reports Modified. Nothing at the RAW per-node
    // (ExtractedSerializers/ExtractedMessages) level is ever Cached in any scenario below -- the
    // exact same "every node reruns" profile shows up whether the edit lands in the tracked file,
    // an unrelated file, or is a single added reference. Only downstream, at the Collect() level,
    // does the pipeline recover a Cached result -- and only when EVERY individual element that
    // feeds it is Unchanged/Cached; one truly Modified element (scenario (b)) is enough to make the
    // whole collected array report Modified instead.
    //
    // S6 "locations" adds a wrinkle: the RAW per-node output now carries a location bag alongside
    // the schema, and that bag IS whitespace-sensitive (see LocationSpec's own doc comment). So the
    // raw stage itself can report Modified purely because a span shifted, even when the SCHEMA is
    // untouched (scenario (c)) or even when the location bag's KEY set changes from a same-length
    // rename (scenario (b), where field name IS the key). The schemas-only projection immediately
    // strips that back out, which is what keeps Collect/Resolve/Emit exactly as cached as before S6
    // -- see each scenario's own comments for the concrete per-element reasons. The merged location
    // bag (allLocations) that a raw-stage Modified eventually feeds is NOT one of the tracked stages
    // below -- it collects the raw ExtractedSerializer/ExtractedMessage values directly, with no
    // separate per-node Select of its own -- so it is not asserted on here either; the scenarios only
    // pin what happens at the RAW/schemas/Collect/Resolve level.
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Scenario (a): editing an unrelated file reuses every tracked stage and re-emits no file")]
    public void Scenario_a_unrelated_file_edited()
    {
        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceBefore) },
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceAfter) });

        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);

        // S6: neither serializer's location bag shifts (nothing in Main.cs moved), so the
        // schemas-only projection of ExtractedSerializers reports the same reason as the raw stage.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);

        // S6: same reasoning as the serializer projection above -- no message's location bag shifts.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // ResolvedSerializers' own input (serializers.Combine(messages)) is unchanged (both
        // component values are the SAME cached references as before), so the SelectMany transform
        // is skipped entirely for both serializers -- Cached, not merely Unchanged.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // S5: CompilationFacts' Select reruns every time (it combines context.CompilationProvider
        // directly, which never itself compares equal across an edit), but neither protocol's
        // local-implementor set nor the referenced-assembly-using-V2 list is touched by an edit to
        // an unrelated file, so its OUTPUT compares equal to the previous run -- Unchanged (ran,
        // but equal), not Cached (which would mean it never ran at all).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Unchanged);

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
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Modified, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);

        // S6: "Count" -> "Total" is the same length, so no message's LOCATION SPAN actually moves --
        // but AlphaTwo's location bag is keyed by FIELD NAME (LocationKey.Member), and that name
        // itself changed, so the bag's key set differs from before ("Count" gone, "Total" added).
        // AlphaTwo's raw ExtractedMessages element is therefore Modified for two independent reasons
        // (schema AND location both differ), which is exactly what the assertion above already
        // shows; MessageSchemas reports the SCHEMA half of that on its own.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Modified, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
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

        // S5: renaming a field changes nothing CompilationFacts tracks -- AlphaTwo is marked
        // [AkkaSerializable] either way, so it was never a candidate for either protocol's
        // local-implementor bucket, and no reference changed. Unchanged, exactly like scenario (a).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Unchanged);

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

        // THE S6 SCENARIO FLIP: pre-S6, a comment carries no semantic information, so this scenario's
        // tracked-step profile was indistinguishable from scenario (a)'s. S6 adds a location bag to
        // the raw ExtractedMessages/ExtractedSerializers output, and a location bag IS
        // whitespace-sensitive by design (that is the whole point -- it must track real spans, so a
        // diagnostic points at the right place). The inserted comment sits inside BetaTwo, so every
        // declaration PHYSICALLY AFTER it in Main.cs -- BetaTwo itself, BetaThree, and BetaSerializer
        // -- gets a real, different absolute text position; nothing schema-level about any of them
        // changed. AlphaOne/AlphaTwo/AlphaThree/AlphaSerializer/BetaOne sit BEFORE the comment and are
        // completely unaffected.
        //
        // BetaSerializer's raw stage is Modified (its location bag shifted), but its SCHEMA is
        // unaffected -- Unchanged, not Cached, since the stage still recomputed and produced an equal
        // SerializerInfo.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Modified);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged);

        // The schemas-only projection feeds a Collect() whose every element is Cached/Unchanged
        // (equal to last run), so CollectedSerializers itself lands on Cached -- the raw stage's
        // Modified-ness for BetaSerializer (its location bag shifted) never reaches here.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);

        // BetaTwo (the edited message) and BetaThree (physically after it) both shift; BetaOne and
        // every Alpha message do not.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Modified, IncrementalStepRunReason.Modified);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): both Collect()'d arrays are Cached, so ResolvedSerializers' own input
        // never changed identity -- both serializers' resolve steps are skipped entirely (Cached).
        // THIS is the property S6 exists to preserve: a location-only edit reaches neither Resolve nor
        // Emit.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // Same as scenario (a): a comment carries no semantic information CompilationFacts cares
        // about either, so it reruns but compares equal -- Unchanged.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Unchanged);

        // TARGET: unchanged from today -- a comment-only edit re-emitting nothing is already the
        // desired behavior; S6 must not regress it, and does not: no file is re-emitted even though
        // three raw extraction nodes (BetaSerializer, BetaTwo, BetaThree) report Modified.
        ChangedHintNames(result).Should().BeEmpty();
    }

    [Fact(DisplayName = "Scenario (d): a trivial keystroke in an unrelated file still re-runs the whole-compilation coverage scan")]
    public void Scenario_d_trivial_keystroke_in_unrelated_file()
    {
        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceBefore) },
            new[] { new SourceFile("Main.cs", FixtureSource), new SourceFile("Unrelated.cs", UnrelatedSourceWithTrivialKeystroke) });

        // Same profile as (a): a single appended blank line in a file the generator never looks at
        // is still enough to make every node's extraction transform recompute (Unchanged), and
        // Collect() still lands on Cached since nothing actually differs. The edit is in a file
        // Main.cs never shares -- no location in it shifts either, so the S6 schemas-only projection
        // stays Cached for every element too (contrast scenario (c), where the edit lands INSIDE the
        // tracked file).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): ResolvedSerializers' input is unchanged, so both serializers' resolve
        // steps are skipped entirely (Cached).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // TARGET (S5), and this scenario's namesake pin: CompilationFacts -- the stage that now
        // carries the AKKASG029 whole-compilation coverage walk -- reruns (its Select combines
        // context.CompilationProvider directly, which never itself compares equal across an edit)
        // but produces the SAME implementor lists and the SAME referenced-assembly-using-V2 list as
        // before, since this edit lands in a file the generator never looks at. Its output therefore
        // reports Unchanged, not Modified: this PIN is what proves the walk's OUTPUT, not just the
        // walk's cost, is now shared/cached machinery rather than a bespoke non-tracked re-scan. If
        // a future change makes this report Modified instead, that is a real behavior change to call
        // out here, not a silent regression.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Unchanged);
        ChangedHintNames(result).Should().BeEmpty();

        // The coverage output itself (AKKASG029, ReportProtocolCoverage) is still registered on
        // resolvedSerializers.Combine(compilationFacts) with NO tracking name of its own -- it is
        // not gated by the same equality contract as the six named stages above, and is not
        // observable through GetRunResult().Results[0].TrackedSteps at all. OBSERVED today: it
        // produces the SAME (empty) diagnostics both before and after this trivial edit -- the
        // fixture is coverage-clean -- which is the externally-visible half of "it still re-runs
        // every time": the callback itself is invoked on every edit (RegisterSourceOutput has no
        // caching of its own), it just now does far less work when it runs, since CompilationFacts
        // already did the whole-compilation walk once, for every serializer, upstream of it.
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
        // Cached since no extracted model actually differs. No text in Main.cs moved, so the S6
        // schemas-only projection is Cached too.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // Same as scenario (a): ResolvedSerializers' input is unchanged, so both serializers' resolve
        // steps are skipped entirely (Cached).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // TARGET (S5): the added assembly's own emitted metadata never actually references
        // Akka.Serialization.V2 (ExtraReferenceSource declares a plain class that uses none of its
        // types, and an unused MetadataReference does not turn into an AssemblyRef entry in the
        // compiled PE), so CompilationFacts.ReferencedAssembliesUsingV2 -- and therefore every other
        // fact -- compares equal to the previous run: Unchanged, not Modified. Contrast
        // Scenario_e2_metadata_reference_that_references_v2_added below, where the added reference
        // DOES touch V2 and this same stage reports Modified instead.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Unchanged);

        // TARGET: unchanged from today -- an unused added reference re-emitting nothing is already
        // the desired behavior; no migration PR needs to touch this.
        ChangedHintNames(result).Should().BeEmpty();
    }

    [Fact(DisplayName = "Scenario (e2): adding a metadata reference that itself references Akka.Serialization.V2 changes CompilationFacts but re-emits no file")]
    public void Scenario_e2_metadata_reference_that_references_v2_added()
    {
        // Unlike ExtraReferenceSource (scenario (e), above), this assembly actually USES a type from
        // Akka.Serialization.V2 ([AkkaSerializable] on MarkerMessage) -- so the C# compiler DOES
        // emit an AssemblyRef to Akka.Serialization.V2 into this compiled assembly's own metadata,
        // making it exactly the shape CompilationFacts.ReferencedAssembliesUsingV2 (Decision 19's
        // pre-filter) exists to detect. MarkerMessage implements neither IAlphaProtocol nor
        // IBetaProtocol, so this cannot also change either protocol's local-implementor bucket --
        // the ONLY fact this edit can move is the referenced-assembly-name list itself.
        var extraReference = GeneratorTestHarness.CompileToReference(ExtraReferenceSourceUsingV2, "ScenarioExtraReferenceAssemblyUsingV2");

        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Main.cs", FixtureSource) },
            new[] { new SourceFile("Main.cs", FixtureSource) },
            beforeExtraReferences: Array.Empty<MetadataReference>(),
            afterExtraReferences: new[] { extraReference });

        // Same profile as scenario (e) up through ResolvedSerializers: nothing in the fixture itself
        // changed, so every extracted/collected/resolved model still compares equal, and the S6
        // schemas-only projection stays Cached (no text in Main.cs moved).
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedSerializers,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.SerializerSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedSerializers, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ExtractedMessages,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged,
            IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Unchanged);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.MessageSchemas,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CollectedMessages, IncrementalStepRunReason.Cached);

        // TARGET (S5): CompilationFacts is NOT combined into ResolvedSerializers' inputs (see
        // TrackingNames.CompilationFacts's own doc comment for why that wiring is deliberately
        // deferred), so a CompilationFacts change alone cannot move ResolvedSerializers -- both
        // serializers stay Cached even though CompilationFacts itself reports Modified below.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.ResolvedSerializers,
            IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached);

        // THE SCENARIO FLIP: the newly added reference DOES reference Akka.Serialization.V2, so
        // CompilationFacts.ReferencedAssembliesUsingV2 gains an entry -- the computed CompilationFacts
        // value genuinely differs from the previous run, so this stage reports Modified, not
        // Unchanged. This is the behavioral proof that ComputeReferencedAssembliesUsingV2 is real
        // and wired up, not merely present with no effect.
        AssertReasons(result, AkkaSerializerGenerator.TrackingNames.CompilationFacts, IncrementalStepRunReason.Modified);

        // No serializer's own message table changed and MarkerMessage implements neither protocol,
        // so -- even though CompilationFacts itself changed -- no file is re-emitted and no new
        // AKKASG029 diagnostic appears.
        ChangedHintNames(result).Should().BeEmpty();
        result.After.RunResult.Diagnostics.Where(d => d.Id == "AKKASG029").Should().BeEmpty();
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
