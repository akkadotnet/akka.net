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
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{
    /// <summary>
    /// Akka.Persistence's built-in serializers used to read their <see cref="Serializer.Identifier"/> lazily from
    /// <c>akka.actor.serialization-identifiers</c>. They now declare it in code. This spec pins each code value to
    /// exactly what Akka.Persistence's persistence.conf still ships, so the two can't drift apart before the
    /// HOCON rows are deleted in a later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        private static readonly Config ModuleConfig = Persistence.DefaultConfig();

        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output) : base(ModuleConfig, output)
        {
        }

        private static int HoconIdentifierFor(Type serializerType) =>
            ModuleConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        [Fact(DisplayName = "Should_match_persistence_conf_serialization_identifier_When_reading_PersistenceMessageSerializer_Identifier")]
        public async Task Should_match_persistence_conf_serialization_identifier_When_reading_PersistenceMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new PersistenceMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(7);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(PersistenceMessageSerializer)));
        }

        [Fact(DisplayName = "Should_match_persistence_conf_serialization_identifier_When_reading_PersistenceSnapshotSerializer_Identifier")]
        public async Task Should_match_persistence_conf_serialization_identifier_When_reading_PersistenceSnapshotSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new PersistenceSnapshotSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(8);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(PersistenceSnapshotSerializer)));
        }
    }
}
