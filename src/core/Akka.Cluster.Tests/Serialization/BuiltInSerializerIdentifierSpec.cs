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
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization
{
    /// <summary>
    /// Akka.Cluster's built-in serializers used to read their <see cref="Serializer.Identifier"/> lazily from
    /// <c>akka.actor.serialization-identifiers</c>. They now declare it in code. This spec pins the code value to
    /// exactly what Cluster.conf still ships, so the two can't drift apart before the HOCON rows are deleted in a
    /// later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output)
            : base(ClusterConfigFactory.Default(), output)
        {
        }

        private static readonly Config ClusterConfig = ClusterConfigFactory.Default();

        private static int HoconIdentifierFor(Type serializerType) =>
            ClusterConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        [Fact(DisplayName = "Should_match_Cluster_conf_serialization_identifier_When_reading_ClusterMessageSerializer_Identifier")]
        public async Task Should_match_Cluster_conf_serialization_identifier_When_reading_ClusterMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ClusterMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(5);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(ClusterMessageSerializer)));
        }

        [Fact(DisplayName = "Should_match_Cluster_conf_serialization_identifier_When_reading_ReliableDeliverySerializer_Identifier")]
        public async Task Should_match_Cluster_conf_serialization_identifier_When_reading_ReliableDeliverySerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ReliableDeliverySerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(36);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(ReliableDeliverySerializer)));
        }
    }
}
