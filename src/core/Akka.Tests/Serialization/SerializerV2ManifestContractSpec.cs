//-----------------------------------------------------------------------
// <copyright file="SerializerV2ManifestContractSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Serialization;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Pins the documented LEGACY EXEMPTION to the V2 manifest invariant (serializer-v2 design.md
    /// Decision 14, exemption defined by Decision 5): new native V2 serializers must return
    /// non-empty, non-CLR manifests, but ports of existing serializer IDs preserve their legacy
    /// manifest behavior when changing it would break wire or persisted data.
    /// <see cref="ByteArraySerializer"/> (Akka-internal serializer id 4) is the canonical case:
    /// its manifest has always been empty, and stays empty — stably, and without requiring a
    /// prior <c>Serialize</c> call.
    /// </summary>
    public class SerializerV2ManifestContractSpec
    {
        [Fact(DisplayName = "Should_return_empty_manifest_When_ByteArraySerializer_manifests_without_serializing")]
        public void Should_return_empty_manifest_When_ByteArraySerializer_manifests_without_serializing()
        {
            // A fresh instance with nothing serialized yet: Manifest is derivable without a
            // Serialize call, and preserves the legacy empty manifest.
            var serializer = new ByteArraySerializer(null!);

            var manifest = serializer.Manifest(new byte[] { 1, 2, 3 });

            Assert.Equal(string.Empty, manifest);
        }

        [Fact(DisplayName = "Should_return_stable_empty_manifest_When_ByteArraySerializer_is_called_repeatedly")]
        public void Should_return_stable_empty_manifest_When_ByteArraySerializer_is_called_repeatedly()
        {
            var serializer = new ByteArraySerializer(null!);
            var payload = new byte[] { 1, 2, 3 };

            var first = serializer.Manifest(payload);
            var second = serializer.Manifest(payload);
            var differentPayload = serializer.Manifest(new byte[] { 4, 5 });
            var freshInstance = new ByteArraySerializer(null!).Manifest(payload);

            Assert.Equal(string.Empty, first);
            Assert.Equal(first, second);
            Assert.Equal(first, differentPayload);
            Assert.Equal(first, freshInstance);
        }
    }
}
