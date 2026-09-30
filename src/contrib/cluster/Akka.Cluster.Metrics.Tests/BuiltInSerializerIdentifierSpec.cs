//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Metrics.Tests
{
    /// <summary>
    /// Akka.Cluster.Metrics' built-in serializer used to read its <see cref="Serializer.Identifier"/> lazily from
    /// <c>akka.actor.serialization-identifiers</c>. It now declares it in code. This spec pins the code value to
    /// exactly what Akka.Cluster.Metrics' reference.conf still ships, so the two can't drift apart before the
    /// HOCON row is deleted in a later PR.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec : AkkaSpec
    {
        private static Config SpecConfig =>
            ConfigurationFactory.ParseString("akka.actor.provider = cluster").WithFallback(ClusterMetrics.DefaultConfig());

        public BuiltInSerializerIdentifierSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        [Fact(DisplayName = "Should_match_reference_conf_serialization_identifier_When_reading_ClusterMetricsMessageSerializer_Identifier")]
        public async Task Should_match_reference_conf_serialization_identifier_When_reading_ClusterMetricsMessageSerializer_Identifier()
        {
            await Task.Yield();

            var serializer = new ClusterMetricsMessageSerializer((ExtendedActorSystem)Sys);
            var expectedId = ClusterMetrics.DefaultConfig()
                .GetInt($"akka.actor.serialization-identifiers.\"{typeof(ClusterMetricsMessageSerializer).TypeQualifiedName()}\"");

            serializer.Identifier.Should().Be(10);
            serializer.Identifier.Should().Be(expectedId);
        }
    }
}
