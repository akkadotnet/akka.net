//-----------------------------------------------------------------------
// <copyright file="MixedBindingPairSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    public sealed class MixedBindingPairSpec
    {
        private static Config Bound(string alias) => MixedBindingPair.BindTo(alias, typeof(IFakeProtocol)).WithFallback(FakePort.Aliases());

        [Fact(DisplayName = "Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy")]
        public async Task Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy()
        {
            await using var pair = MixedBindingPair.Create("mixed-ok", Bound("fake-v2"), Bound("fake"));

            pair.AssertRoundTripsBothWays(FakePort.Cases, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy")]
        public async Task Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy()
        {
            await using var pair = MixedBindingPair.Create("mixed-pinned", Bound("fake"), Bound("fake"));

            Action act = () => pair.AssertRoundTripsBothWays(FakePort.Cases, FakeV2Serializer.V2Id, FakeLegacySerializer.LegacyId);

            act.Should().Throw<Exception>().WithMessage("*V2 node to legacy node*");
        }
    }
}
