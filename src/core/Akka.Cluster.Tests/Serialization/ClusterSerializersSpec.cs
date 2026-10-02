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
    /// Keeps <see cref="ClusterSerializers"/> in step with the rows Cluster.conf shipped at 1.6.0-beta1, which
    /// <see cref="FrozenSerializerRows"/> keeps. The shared checks live in <see cref="ModuleSerializerSpecs"/>; this
    /// spec adds what is specific to Cluster, including the internal API needed to force a reflection-only
    /// baseline for the parity comparison below.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ClusterSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly Config ClusterProvider = ConfigurationFactory.ParseString("akka.actor.provider = cluster");

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        // Cluster.conf no longer ships these rows; the table has to keep matching what 1.6.0-beta1 shipped
        private static readonly Config ClusterRows = FrozenSerializerRows.Cluster;

        private static readonly Type[] BoundSamples =
        {
            typeof(ClusterHeartbeatSender.Heartbeat), typeof(ClusterRouterPool), typeof(ConsumerController.Delivery<int>)
        };

        public ClusterSerializersSpec(ITestOutputHelper output) : base(ClusterProvider, output)
        {
        }

        private static AkkaSerialization Build(ActorSystem system, ModuleSerializerTable table, bool dynamicTypeLoading)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                return new AkkaSerialization((ExtendedActorSystem)system, table);
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        [Theory(DisplayName = "Serialization should resolve every built-in Cluster serializer and bound type as 1.6.0-beta1 did, with no Cluster rows in the config")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_match_the_beta1_rows_When_the_config_has_no_Cluster_rows(bool dynamicTypeLoading)
        {
            var details = new ClusterSerializers().Create((ExtendedActorSystem)Sys);

            // core's module map names Akka.Cluster, and ClusterSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Cluster").Should().NotBeNull();

            // beta1 read the frozen rows from Cluster.conf and resolved them by reflection, with no module table
            await ModuleSerializerSpecs.WithSystem("cluster-beta1", ClusterConfigFactory.Default(), ClusterRows, system =>
            {
                var reflected = Build(system, NoModules, dynamicTypeLoading: true);

                // Sys carries no Cluster rows at all: everything below comes from the table's defaults
                Sys.Settings.Config.HasPath("akka.actor.serializers.akka-cluster").Should().BeFalse();
                var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading);

                foreach (var entry in details)
                {
                    entry.Serializer.Should().BeOfType(reflected.GetSerializerById(entry.Serializer.Identifier).GetType(), entry.Alias);
                    fromTable.GetSerializerById(entry.Serializer.Identifier).Should().BeOfType(entry.Serializer.GetType(), entry.Alias);
                }

                foreach (var type in BoundSamples)
                    fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType(), type.FullName);

                foreach (var type in details.SelectMany(d => d.UseFor))
                    fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType(), type.FullName);
            });
        }

        [Fact(DisplayName = "Serialization should resolve every frozen Cluster row on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_frozen_rows_When_a_plain_system_has_no_Cluster_rows()
            => await ModuleSerializerSpecs.AssertPlainSystemResolvesFrozenRows("cluster-plain", ClusterRows, (s, id) => s.GetSerializerById(id));

        [Fact(DisplayName = "ClusterSerializers should match the rows Cluster.conf shipped at 1.6.0-beta1")]
        public void Should_match_the_frozen_rows_When_the_table_is_built()
        {
            var table = new ClusterSerializers();
            ModuleSerializerSpecs.AssertTableMatchesFrozenRows(ClusterRows, table.Create((ExtendedActorSystem)Sys));
        }

        [Fact(DisplayName = "ClusterSerializers should build without throwing on a system that never loaded Cluster.conf")]
        public async Task Should_build_without_throwing_When_Cluster_conf_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("cluster-no-config", s => new ClusterSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve Cluster.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("cluster-aqn", ClusterRows, Sys, BoundSamples);

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

            await ModuleSerializerSpecs.WithSystem("cluster-override", overrides, withCopiedRows ? ClusterRows : null, system =>
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
