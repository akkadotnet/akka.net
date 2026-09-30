//-----------------------------------------------------------------------
// <copyright file="SerializerManifestFallbackSpec.cs" company="Akka.NET Project">
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
    /// Covers the reordered <see cref="Serializer.FromBinary(byte[],string)"/> fallback (cache hit, then the
    /// <c>Akka.DynamicTypeLoading</c> switch, then reflection) and the <see cref="SerializerV1Adapter"/> fix
    /// that lets it delegate to whatever <c>FromBinary(byte[], string)</c> override the wrapped serializer has,
    /// instead of duplicating the lookup and silently skipping that override.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class SerializerManifestFallbackSpec : AkkaSpec
    {
        public SerializerManifestFallbackSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "SerializerV1Adapter.FromBinary(bytes, manifest) should call the inner serializer's own override When it has one")]
        public void Should_call_the_inner_serializers_own_override_When_it_has_one()
        {
            var inner = new StringOverrideSerializer((ExtendedActorSystem)Sys);
            var adapter = new SerializerV1Adapter((ExtendedActorSystem)Sys, inner);

            // the old adapter only special-cased SerializerWithStringManifest, so a plain Serializer's own
            // override here was skipped in favor of a duplicate TypeCache lookup - which would throw for this
            // manifest, since it names no real type
            adapter.FromBinary(Array.Empty<byte>(), "not-a-real-type").Should().Be("not-a-real-type");
        }

        [Fact(DisplayName = "Serializer.FromBinary(bytes, manifest) should throw a clear SerializationException When dynamic type loading is off")]
        public async Task Should_throw_SerializationException_When_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var serializer = new ManifestSerializer((ExtendedActorSystem)Sys);
                var manifest = typeof(AnotherProbeType).AssemblyQualifiedName!;

                var exception = Assert.Throws<SerializationException>(() => serializer.FromBinary(Array.Empty<byte>(), manifest));

                exception.Message.Should().Contain(manifest);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Serializer.FromBinary(bytes, manifest) should still return a cached type When dynamic type loading is off")]
        public async Task Should_return_a_cached_type_When_dynamic_type_loading_is_disabled()
        {
            var serializer = new ManifestSerializer((ExtendedActorSystem)Sys);
            var manifest = typeof(CachedProbeType).AssemblyQualifiedName!;

            // primes Akka.Util.Reflection.TypeCache for this manifest while the switch is on
            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, () =>
            {
                serializer.FromBinary(Array.Empty<byte>(), manifest).Should().Be(typeof(CachedProbeType));
                return Task.CompletedTask;
            });

            // a cache hit wins before the switch is even read, so the same manifest still resolves off
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                serializer.FromBinary(Array.Empty<byte>(), manifest).Should().Be(typeof(CachedProbeType));
                return Task.CompletedTask;
            });
        }

        /// <summary>A plain <see cref="Serializer"/> that overrides the string-manifest overload itself.</summary>
        private sealed class StringOverrideSerializer : Serializer
        {
            public StringOverrideSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => -108;
            public override bool IncludeManifest => true;
            public override byte[] ToBinary(object obj) => Array.Empty<byte>();
            public override object FromBinary(byte[] bytes, Type type) => throw new InvalidOperationException("the adapter bypassed the string overload");
            public override object FromBinary(byte[] bytes, string manifest) => manifest;
        }

        /// <summary>A plain <see cref="Serializer"/> that does not override the string overload, so it uses the base fallback.</summary>
        private sealed class ManifestSerializer : Serializer
        {
            public ManifestSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => -109;
            public override bool IncludeManifest => true;
            public override byte[] ToBinary(object obj) => Array.Empty<byte>();
            public override object FromBinary(byte[] bytes, Type type) => type;
        }

        private sealed class AnotherProbeType
        {
        }

        private sealed class CachedProbeType
        {
        }
    }
}
