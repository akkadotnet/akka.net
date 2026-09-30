//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    /// <summary>
    /// Akka.Remote's built-in (legacy) serializers used to read their <see cref="Serializer.Identifier"/> lazily
    /// from <c>akka.actor.serialization-identifiers</c> via <see cref="SerializerIdentifierHelper"/>. They now
    /// declare it in code with a hard <c>public override int Identifier</c>. This spec pins the code value to
    /// exactly what Remote.conf still ships, so the two can't drift apart before the HOCON rows are deleted in a
    /// later PR, and proves that a user's HOCON override of a built-in id is now ignored (maintainer decision D1
    /// on the serializer-registration plan: the code id wins).
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output)
            : base(RemoteConfigFactory.Default(), output)
        {
        }

        private static readonly Config RemoteConfig = RemoteConfigFactory.Default();

        private static int HoconIdentifierFor(Type serializerType) =>
            RemoteConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        public static TheoryData<string, int> BuiltInSerializers => new()
        {
            { nameof(ProtobufSerializer), 2 },
            { nameof(DaemonMsgCreateSerializer), 3 },
            { nameof(MessageContainerSerializer), 6 },
            { nameof(MiscMessageSerializer), 16 },
            { nameof(PrimitiveSerializers), 17 },
            { nameof(SystemMessageSerializer), 22 },
        };

        private Serializer CreateSerializer(string serializerName)
        {
            var system = (ExtendedActorSystem)Sys;
            return serializerName switch
            {
                nameof(ProtobufSerializer) => new ProtobufSerializer(system),
                nameof(DaemonMsgCreateSerializer) => new DaemonMsgCreateSerializer(system),
                nameof(MessageContainerSerializer) => new MessageContainerSerializer(system),
                nameof(MiscMessageSerializer) => new MiscMessageSerializer(system),
                nameof(PrimitiveSerializers) => new PrimitiveSerializers(system,
                    Sys.Settings.Config.GetConfig("akka.actor.serialization-settings.primitive")),
                nameof(SystemMessageSerializer) => new SystemMessageSerializer(system),
                _ => throw new ArgumentOutOfRangeException(nameof(serializerName), serializerName, "unknown serializer")
            };
        }

        [Theory(DisplayName = "Should_match_Remote_conf_serialization_identifier_When_reading_a_builtin_serializer_Identifier")]
        [MemberData(nameof(BuiltInSerializers))]
        public async Task Should_match_Remote_conf_serialization_identifier_When_reading_a_builtin_serializer_Identifier(
            string serializerName, int expectedId)
        {
            await Task.Yield();

            var serializer = CreateSerializer(serializerName);
            serializer.Identifier.Should().Be(expectedId);
            serializer.Identifier.Should().Be(HoconIdentifierFor(serializer.GetType()),
                "the code id and Remote.conf's serialization-identifiers row must not drift apart before the HOCON row is deleted");
        }

        /// <summary>
        /// D1: a user's <c>akka.actor.serialization-identifiers</c> override of a built-in serializer's id no
        /// longer takes effect - the code id is a wire contract and always wins.
        /// </summary>
        [Fact(DisplayName = "Should_keep_the_code_Identifier_When_HOCON_tries_to_override_a_builtin_serializer_id")]
        public async Task Should_keep_the_code_Identifier_When_HOCON_tries_to_override_a_builtin_serializer_id()
        {
            await Task.Yield();

            var overrideConfig = ConfigurationFactory.ParseString(
                    "akka.actor.serialization-identifiers { \"Akka.Remote.Serialization.MiscMessageSerializer, Akka.Remote\" = 99 }")
                .WithFallback(RemoteConfigFactory.Default());

            var system = (ExtendedActorSystem)ActorSystem.Create("D1-hocon-override-spec", overrideConfig);
            try
            {
                var serializer = new MiscMessageSerializer(system);
                serializer.Identifier.Should().Be(16, "the code id wins over a HOCON override (D1)");
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
