//-----------------------------------------------------------------------
// <copyright file="MetricsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
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
    /// Keeps <see cref="MetricsSerializers"/> in sync with Akka.Cluster.Metrics' reference.conf. The shared checks
    /// live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class MetricsSerializersSpec : AkkaSpec
    {
        private static readonly Config MetricsRows = ClusterMetrics.DefaultConfig();

        private static readonly Type[] BoundSamples =
        {
            typeof(MetricsGossipEnvelope), typeof(AdaptiveLoadBalancingPool), typeof(MixMetricsSelector),
            typeof(CpuMetricsSelector), typeof(MemoryMetricsSelector)
        };

        public MetricsSerializersSpec(ITestOutputHelper output) : base(MetricsRows, output)
        {
        }

        [Fact(DisplayName = "MetricsSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new MetricsSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(MetricsRows, table.Serializers.Select(s => s.Type), table.BoundTypes);
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("metrics-aqn", MetricsRows, Sys);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Metrics_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("metrics-override", overrides, MetricsRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(MetricsGossipEnvelope)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(AdaptiveLoadBalancingPool)).Should().BeOfType<ClusterMetricsMessageSerializer>();
            });
        }

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
