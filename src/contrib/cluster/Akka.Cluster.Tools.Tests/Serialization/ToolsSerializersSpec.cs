//-----------------------------------------------------------------------
// <copyright file="ToolsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
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
    /// Checks how <see cref="ToolsSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ToolsSerializersSpec : AkkaSpec
    {
        private static readonly Config ToolsConfig = ClusterClientReceptionist.DefaultConfig()
            .WithFallback(DistributedPubSub.DefaultConfig())
            .WithFallback(ClusterSingleton.DefaultConfig());

        public ToolsSerializersSpec(ITestOutputHelper output) : base(ToolsConfig, output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new ToolsSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every Tools table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_Tools_rows()
        {
            // core's module map names Akka.Cluster.Tools, and ToolsSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Cluster.Tools").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("tools-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "ToolsSerializers should build without throwing on a system that never loaded its reference.conf")]
        public async Task Should_build_without_throwing_When_its_config_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("tools-no-config", s => new ToolsSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("tools-aqn", Table, Sys);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Tools binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Tools_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Tools.Singleton.IClusterSingletonMessage, Akka.Cluster.Tools"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("tools-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(IClusterSingletonMessage)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(IDistributedPubSubMessage)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace a built-in Tools alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_a_Tools_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("tools-alias-override", "akka-pubsub",
                typeof(IDistributedPubSubMessage), typeof(SendToOneSubscriber));

        [Fact(DisplayName = "Serialization should build every Tools serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Tools_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(15).Should().BeOfType<ClusterClientMessageSerializer>();
            serialization.GetSerializerById(9).Should().BeOfType<DistributedPubSubMessageSerializer>();
            serialization.GetSerializerById(14).Should().BeOfType<ClusterSingletonMessageSerializer>();

            serialization.FindSerializerForType(typeof(IClusterClientMessage)).Should().BeOfType<ClusterClientMessageSerializer>();
            serialization.FindSerializerForType(typeof(IClusterClientProtocolMessage)).Should().BeOfType<ClusterClientMessageSerializer>();
            serialization.FindSerializerForType(typeof(IDistributedPubSubMessage)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            serialization.FindSerializerForType(typeof(SendToOneSubscriber)).Should().BeOfType<DistributedPubSubMessageSerializer>();
            serialization.FindSerializerForType(typeof(IClusterSingletonMessage)).Should().BeOfType<ClusterSingletonMessageSerializer>();
        }

        /// <remarks>
        /// Akka.Cluster.Tools is deployed with this test project, so its module default registers on startup - a
        /// plain system resolves Client/PubSub/Singleton by id without ever starting any of those extensions or
        /// loading their reference.conf rows.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve Client/PubSub/Singleton serializers by id on a plain system with none of their extensions started")]
        public async Task Should_resolve_Tools_serializers_When_a_plain_system_has_no_extension_started()
        {
            var system = ActorSystem.Create("tools-no-extension");
            InitializeLogger(system);
            try
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;
                serialization.GetSerializerById(9).Should().BeOfType<DistributedPubSubMessageSerializer>();
                serialization.GetSerializerById(14).Should().BeOfType<ClusterSingletonMessageSerializer>();
                serialization.GetSerializerById(15).Should().BeOfType<ClusterClientMessageSerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
