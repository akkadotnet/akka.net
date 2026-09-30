//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerIdentifierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster.Metrics;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Streams;
using Xunit;
using DistributedDataExtension = Akka.DistributedData.DistributedData;
using PersistenceExtension = Akka.Persistence.Persistence;

namespace Akka.API.Tests
{
    /// <summary>
    /// Every built-in serializer now declares its wire id in code. This is the one project that references all of
    /// Remote, Cluster, Cluster.Tools, Cluster.Sharding, DistributedData, Cluster.Metrics, Persistence, Streams and
    /// core, so it's the one place that can check every built-in serializer's code <see cref="Serializer.Identifier"/>
    /// against every module's shipped <c>akka.actor.serialization-identifiers</c> row in a single test.
    ///
    /// Each serializer is created with <see cref="RuntimeHelpers.GetUninitializedObject"/>, which skips the
    /// constructor entirely, leaving the instance's actor-system field null. A built-in serializer's <c>Identifier</c>
    /// is either a hardcoded constant or a <c>GetType()</c> guard around one - never a HOCON read - so this is safe
    /// for all of them. It also proves D1: a serializer that still resolved its id lazily from HOCON would touch the
    /// null actor-system field on first access and throw, instead of quietly returning the right answer.
    /// </summary>
    public class BuiltInSerializerIdentifierSpec
    {
        [Fact(DisplayName = "Should_match_HOCON_serialization_identifiers_When_reading_every_builtin_serializer_Identifier")]
        public void Should_match_HOCON_serialization_identifiers_When_reading_every_builtin_serializer_Identifier()
        {
            var config = ConfigurationFactory.ParseString("akka.actor.provider = cluster")
                .WithFallback(ConfigurationFactory.FromResource<Remote.RemoteSettings>("Akka.Remote.Configuration.Remote.conf"))
                .WithFallback(ConfigurationFactory.FromResource<Cluster.ClusterSettings>("Akka.Cluster.Configuration.Cluster.conf"))
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig())
                .WithFallback(ClusterClientReceptionist.DefaultConfig())
                .WithFallback(DistributedDataExtension.DefaultConfig())
                .WithFallback(ClusterMetrics.DefaultConfig())
                .WithFallback(PersistenceExtension.DefaultConfig())
                .WithFallback(ActorMaterializer.DefaultConfig());

            using var system = ActorSystem.Create(nameof(BuiltInSerializerIdentifierSpec), config);

            var rows = system.Settings.Config.GetConfig("akka.actor.serialization-identifiers");
            foreach (var row in rows.AsEnumerable())
            {
                var type = Type.GetType(row.Key, throwOnError: true);
                var expectedId = row.Value.GetInt();

                var serializer = (Serializer)RuntimeHelpers.GetUninitializedObject(type);
                Assert.True(expectedId == serializer.Identifier,
                    $"{type.FullName} declared Identifier {serializer.Identifier}, but its HOCON row says {expectedId}");
            }
        }
    }
}
