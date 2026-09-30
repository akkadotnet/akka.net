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
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization
{
    /// <summary>
    /// Akka.DistributedData's built-in serializers used to read their <see cref="Serializer.Identifier"/> lazily
    /// from <c>akka.actor.serialization-identifiers</c>. They now declare it in code. This spec pins each code
    /// value to exactly what Akka.DistributedData's reference.conf still ships, so the two can't drift apart
    /// before the HOCON rows are deleted in a later PR.
    /// </summary>
    [Collection("DistributedDataSpec")]
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        private static readonly Config ModuleConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output) : base(ModuleConfig, output)
        {
        }

        private static int HoconIdentifierFor(Type serializerType) =>
            ModuleConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_ReplicatedDataSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_ReplicatedDataSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ReplicatedDataSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(11);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(ReplicatedDataSerializer)));
        }

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_ReplicatorMessageSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_ReplicatorMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ReplicatorMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(12);
            serializer.Identifier.Should().Be(HoconIdentifierFor(typeof(ReplicatorMessageSerializer)));
        }
    }
}
