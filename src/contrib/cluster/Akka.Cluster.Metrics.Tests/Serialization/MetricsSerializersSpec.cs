//-----------------------------------------------------------------------
// <copyright file="MetricsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Metrics.Tests
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
    /// Keeps <see cref="MetricsSerializers"/> in step with the rows Akka.Cluster.Metrics' reference.conf shipped at
    /// 1.6.0-beta1, which <see cref="FrozenSerializerRows"/> keeps. The shared checks live in
    /// <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class MetricsSerializersSpec : AkkaSpec
    {
        // reference.conf no longer ships these rows; the table has to keep matching what 1.6.0-beta1 shipped
        private static readonly Config MetricsRows = FrozenSerializerRows.Metrics;

        private static readonly Type[] BoundSamples =
        {
            typeof(MetricsGossipEnvelope), typeof(AdaptiveLoadBalancingPool), typeof(MixMetricsSelector),
            typeof(CpuMetricsSelector), typeof(MemoryMetricsSelector)
        };

        public MetricsSerializersSpec(ITestOutputHelper output) : base(ClusterMetrics.DefaultConfig(), output)
        {
        }

        [Fact(DisplayName = "MetricsSerializers should match the rows reference.conf shipped at 1.6.0-beta1")]
        public void Should_match_the_frozen_rows_When_the_table_is_built()
        {
            var table = new MetricsSerializers();
            ModuleSerializerSpecs.AssertTableMatchesFrozenRows(MetricsRows, table.Create((ExtendedActorSystem)Sys));
        }

        [Fact(DisplayName = "Serialization should resolve every frozen Metrics row on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_frozen_rows_When_a_plain_system_has_no_Metrics_rows()
            => await ModuleSerializerSpecs.AssertPlainSystemResolvesFrozenRows("metrics-plain", MetricsRows, (s, id) => s.GetSerializerById(id));

        [Fact(DisplayName = "MetricsSerializers should build without throwing on a system that never loaded its reference.conf")]
        public async Task Should_build_without_throwing_When_its_config_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("metrics-no-config", s => new MetricsSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("metrics-aqn", MetricsRows, Sys);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Metrics binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Metrics_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("metrics-override", overrides, withCopiedRows ? MetricsRows : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(MetricsGossipEnvelope)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(AdaptiveLoadBalancingPool)).Should().BeOfType<ClusterMetricsMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace the built-in Metrics alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_the_Metrics_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("metrics-alias-override", "akka-cluster-metrics", BoundSamples);

        [Fact(DisplayName = "Serialization should build ClusterMetricsMessageSerializer under id 10, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_metrics_serializer_id_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(10).Should().BeOfType<ClusterMetricsMessageSerializer>();
            foreach (var type in BoundSamples)
                serialization.FindSerializerForType(type).Should().BeOfType<ClusterMetricsMessageSerializer>();
        }
    }
}
