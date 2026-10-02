//-----------------------------------------------------------------------
// <copyright file="MixedBindingPairSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>
    /// <see cref="MixedBindingPair"/> on the fake serializers: one node binds <see cref="IFakeProtocol"/> to the V2 alias,
    /// the other pins it to the legacy alias, both through <c>akka.actor.serialization-bindings</c>.
    /// </summary>
    public sealed class MixedBindingPairSpec
    {
        private static MixedBindingPair Start(string name, Config v2Side, Config legacySide)
            => MixedBindingPair.Create(name, v2Side, legacySide, FakePort.UnboundRows());

        private static Config V2Bindings => MixedBindingPair.BindTo("fake-v2", typeof(IFakeProtocol));

        private static Config LegacyBindings => MixedBindingPair.BindTo("fake", typeof(IFakeProtocol));

        [Fact(DisplayName = "Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy")]
        public async Task Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy()
        {
            await using var pair = Start("mixed-both-ways", V2Bindings, LegacyBindings);

            var exchanges = pair.AssertRoundTripsBothWays(FakePort.Cases, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);

            exchanges.Should().HaveCount(FakePort.Cases.Length * 2);
            exchanges.Count(e => e.Direction == MixedBindingDirection.V2ToLegacy).Should().Be(FakePort.Cases.Length);
        }

        [Fact(DisplayName = "Should_WriteDifferentBytes_When_NodesDisagreeAboutTheBinding")]
        public async Task Should_WriteDifferentBytes_When_NodesDisagreeAboutTheBinding()
        {
            await using var pair = Start("mixed-bytes", V2Bindings, LegacyBindings);
            var message = FakePort.Cases[0].Message;

            var fromV2 = pair.Send(MixedBindingDirection.V2ToLegacy, message);
            var fromLegacy = pair.Send(MixedBindingDirection.LegacyToV2, message);

            fromV2.Writer.Should().BeOfType<FakeV2Serializer>();
            fromLegacy.Writer.Should().BeOfType<FakeLegacySerializer>();
            fromV2.Bytes.Should().NotEqual(fromLegacy.Bytes);
            fromV2.Received.Should().Be(message);
            fromLegacy.Received.Should().Be(message);
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy")]
        public async Task Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy()
        {
            await using var pair = Start("mixed-unswitched", LegacyBindings, LegacyBindings);

            Action act = () => pair.AssertRoundTripsBothWays(FakePort.Cases, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);

            act.Should().Throw<Exception>().WithMessage("*the V2 node should write*with the V2 serializer*");
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheLegacyNodeWasNotPinned")]
        public async Task Should_FailTheCheck_When_TheLegacyNodeWasNotPinned()
        {
            await using var pair = Start("mixed-unpinned", V2Bindings, V2Bindings);

            Action act = () => pair.AssertRoundTripsBothWays(FakePort.Cases, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);

            act.Should().Throw<Exception>().WithMessage("*the legacy node should write*with the legacy serializer*");
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheV2NodeLosesData")]
        public async Task Should_FailTheCheck_When_TheV2NodeLosesData()
        {
            await using var pair = Start("mixed-lossy", V2Bindings, LegacyBindings);
            var lossy = new V2PortCase(new FakePing("a", 5), "P", "lossy", (expected, actual) => actual.Should().Be(new FakePing("a", 6)));

            Action act = () => pair.AssertRoundTripsBothWays(new[] { lossy }, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);

            act.Should().Throw<Exception>();
        }

        [Fact(DisplayName = "Should_SpellBindingKeys_When_BuildingBindToConfig")]
        public void Should_SpellBindingKeys_When_BuildingBindToConfig()
        {
            var config = MixedBindingPair.BindTo("fake-v2", typeof(FakePing), typeof(FakePong));

            var bindings = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable().ToDictionary(kv => kv.Key, kv => kv.Value.GetString());

            bindings.Should().HaveCount(2);
            bindings.Values.Should().OnlyContain(alias => alias == "fake-v2");
            bindings.Keys.Should().Contain("Akka.Tests.Serialization.V2PortKit.FakePing, Akka.Tests");
        }
    }
}
