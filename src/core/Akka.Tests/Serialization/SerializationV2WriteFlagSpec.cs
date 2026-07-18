//-----------------------------------------------------------------------
// <copyright file="SerializationV2WriteFlagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Specs for the write-side V2 serializer flag surface (<c>akka.actor.serialization.v2</c>)
    /// and the binding-rewrite hook in <see cref="Akka.Serialization.Serialization"/>.
    /// Uses a pair of dummy serializers standing in for a legacy (protobuf) serializer and its
    /// forked V2 (MessagePack) replacement.
    /// </summary>
    public class SerializationV2WriteFlagSpec
    {
        /// <summary>
        /// Mirrors what a migrated subsystem ships in its reference config: both serializers
        /// registered unconditionally, the regular binding pointing at the legacy serializer,
        /// and a `write-bindings` declaration re-pointing the same key at the V2 serializer
        /// when the `test-subsystem` flag resolves to on.
        /// </summary>
        private const string BaseConfig = @"
            akka.actor {
                serializers {
                    v2-flag-legacy = ""Akka.Tests.Serialization.V2FlagLegacySerializer, Akka.Tests""
                    v2-flag-v2 = ""Akka.Tests.Serialization.V2FlagV2Serializer, Akka.Tests""
                }
                serialization-bindings {
                    ""Akka.Tests.Serialization.IV2FlagTestMessage, Akka.Tests"" = v2-flag-legacy
                }
                serialization.v2.write-bindings {
                    test-subsystem {
                        ""Akka.Tests.Serialization.IV2FlagTestMessage, Akka.Tests"" = v2-flag-v2
                    }
                }
            }";

        private static async Task WithSystemAsync(string config, Func<ActorSystem, Task> test)
        {
            var system = ActorSystem.Create(nameof(SerializationV2WriteFlagSpec),
                ConfigurationFactory.ParseString(config)
                    .WithFallback(ConfigurationFactory.ParseString(BaseConfig)));
            try
            {
                await test(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static void AssertWriteSerializerId(ActorSystem system, int expectedId)
        {
            var byInstance = system.Serialization.FindSerializerFor(new V2FlagTestMessage("payload"));
            byInstance.Identifier.Should().Be(expectedId);

            var byType = system.Serialization.FindSerializerForType(typeof(V2FlagTestMessage));
            byType.Identifier.Should().Be(expectedId);
        }

        private static void AssertBothIdsReadable(ActorSystem system)
        {
            var bytes = Encoding.UTF8.GetBytes("hello");

            // read dispatch is purely by serializer id and must never depend on the flag state
            var fromLegacy = system.Serialization
                .Deserialize(bytes, V2FlagLegacySerializer.Id, typeof(V2FlagTestMessage))
                .Should().BeOfType<V2FlagTestMessage>().Subject;
            fromLegacy.Payload.Should().Be(V2FlagLegacySerializer.DecodedPrefix + "hello");

            var fromV2 = system.Serialization
                .Deserialize(bytes, V2FlagV2Serializer.Id, typeof(V2FlagTestMessage))
                .Should().BeOfType<V2FlagTestMessage>().Subject;
            fromV2.Payload.Should().Be(V2FlagV2Serializer.DecodedPrefix + "hello");
        }

        [Fact(DisplayName = "Should bind legacy serializer for writes when the v2 flag is off (default)")]
        public async Task Should_bind_legacy_serializer_when_flag_off()
        {
            await WithSystemAsync("", async system =>
            {
                AssertWriteSerializerId(system, V2FlagLegacySerializer.Id);
                await Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should bind v2 serializer for writes when the master flag is on")]
        public async Task Should_bind_v2_serializer_when_master_flag_on()
        {
            await WithSystemAsync("akka.actor.serialization.v2.enabled = on", async system =>
            {
                AssertWriteSerializerId(system, V2FlagV2Serializer.Id);
                await Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should bind legacy serializer when master flag is on but the subsystem override is off")]
        public async Task Should_bind_legacy_serializer_when_subsystem_override_off()
        {
            await WithSystemAsync(@"
                akka.actor.serialization.v2.enabled = on
                akka.actor.serialization.v2.test-subsystem = off", async system =>
            {
                AssertWriteSerializerId(system, V2FlagLegacySerializer.Id);
                await Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should bind v2 serializer when master flag is off but the subsystem override is on")]
        public async Task Should_bind_v2_serializer_when_subsystem_override_on()
        {
            await WithSystemAsync("akka.actor.serialization.v2.test-subsystem = on", async system =>
            {
                AssertWriteSerializerId(system, V2FlagV2Serializer.Id);
                await Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should resolve both serializer ids on the read side when the flag is off")]
        public async Task Should_resolve_both_ids_when_flag_off()
        {
            await WithSystemAsync("", async system =>
            {
                AssertBothIdsReadable(system);
                await Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Should resolve both serializer ids on the read side when the flag is on")]
        public async Task Should_resolve_both_ids_when_flag_on()
        {
            await WithSystemAsync("akka.actor.serialization.v2.enabled = on", async system =>
            {
                AssertBothIdsReadable(system);
                await Task.CompletedTask;
            });
        }
    }

    /// <summary>
    /// Marker interface bound in `serialization-bindings` - stands in for a subsystem marker
    /// such as `IDeliverySerializable` or `IReplicatorMessage`.
    /// </summary>
    public interface IV2FlagTestMessage
    {
    }

    public sealed class V2FlagTestMessage : IV2FlagTestMessage
    {
        public V2FlagTestMessage(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }
    }

    /// <summary>
    /// Stands in for a legacy (protobuf) internal serializer. Prefixes decoded payloads so specs
    /// can prove which serializer performed a by-id deserialization.
    /// </summary>
    public sealed class V2FlagLegacySerializer : Serializer
    {
        public const int Id = 9001;
        public const string DecodedPrefix = "legacy:";

        public V2FlagLegacySerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => Id;

        public override bool IncludeManifest => false;

        public override byte[] ToBinary(object obj)
            => Encoding.UTF8.GetBytes(((V2FlagTestMessage)obj).Payload);

        public override object FromBinary(byte[] bytes, Type? type)
            => new V2FlagTestMessage(DecodedPrefix + Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Stands in for a forked V2 (MessagePack) internal serializer registered alongside the
    /// legacy one under a new id.
    /// </summary>
    public sealed class V2FlagV2Serializer : Serializer
    {
        public const int Id = 9002;
        public const string DecodedPrefix = "v2:";

        public V2FlagV2Serializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => Id;

        public override bool IncludeManifest => false;

        public override byte[] ToBinary(object obj)
            => Encoding.UTF8.GetBytes(((V2FlagTestMessage)obj).Payload);

        public override object FromBinary(byte[] bytes, Type? type)
            => new V2FlagTestMessage(DecodedPrefix + Encoding.UTF8.GetString(bytes));
    }
}
