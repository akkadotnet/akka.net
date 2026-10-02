//-----------------------------------------------------------------------
// <copyright file="ClusterSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Routing;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Cluster.Tests.Serialization
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
    /// Checks how <see cref="ClusterSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ClusterSerializersSpec : AkkaSpec
    {
        private static readonly Config ClusterProvider = ConfigurationFactory.ParseString("akka.actor.provider = cluster");

        private static readonly Type[] BoundSamples =
        {
            typeof(ClusterHeartbeatSender.Heartbeat), typeof(ClusterRouterPool), typeof(ConsumerController.Delivery<int>)
        };

        public ClusterSerializersSpec(ITestOutputHelper output) : base(ClusterProvider, output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new ClusterSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every Cluster table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_Cluster_rows()
        {
            // core's module map names Akka.Cluster, and ClusterSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Cluster").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("cluster-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "ClusterSerializers should build without throwing on a system that never loaded Cluster.conf")]
        public async Task Should_build_without_throwing_When_Cluster_conf_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("cluster-no-config", s => new ClusterSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve Cluster.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("cluster-aqn", Table, Sys, BoundSamples);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Cluster binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Cluster_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Delivery.Internal.IDeliverySerializable, Akka"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("cluster-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ConsumerController.Delivery<int>)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(ClusterHeartbeatSender.Heartbeat)).Should().BeOfType<ClusterMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace a built-in Cluster alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_a_Cluster_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("cluster-alias-override", "akka-cluster",
                typeof(ClusterHeartbeatSender.Heartbeat), typeof(ClusterRouterPool));

        [Fact(DisplayName = "Serialization should build every Cluster serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Cluster_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(5).Should().BeOfType<ClusterMessageSerializer>();
            serialization.GetSerializerById(36).Should().BeOfType<ReliableDeliverySerializer>();

            serialization.FindSerializerForType(typeof(ClusterHeartbeatSender.Heartbeat)).Should().BeOfType<ClusterMessageSerializer>();
            serialization.FindSerializerForType(typeof(ClusterRouterPool)).Should().BeOfType<ClusterMessageSerializer>();
            serialization.FindSerializerForType(typeof(ConsumerController.Delivery<int>)).Should().BeOfType<ReliableDeliverySerializer>();
        }
    }
}
