//-----------------------------------------------------------------------
// <copyright file="MixedBindingPairSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    public sealed class MixedBindingPairSpec
    {
        private const string Aliases = @"
            akka.actor.serializers {
                pair = ""Akka.Tests.Serialization.V2PortKit.MixedBindingPairSpec+PairLegacySerializer, Akka.Tests""
                pair-v2 = ""Akka.Tests.Serialization.V2PortKit.MixedBindingPairSpec+PairV2Serializer, Akka.Tests""
            }";

        private static readonly V2PortCase[] Cases = { new(new PairMessage("one"), "P", "one") };

        private static Config Bound(string alias) =>
            MixedBindingPair.BindTo(alias, typeof(PairMessage)).WithFallback(ConfigurationFactory.ParseString(Aliases));

        public sealed record PairMessage(string Value);

        /// <summary>Id 39, manifest <c>P</c>, UTF-8 text.</summary>
        public sealed class PairLegacySerializer : SerializerWithStringManifest
        {
            private readonly string _manifest;

            public PairLegacySerializer(ExtendedActorSystem system) : base(system)
            {
                _manifest = system.Settings.Config.GetString("test.pair-legacy-manifest", "P");
            }

            public override int Identifier => 39;

            public override string Manifest(object o) => _manifest;

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((PairMessage)obj).Value);

            public override object FromBinary(byte[] bytes, string manifest)
            {
                if (manifest != _manifest)
                    throw new ArgumentException($"Unexpected legacy manifest [{manifest}].", nameof(manifest));
                return new PairMessage(Encoding.UTF8.GetString(bytes));
            }
        }

        /// <summary>Id 79, manifest <c>P</c>, UTF-8 text behind a 0x01 byte.</summary>
        public sealed class PairV2Serializer : SerializerV2
        {
            public PairV2Serializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 79;

            public override string Manifest(object obj) => "P";

            public override int SizeHint(object obj) => 1 + Encoding.UTF8.GetByteCount(((PairMessage)obj).Value);

            public override int Serialize(object obj, IBufferWriter<byte> writer)
            {
                var bytes = new byte[SizeHint(obj)];
                bytes[0] = 1;
                Encoding.UTF8.GetBytes(((PairMessage)obj).Value, 0, ((PairMessage)obj).Value.Length, bytes, 1);
                writer.Write(bytes);
                return bytes.Length;
            }

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
            {
                if (manifest != "P")
                    throw new ArgumentException($"Unexpected V2 manifest [{manifest}].", nameof(manifest));
                return new PairMessage(Encoding.UTF8.GetString(bytes.Slice(1).ToArray()));
            }
        }

        [Fact(DisplayName = "Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy")]
        public async Task Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy()
        {
            Assert.Null(Cases[0].V2Manifest);
            await using var pair = MixedBindingPair.Create("mixed-ok", Bound("pair-v2"), Bound("pair"));

            pair.AssertRoundTripsBothWays(Cases, 79, 39);
        }

        [Theory(DisplayName = "Should_RequireAnExplicitV2Manifest_When_LegacyHadNoToken")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_RequireAnExplicitV2Manifest_When_LegacyHadNoToken(bool explicitV2Manifest)
        {
            var empty = ConfigurationFactory.ParseString("test.pair-legacy-manifest = \"\"");
            await using var pair = MixedBindingPair.Create("mixed-manifests", Bound("pair-v2").WithFallback(empty),
                Bound("pair").WithFallback(empty));
            var sample = new V2PortCase(new PairMessage("one"), "", "legacy-empty")
            {
                V2Manifest = explicitV2Manifest ? "P" : null
            };
            Action exchange = () => pair.AssertRoundTripsBothWays([sample], 79, 39);

            if (explicitV2Manifest)
                exchange.Should().NotThrow();
            else
                exchange.Should().Throw<Exception>().WithMessage("*manifest*");
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy")]
        public async Task Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy()
        {
            await using var pair = MixedBindingPair.Create("mixed-pinned", Bound("pair"), Bound("pair"));

            Action act = () => pair.AssertRoundTripsBothWays(Cases, 79, 39);

            act.Should().Throw<Exception>().WithMessage("*V2 node to legacy node*");
        }

        [Theory(DisplayName = "Should_PreserveNamesAndDisposeBothSystems_When_CreatingAMixedPair")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_PreserveNamesAndDisposeBothSystems_When_CreatingAMixedPair(bool sameSystemName)
        {
            const string name = "mixed-names";
            var pair = sameSystemName
                ? MixedBindingPair.CreateWithSameSystemName(name, Bound("pair-v2"), Bound("pair"))
                : MixedBindingPair.Create(name, Bound("pair-v2"), Bound("pair"));
            await using (pair)
            {
                Assert.Equal(sameSystemName ? name : name + "-v2", pair.V2Node.Name);
                Assert.Equal(sameSystemName ? name : name + "-legacy", pair.LegacyNode.Name);
                pair.AssertRoundTripsBothWays(Cases, 79, 39);
            }

            Assert.True(pair.V2Node.WhenTerminated.IsCompletedSuccessfully);
            Assert.True(pair.LegacyNode.WhenTerminated.IsCompletedSuccessfully);
        }
    }
}
