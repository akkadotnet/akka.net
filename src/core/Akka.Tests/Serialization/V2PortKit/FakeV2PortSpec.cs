//-----------------------------------------------------------------------
// <copyright file="FakeV2PortSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>
    /// The kit run against a fake legacy and V2 pair: how a real port's spec looks, and proof that its tests pass on a
    /// port that keeps the contract. Golden files under <c>GoldenBytes/Fake</c> come from <c>AKKA_GOLDEN_CAPTURE=1</c>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class FakeV2PortSpec : V2PortSpec
    {
        private static readonly GoldenBytes GoldenFiles = GoldenBytes.For("GoldenBytes/Fake");

        public FakeV2PortSpec(ITestOutputHelper output) : base(FakePort.Setup(), output)
        {
        }

        protected virtual FakeDefect Defect => FakeDefect.None;

        protected override Serializer CreateLegacy(ExtendedActorSystem system) => new FakeLegacySerializer(system);

        protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new FakeV2Serializer(system, Defect);

        protected override IReadOnlyList<V2PortCase> Cases => FakePort.Cases;

        protected override GoldenBytes Golden => GoldenFiles;

        /// <summary>The same spec with a broken V2 serializer, run by hand to show a check catches the defect.</summary>
        internal sealed class Broken : FakeV2PortSpec
        {
            public Broken(ITestOutputHelper output, FakeDefect defect) : base(output)
            {
                Defect = defect;
            }

            protected override FakeDefect Defect { get; }
        }
    }

    /// <summary>The kit's checks fail when they should.</summary>
    public sealed class V2PortSpecSelfTests
    {
        private readonly ITestOutputHelper _output;

        public V2PortSpecSelfTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(DisplayName = "Should_FailIdCheck_When_V2IdIsNotLegacyPlus40")]
        public async Task Should_FailIdCheck_When_V2IdIsNotLegacyPlus40()
        {
            var spec = new FakeV2PortSpec.Broken(_output, FakeDefect.WrongId);
            try
            {
                Action act = spec.Should_UseReservedIdBlock_When_PortIsDeclared;
                act.Should().Throw<Exception>();
            }
            finally
            {
                await spec.DisposeAsync();
            }
        }

        [Fact(DisplayName = "Should_FailRoundTrip_When_V2LosesData")]
        public async Task Should_FailRoundTrip_When_V2LosesData()
        {
            var spec = new FakeV2PortSpec.Broken(_output, FakeDefect.LosesCount);
            try
            {
                Action act = spec.Should_RoundTripEveryManifest_When_V2Serializes;
                act.Should().Throw<Exception>().WithMessage("*V2 FromBinary*");
            }
            finally
            {
                await spec.DisposeAsync();
            }
        }
    }
}
