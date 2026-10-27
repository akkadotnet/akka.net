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
            public PairLegacySerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 39;

            public override string Manifest(object o) => "P";

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((PairMessage)obj).Value);

            public override object FromBinary(byte[] bytes, string manifest) => new PairMessage(Encoding.UTF8.GetString(bytes));
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

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) =>
                new PairMessage(Encoding.UTF8.GetString(bytes.Slice(1).ToArray()));
        }

        [Fact(DisplayName = "Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy")]
        public async Task Should_RoundTripBothWays_When_OneNodeBindsV2AndTheOtherPinsLegacy()
        {
            await using var pair = MixedBindingPair.Create("mixed-ok", Bound("pair-v2"), Bound("pair"));

            pair.AssertRoundTripsBothWays(Cases, 79, 39);
        }

        [Fact(DisplayName = "Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy")]
        public async Task Should_FailTheCheck_When_TheV2NodeIsStillPinnedToLegacy()
        {
            await using var pair = MixedBindingPair.Create("mixed-pinned", Bound("pair"), Bound("pair"));

            Action act = () => pair.AssertRoundTripsBothWays(Cases, 79, 39);

            act.Should().Throw<Exception>().WithMessage("*V2 node to legacy node*");
        }
    }
}
