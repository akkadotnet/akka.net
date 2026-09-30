//-----------------------------------------------------------------------
// <copyright file="ToolsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Singleton.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests
{
    /// <summary>
    /// AppContext switches are process-wide, so a spec that flips <c>Akka.DynamicTypeLoading</c> never runs beside another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    /// <summary>
    /// Keeps <see cref="ToolsSerializers"/> in sync with Akka.Cluster.Tools' three reference.conf files
    /// (Client, PublishSubscribe, Singleton). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// Unlike Remote/Streams/Cluster, this test project has no <c>InternalsVisibleTo</c> grant from Akka core,
    /// so it cannot see the internal <c>ModuleSerializer</c> record and skips the reflection-only baseline;
    /// the serializer-type list below is a hand-kept mirror of <see cref="ToolsSerializers"/> instead of a
    /// live read of its <c>Serializers</c> property.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ToolsSerializersSpec : AkkaSpec
    {
        private static readonly Config ToolsRows = ClusterClientReceptionist.DefaultConfig()
            .WithFallback(DistributedPubSub.DefaultConfig())
            .WithFallback(ClusterSingleton.DefaultConfig());

        private static readonly Type[] SerializerTypes =
        {
            typeof(ClusterClientMessageSerializer), typeof(DistributedPubSubMessageSerializer), typeof(ClusterSingletonMessageSerializer)
        };

        public ToolsSerializersSpec(ITestOutputHelper output) : base(ToolsRows, output)
        {
        }

        [Fact(DisplayName = "ToolsSerializers should list no serializer or type that its reference.conf files do not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new ToolsSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(ToolsRows, SerializerTypes, table.BoundTypes);
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("tools-aqn", ToolsRows, Sys);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Tools_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Tools.Singleton.IClusterSingletonMessage, Akka.Cluster.Tools"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("tools-override", overrides, ToolsRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(IClusterSingletonMessage)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(IDistributedPubSubMessage)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should build every Tools serializer under its usual class, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Tools_serializer_classes_When_dynamic_type_loading_is_disabled()
        {
            // Serialization.GetSerializerById is internal; this project has no Akka core IVT grant, so bound
            // types are checked by class instead of by wire id (covered for Cluster/Sharding/Metrics elsewhere).
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.FindSerializerForType(typeof(IClusterClientMessage)).Should().BeOfType<ClusterClientMessageSerializer>();
            serialization.FindSerializerForType(typeof(IClusterClientProtocolMessage)).Should().BeOfType<ClusterClientMessageSerializer>();
            serialization.FindSerializerForType(typeof(IDistributedPubSubMessage)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            serialization.FindSerializerForType(typeof(SendToOneSubscriber)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            serialization.FindSerializerForType(typeof(IClusterSingletonMessage)).Should().BeOfType<ClusterSingletonMessageSerializer>();
        }
    }
}
