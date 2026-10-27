//-----------------------------------------------------------------------
// <copyright file="SerializerV2StringManifestSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// <see cref="SerializerV2"/> derives from <see cref="SerializerWithStringManifest"/> (issue #8784), which makes
    /// <see cref="SerializerV1Adapter"/> one too. These specs pin that a wrapped V1 serializer still behaves
    /// exactly as it did: an empty manifest when it has none, and its own <c>FromBinary(byte[], Type)</c> when a
    /// caller deserializes by type.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class SerializerV2StringManifestSpec : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
              serializers {
                no-manifest = ""Akka.Tests.Serialization.SerializerV2StringManifestSpec+NoManifestSerializer, Akka.Tests""
              }
              serialization-bindings {
                ""Akka.Tests.Serialization.SerializerV2StringManifestSpec+NoManifestMessage, Akka.Tests"" = no-manifest
              }
            }");

        public SerializerV2StringManifestSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        /// <summary>
        /// The manifest selection many persistence plugins compiled against 1.5 use.
        /// </summary>
        private static string LegacyManifest(Serializer serializer, object payload) => serializer switch
        {
            SerializerWithStringManifest stringManifest => stringManifest.Manifest(payload),
            { IncludeManifest: true } => payload.GetType().TypeQualifiedName(),
            _ => string.Empty
        };

        [Fact(DisplayName = "Should_return_empty_manifest_When_wrapped_V1_serializer_has_IncludeManifest_false")]
        public void Should_return_empty_manifest_When_wrapped_V1_serializer_has_IncludeManifest_false()
        {
            var message = new NoManifestMessage("hi");

            var publicSerializer = Sys.Serialization.FindSerializerFor(message);
            publicSerializer.Should().BeOfType<NoManifestSerializer>();
            publicSerializer.IncludeManifest.Should().BeFalse();
            Akka.Serialization.Serialization.ManifestFor(publicSerializer, message).Should().BeEmpty();
            LegacyManifest(publicSerializer, message).Should().BeEmpty();

            var adapter = Sys.Serialization.FindSerializerV2For(message);
            adapter.Should().BeOfType<SerializerV1Adapter>();
            adapter.Should().BeAssignableTo<SerializerWithStringManifest>();
            Akka.Serialization.Serialization.ManifestFor(adapter, message).Should().BeEmpty();
            Akka.Serialization.Serialization.ManifestFor((Serializer)adapter, message).Should().BeEmpty();
            LegacyManifest(adapter, message).Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_round_trip_with_empty_manifest_When_wrapped_V1_serializer_has_IncludeManifest_false")]
        public void Should_round_trip_with_empty_manifest_When_wrapped_V1_serializer_has_IncludeManifest_false()
        {
            var message = new NoManifestMessage("hi");
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);

            Sys.Serialization.Deserialize(bytes, serializer.Identifier, string.Empty)
                .Should().Be(new NoManifestMessage("<null>:hi"));
        }

        [Fact(DisplayName = "Should_pass_type_to_wrapped_serializer_When_deserializing_by_type_with_dynamic_type_loading_off")]
        public async Task Should_pass_type_to_wrapped_serializer_When_deserializing_by_type_with_dynamic_type_loading_off()
        {
            var message = new NoManifestMessage("hi");
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);

            // The adapter's own FromBinary(byte[], Type) is now sealed in SerializerWithStringManifest and goes
            // type -> name -> type. Serialization.Deserialize must hand the Type straight to the wrapped
            // serializer, so this works without reflection on a type the TypeCache has never seen.
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, typeof(UncachedHint))
                    .Should().Be(new NoManifestMessage(nameof(UncachedHint) + ":hi"));
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, (Type?)null)
                    .Should().Be(new NoManifestMessage("<null>:hi"));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should_treat_null_type_as_empty_manifest_When_SerializerV2_FromBinary_gets_null_type")]
        public void Should_treat_null_type_as_empty_manifest_When_SerializerV2_FromBinary_gets_null_type()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var byteArraySerializer = Sys.Serialization.FindSerializerForType(typeof(byte[]));
            byteArraySerializer.Should().BeOfType<ByteArraySerializer>();

            byteArraySerializer.FromBinary(bytes, (Type?)null).Should().BeSameAs(bytes);
            Sys.Serialization.Deserialize(bytes, byteArraySerializer.Identifier, (Type?)null).Should().BeSameAs(bytes);
        }

        public sealed record NoManifestMessage(string Text);

        private sealed class UncachedHint
        {
        }

        /// <summary>
        /// A V1 serializer with no manifest. Deserialization records which type hint reached it.
        /// </summary>
        public sealed class NoManifestSerializer : Serializer
        {
            public NoManifestSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 778401;

            public override bool IncludeManifest => false;

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((NoManifestMessage)obj).Text);

            public override object FromBinary(byte[] bytes, Type? type)
                => new NoManifestMessage((type?.Name ?? "<null>") + ":" + Encoding.UTF8.GetString(bytes));
        }
    }
}
