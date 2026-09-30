//-----------------------------------------------------------------------
// <copyright file="ShardingSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.Sharding.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
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
    /// Keeps <see cref="ShardingSerializers"/> in sync with Akka.Cluster.Sharding's reference.conf. The shared
    /// checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ShardingSerializersSpec : AkkaSpec
    {
        // sharding's own rows only; ClusterSharding.DefaultConfig() also falls back to DistributedData's reference.conf
        private static readonly Config ShardingRows =
            ConfigurationFactory.FromResource<ClusterSharding>("Akka.Cluster.Sharding.reference.conf");

        public ShardingSerializersSpec(ITestOutputHelper output) : base(ClusterSharding.DefaultConfig(), output)
        {
        }

        [Fact(DisplayName = "ShardingSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new ShardingSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(ShardingRows, table.Serializers.Select(s => s.Type), table.BoundTypes);
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("sharding-aqn", ShardingRows, Sys);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Sharding_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Sharding.IClusterShardingSerializable, Akka.Cluster.Sharding"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("sharding-override", overrides, ShardingRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ShardRegion.StartEntity)).Should().BeOfType<ByteArraySerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should build ClusterShardingMessageSerializer under id 13, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_sharding_serializer_id_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(13).Should().BeOfType<ClusterShardingMessageSerializer>();
            serialization.FindSerializerForType(typeof(ShardRegion.StartEntity)).Should().BeOfType<ClusterShardingMessageSerializer>();
        }
    }
}
