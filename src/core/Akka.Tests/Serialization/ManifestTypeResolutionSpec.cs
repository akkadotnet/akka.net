//-----------------------------------------------------------------------
// <copyright file="ManifestTypeResolutionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Covers the base <see cref="Serializer.FromBinary(byte[], string)"/> and
    /// <see cref="SerializerV1Adapter.FromBinary(byte[], string)"/> fallback: a <see cref="Serializer"/> that
    /// has a manifest (<c>IncludeManifest</c> true) but does not override this method resolves that manifest
    /// through <see cref="Akka.Util.Reflection.TypeCache"/>, which is only reachable while
    /// <c>Akka.DynamicTypeLoading</c> is on. None of core's own built-in serializers reach this path, but
    /// Akka.Remote's <c>ProtobufSerializer</c> / <c>SystemMessageSerializer</c> and Akka.Persistence's
    /// snapshot / message serializers do - see <c>BREAKING_CHANGES_V1.6.md</c>. This spec uses a minimal
    /// stand-in <see cref="Serializer"/> instead, so it needs neither module referenced.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ManifestTypeResolutionSpec : AkkaSpec
    {
        private static readonly string ManifestTypeName = typeof(ProbeManifestType).AssemblyQualifiedName!;

        public ManifestTypeResolutionSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "Serializer.FromBinary(bytes, manifest) should resolve the manifest type by reflection when dynamic type loading is on")]
        public async Task Should_resolve_the_manifest_type_When_dynamic_type_loading_is_enabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, () =>
            {
                var serializer = new ManifestSerializer((ExtendedActorSystem)Sys);
                serializer.FromBinary(Array.Empty<byte>(), ManifestTypeName).Should().Be(typeof(ProbeManifestType));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Serializer.FromBinary(bytes, manifest) should reject the manifest type when dynamic type loading is off")]
        public async Task Should_throw_SerializationException_When_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var serializer = new ManifestSerializer((ExtendedActorSystem)Sys);

                var exception = Assert.Throws<SerializationException>(
                    () => serializer.FromBinary(Array.Empty<byte>(), ManifestTypeName));

                exception.Message.Should().Contain(ManifestTypeName);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "SerializerV1Adapter.FromBinary(bytes, manifest) should reject the manifest type when dynamic type loading is off")]
        public async Task Should_throw_SerializationException_When_the_V1_adapter_and_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var adapter = new SerializerV1Adapter((ExtendedActorSystem)Sys, new ManifestSerializer((ExtendedActorSystem)Sys));

                var exception = Assert.Throws<SerializationException>(
                    () => adapter.FromBinary(Array.Empty<byte>(), ManifestTypeName));

                exception.Message.Should().Contain(ManifestTypeName);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// A <see cref="Serializer"/> with a string manifest that is neither manifest-less nor a
        /// <c>SerializerWithStringManifest</c>, so <c>FromBinary(byte[], string)</c> falls to the base
        /// implementation's <see cref="Akka.Util.Reflection.TypeCache"/> lookup. Returns the resolved
        /// <see cref="Type"/> itself so the test can assert on it without needing a real payload.
        /// </summary>
        private sealed class ManifestSerializer : Serializer
        {
            public ManifestSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            // Fixed, so the test does not need an akka.actor.serialization-identifiers entry for a type
            // that only ever exists inside this spec.
            public override int Identifier => -108;

            public override bool IncludeManifest => true;

            public override byte[] ToBinary(object obj) => Array.Empty<byte>();

            public override object FromBinary(byte[] bytes, Type type) => type;
        }

        private sealed class ProbeManifestType
        {
        }
    }
}
