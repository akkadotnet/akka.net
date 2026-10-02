//-----------------------------------------------------------------------
// <copyright file="V2PortSpecsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>
    /// What <see cref="V2PortSpecs"/> catches. <see cref="FakeV2PortSpec"/> shows the checks pass on a port that keeps
    /// the contract; here each fake defect (<see cref="FakeDefect"/>) breaks it one way, and the matching check must fail.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public sealed class V2PortSpecsSpec : AkkaSpec
    {
        private static readonly GoldenBytes FakeV2Golden = GoldenBytes.For("GoldenBytes/Fake", GoldenKind.V2);
        private static readonly GoldenBytes FakeLegacyGolden = GoldenBytes.For("GoldenBytes/Fake", GoldenKind.Legacy);

        public V2PortSpecsSpec(ITestOutputHelper output) : base(FakePort.Setup(), output)
        {
        }

        [Fact(DisplayName = "Should_FailIdCheck_When_V2IdIsNotLegacyIdPlusOffset")]
        public void Should_FailIdCheck_When_V2IdIsNotLegacyIdPlusOffset()
        {
            Action act = () => V2PortSpecs.AssertIds(Sys, FakePort.Port(FakeDefect.WrongId));

            act.Should().Throw<Exception>().WithMessage("*plus 40*");
        }

        [Fact(DisplayName = "Should_FailIdCheck_When_V2IdIsOutsideTheReservedBlock")]
        public void Should_FailIdCheck_When_V2IdIsOutsideTheReservedBlock()
        {
            var port = FakePort.Port();
            port.V2IdMax = 78;

            Action act = () => V2PortSpecs.AssertIds(Sys, port);

            act.Should().Throw<Exception>().WithMessage("*reserved block*");
        }

        [Fact(DisplayName = "Should_PassIdCheck_When_PortUsesLegacyIdPlusOffsetInsideTheBlock")]
        public void Should_PassIdCheck_When_PortUsesLegacyIdPlusOffsetInsideTheBlock()
        {
            V2PortSpecs.AssertIds(Sys, FakePort.Port());
        }

        [Fact(DisplayName = "Should_FailManifestParity_When_V2ManifestDiffersFromLegacyToken")]
        public void Should_FailManifestParity_When_V2ManifestDiffersFromLegacyToken()
        {
            Action act = () => V2PortSpecs.AssertManifestParity(Sys, FakePort.Port(FakeDefect.WrongManifest));

            act.Should().Throw<Exception>().WithMessage("*reuse the legacy manifest token*");
        }

        [Fact(DisplayName = "Should_FailManifestParity_When_CorpusNamesTheWrongLegacyManifest")]
        public void Should_FailManifestParity_When_CorpusNamesTheWrongLegacyManifest()
        {
            var port = new V2Port(
                s => new FakeLegacySerializer(s), s => new FakeV2Serializer(s), new[] { new V2PortCase(new FakePing("a", 1), "Z") });

            Action act = () => V2PortSpecs.AssertManifestParity(Sys, port);

            act.Should().Throw<Exception>().WithMessage("*legacy manifest of*");
        }

        [Fact(DisplayName = "Should_FailV2RoundTrip_When_V2LosesData")]
        public void Should_FailV2RoundTrip_When_V2LosesData()
        {
            Action act = () => V2PortSpecs.AssertV2RoundTrips(Sys, FakePort.Port(FakeDefect.LosesCount));

            act.Should().Throw<Exception>().WithMessage("*FromBinary*");
        }

        [Fact(DisplayName = "Should_FailV2RoundTrip_When_SerializeAndToBinaryWriteDifferentBytes")]
        public void Should_FailV2RoundTrip_When_SerializeAndToBinaryWriteDifferentBytes()
        {
            Action act = () => V2PortSpecs.AssertV2RoundTrips(Sys, FakePort.Port(FakeDefect.BufferPathDiffers));

            act.Should().Throw<Exception>().WithMessage("*Serialize and ToBinary must write the same bytes*");
        }

        [Fact(DisplayName = "Should_FailV2RoundTrip_When_SizeHintIsNotExact")]
        public void Should_FailV2RoundTrip_When_SizeHintIsNotExact()
        {
            Action act = () => V2PortSpecs.AssertV2RoundTrips(Sys, FakePort.Port(FakeDefect.InexactSizeHint));

            act.Should().Throw<Exception>().WithMessage("*SizeHint must be exact*");
        }

        [Fact(DisplayName = "Should_PassRoundTrips_When_BothSerializersKeepTheirContracts")]
        public void Should_PassRoundTrips_When_BothSerializersKeepTheirContracts()
        {
            V2PortSpecs.AssertV2RoundTrips(Sys, FakePort.Port());
            V2PortSpecs.AssertLegacyRoundTrips(Sys, FakePort.Port());
        }

        [Fact(DisplayName = "Should_FailBothIdsCheck_When_NodeDoesNotRegisterTheV2Row")]
        public async Task Should_FailBothIdsCheck_When_NodeDoesNotRegisterTheV2Row()
        {
            var legacyOnly = SerializationSetup.Create(system => ImmutableHashSet.Create(
                SerializerDetails.Create("fake", new FakeLegacySerializer(system), ImmutableHashSet.Create(typeof(IFakeProtocol)))));
            var system = ActorSystem.Create("v2port-legacy-only", ActorSystemSetup.Create(legacyOnly)
                .And(BootstrapSetup.Create().WithConfig(AkkaSpecConfig)));
            try
            {
                Action act = () => V2PortSpecs.AssertBothIdsDecode(system, FakePort.Port());

                act.Should().Throw<Exception>();
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Should_FailBindingCheck_When_BindingMovedToV2WhileLegacyIsExpected")]
        public async Task Should_FailBindingCheck_When_BindingMovedToV2WhileLegacyIsExpected()
        {
            await using var pair = MixedBindingPair.Create(
                "v2port-binding", MixedBindingPair.BindTo("fake-v2", typeof(IFakeProtocol)), Config.Empty, FakePort.UnboundRows());

            Action expectingLegacy = () => V2PortSpecs.AssertBinding(pair.V2Node, FakePort.Port());
            var port = FakePort.Port();
            port.ExpectedBinding = V2PortBinding.V2;

            expectingLegacy.Should().Throw<Exception>().WithMessage("*Legacy serializer*");
            V2PortSpecs.AssertBinding(pair.V2Node, port);
        }

        [Fact(DisplayName = "Should_PassBindingCheck_When_V2RowIsReadOnly")]
        public void Should_PassBindingCheck_When_V2RowIsReadOnly()
        {
            V2PortSpecs.AssertBinding(Sys, FakePort.Port());
        }

        [Fact(DisplayName = "Should_FailDynamicTypeLoadingCheck_When_V2NeedsTypeLoading")]
        public void Should_FailDynamicTypeLoadingCheck_When_V2NeedsTypeLoading()
        {
            var port = FakePort.Port(FakeDefect.NeedsDynamicTypeLoading);

            V2PortSpecs.AssertV2RoundTrips(Sys, port); // fine while the switch is on
            Action act = () => V2PortSpecs.AssertV2ResolvesWithDynamicTypeLoadingOff(Sys, port);

            act.Should().Throw<InvalidOperationException>().WithMessage("*resolves types by name*");
        }

        [Fact(DisplayName = "Should_PassDynamicTypeLoadingCheck_When_V2NeverLoadsTypesByName")]
        public void Should_PassDynamicTypeLoadingCheck_When_V2NeverLoadsTypesByName()
        {
            V2PortSpecs.AssertV2ResolvesWithDynamicTypeLoadingOff(Sys, FakePort.Port());
        }

        [Fact(DisplayName = "Should_RestoreTheSwitch_When_DynamicTypeLoadingCheckFails")]
        public void Should_RestoreTheSwitch_When_DynamicTypeLoadingCheckFails()
        {
            var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var before);

            try
            {
                V2PortSpecs.AssertV2ResolvesWithDynamicTypeLoadingOff(Sys, FakePort.Port(FakeDefect.NeedsDynamicTypeLoading));
            }
            catch (InvalidOperationException)
            {
                // expected: see Should_FailDynamicTypeLoadingCheck_When_V2NeedsTypeLoading
            }

            var hasSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var after);
            // an unset switch reads as enabled, so a restore may leave it explicitly true
            (!hasSwitch || after).Should().Be(!hadSwitch || before);
        }

        [Fact(DisplayName = "Should_FailGoldenCheck_When_V2BytesChange")]
        public void Should_FailGoldenCheck_When_V2BytesChange()
        {
            Action act = () => V2PortSpecs.AssertV2BytesMatchGolden(Sys, FakePort.Port(FakeDefect.TrailingByte), FakeV2Golden);

            act.Should().Throw<Exception>().WithMessage("*V2 golden bytes for 'ping' changed*expected 12 bytes, actual 13 bytes*");
        }

        [Fact(DisplayName = "Should_FailGoldenCheck_When_LegacyBytesChange")]
        public void Should_FailGoldenCheck_When_LegacyBytesChange()
        {
            var changed = new FakeLegacySerializer((ExtendedActorSystem)Sys, ';');

            Action act = () => V2PortSpecs.AssertLegacyBytesMatchGolden(Sys, changed, FakePort.Cases, FakeLegacyGolden);

            act.Should().Throw<Exception>().WithMessage("*Legacy golden bytes for 'ping' changed*first difference at offset 1*");
        }

        [Fact(DisplayName = "Should_SkipByteComparison_When_LegacyOutputIsNotDeterministic")]
        public void Should_SkipByteComparison_When_LegacyOutputIsNotDeterministic()
        {
            var changed = new FakeLegacySerializer((ExtendedActorSystem)Sys, ';');

            V2PortSpecs.AssertLegacyBytesMatchGolden(Sys, changed, FakePort.Cases, FakeLegacyGolden, compareBytes: false);
        }

        [Fact(DisplayName = "Should_FailGoldenCheck_When_CaseHasNoGoldenFile")]
        public void Should_FailGoldenCheck_When_CaseHasNoGoldenFile()
        {
            var port = new V2Port(
                s => new FakeLegacySerializer(s), s => new FakeV2Serializer(s), new[] { new V2PortCase(new FakePing("new", 1), "P", "not-captured") });

            Action act = () => V2PortSpecs.AssertV2BytesMatchGolden(Sys, port, FakeV2Golden);

            act.Should().Throw<Exception>().WithMessage("*No V2 golden file for 'not-captured'*AKKA_GOLDEN_CAPTURE=v2*");
        }

        [Fact(DisplayName = "Should_DecodeLegacyGolden_When_CapturedFromTheLegacySerializerAlone")]
        public void Should_DecodeLegacyGolden_When_CapturedFromTheLegacySerializerAlone()
        {
            var directory = NewTempDirectory();
            try
            {
                var golden = new GoldenBytes(directory, capture: true, GoldenKind.Legacy);

                // the porter's first step: the legacy serializer and the corpus, no V2 serializer involved
                V2PortSpecs.AssertLegacyBytesMatchGolden(Sys, new FakeLegacySerializer((ExtendedActorSystem)Sys), FakePort.Cases, golden);
                var readBack = new GoldenBytes(directory, capture: false, GoldenKind.Legacy);

                V2PortSpecs.AssertLegacyBytesMatchGolden(Sys, new FakeLegacySerializer((ExtendedActorSystem)Sys), FakePort.Cases, readBack);
                V2PortSpecs.AssertNodeDecodesLegacyGolden(Sys, FakePort.Port(), readBack);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact(DisplayName = "Should_FailLegacyDecode_When_NodeReadsGoldenItCannotParse")]
        public void Should_FailLegacyDecode_When_NodeReadsGoldenItCannotParse()
        {
            var directory = NewTempDirectory();
            try
            {
                // golden bytes written with another separator, as if the legacy wire had been captured wrongly
                var golden = new GoldenBytes(directory, capture: true, GoldenKind.Legacy);
                V2PortSpecs.AssertLegacyBytesMatchGolden(
                    Sys, new FakeLegacySerializer((ExtendedActorSystem)Sys, ';'), FakePort.Cases, golden);

                Action act = () => V2PortSpecs.AssertNodeDecodesLegacyGolden(Sys, FakePort.Port(), new GoldenBytes(directory, false, GoldenKind.Legacy));

                act.Should().Throw<Exception>();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact(DisplayName = "Should_FailCorpusCheck_When_TwoCasesShareAName")]
        public void Should_FailCorpusCheck_When_TwoCasesShareAName()
        {
            var port = new V2Port(
                s => new FakeLegacySerializer(s), s => new FakeV2Serializer(s),
                new[] { new V2PortCase(new FakePing("a", 1), "P"), new V2PortCase(new FakePing("b", 2), "P") });

            Action act = () => V2PortSpecs.AssertCorpus(port);

            act.Should().Throw<Exception>().WithMessage("*each must be unique*");
        }

        [Fact(DisplayName = "Should_FailCorpusCheck_When_ARequiredManifestIsMissing")]
        public void Should_FailCorpusCheck_When_ARequiredManifestIsMissing()
        {
            var port = new V2Port(
                s => new FakeLegacySerializer(s), s => new FakeV2Serializer(s), new[] { new V2PortCase(new FakePing("a", 1), "P") })
            {
                RequiredManifests = new[] { "P", "Q" }
            };

            Action act = () => V2PortSpecs.AssertCorpus(port);

            act.Should().Throw<Exception>().WithMessage("*Q*");
        }

        [Fact(DisplayName = "Should_UseTheCaseComparison_When_CaseSuppliesOne")]
        public void Should_UseTheCaseComparison_When_CaseSuppliesOne()
        {
            var lenient = new V2PortCase(
                new FakePing("a", 1), "P", "lenient", (expected, actual) => actual.Should().BeOfType<FakePing>());
            var port = new V2Port(
                s => new FakeLegacySerializer(s), s => new FakeV2Serializer(s, FakeDefect.LosesCount), new[] { lenient });

            V2PortSpecs.AssertV2RoundTrips(Sys, port);
        }

        [Fact(DisplayName = "Should_MapNamesToSafeFileNames_When_CaseNameHasPathCharacters")]
        public void Should_MapNamesToSafeFileNames_When_CaseNameHasPathCharacters()
        {
            var c = new V2PortCase(new FakePing("a", 1), "DurableProducerQueue.State<string>/x", null);

            c.FileStem.Should().Be("DurableProducerQueue.State_string__x");
            new V2PortCase(new FakePing("a", 1), "").Name.Should().Be("empty-manifest-FakePing");
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "akka-v2port-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
