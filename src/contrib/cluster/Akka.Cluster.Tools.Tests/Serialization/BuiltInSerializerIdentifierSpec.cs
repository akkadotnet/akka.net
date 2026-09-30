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
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Singleton.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Serialization
{
    /// <summary>
    /// Akka.Cluster.Tools' built-in serializers (PublishSubscribe, Singleton and Client) used to read their
    /// <see cref="Serializer.Identifier"/> lazily from <c>akka.actor.serialization-identifiers</c>. They now
    /// declare it in code. This spec pins each code value to exactly what its own module's reference.conf still
    /// ships, so the two can't drift apart before the HOCON rows are deleted in a later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString("akka.actor.provider = cluster")
                .WithFallback(DistributedPubSub.DefaultConfig())
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(ClusterClientReceptionist.DefaultConfig()), output)
        {
        }

        private static int HoconIdentifierFor(Config moduleConfig, Type serializerType) =>
            moduleConfig.GetInt($"akka.actor.serialization-identifiers.\"{serializerType.TypeQualifiedName()}\"");

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_DistributedPubSubMessageSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_DistributedPubSubMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new DistributedPubSubMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(9);
            serializer.Identifier.Should().Be(HoconIdentifierFor(DistributedPubSub.DefaultConfig(), typeof(DistributedPubSubMessageSerializer)));
        }

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_ClusterSingletonMessageSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_ClusterSingletonMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ClusterSingletonMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(14);
            serializer.Identifier.Should().Be(HoconIdentifierFor(ClusterSingleton.DefaultConfig(), typeof(ClusterSingletonMessageSerializer)));
        }

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_ClusterClientMessageSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_ClusterClientMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ClusterClientMessageSerializer((ExtendedActorSystem)Sys);
            serializer.Identifier.Should().Be(15);
            serializer.Identifier.Should().Be(HoconIdentifierFor(ClusterClientReceptionist.DefaultConfig(), typeof(ClusterClientMessageSerializer)));
        }
    }
}
