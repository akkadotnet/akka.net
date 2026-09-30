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
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Core's built-in serializers used to read their <see cref="Serializer.Identifier"/> lazily from
    /// <c>akka.actor.serialization-identifiers</c> via <see cref="SerializerIdentifierHelper"/> (still covered
    /// directly by <see cref="SerializerIdentifierHelperSpec"/>). They now declare their id in code. This spec
    /// pins each code value to exactly what core's own akka.conf still ships, so the two can't drift apart before
    /// the HOCON rows are deleted in a later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        private static readonly Config CoreConfig = ConfigurationFactory.Default();

        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output) : base(CoreConfig, output)
        {
        }

        private static int HoconIdentifierFor(Type serializerType) =>
            CoreConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        [Fact(DisplayName = "Should_match_akka_conf_serialization_identifier_When_reading_ByteArraySerializer_Identifier")]
        public async Task Should_match_akka_conf_serialization_identifier_When_reading_ByteArraySerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ByteArraySerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(4);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(ByteArraySerializer)));
        }

        [Fact(DisplayName = "Should_match_akka_conf_serialization_identifier_When_reading_NewtonSoftJsonSerializer_Identifier")]
        public async Task Should_match_akka_conf_serialization_identifier_When_reading_NewtonSoftJsonSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new NewtonSoftJsonSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(1);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(NewtonSoftJsonSerializer)));
        }
    }
}
