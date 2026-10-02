//-----------------------------------------------------------------------
// <copyright file="ShardingSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
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
    /// Checks how <see cref="ShardingSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ShardingSerializersSpec : AkkaSpec
    {
        public ShardingSerializersSpec(ITestOutputHelper output) : base(ClusterSharding.DefaultConfig(), output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new ShardingSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every Sharding table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_Sharding_rows()
        {
            // core's module map names Akka.Cluster.Sharding, and ShardingSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Cluster.Sharding").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("sharding-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "ShardingSerializers should build without throwing on a system that never loaded its reference.conf")]
        public async Task Should_build_without_throwing_When_its_config_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("sharding-no-config", s => new ShardingSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("sharding-aqn", Table, Sys);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Sharding binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Sharding_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Cluster.Sharding.IClusterShardingSerializable, Akka.Cluster.Sharding"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("sharding-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ShardRegion.StartEntity)).Should().BeOfType<ByteArraySerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace the built-in sharding alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_the_sharding_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("sharding-alias-override", "akka-sharding",
                typeof(ShardRegion.StartEntity));

        [Fact(DisplayName = "Serialization should build ClusterShardingMessageSerializer under id 13, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_sharding_serializer_id_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(13).Should().BeOfType<ClusterShardingMessageSerializer>();
            serialization.FindSerializerForType(typeof(ShardRegion.StartEntity)).Should().BeOfType<ClusterShardingMessageSerializer>();
        }

        /// <remarks>
        /// Akka.Cluster.Sharding is deployed with this test project, so its module default registers on startup -
        /// a plain system resolves the id without ever starting sharding or loading its reference.conf rows.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve the sharding serializer by id on a plain system with sharding not started")]
        public async Task Should_resolve_the_sharding_serializer_When_a_plain_system_has_no_extension_started()
        {
            var system = ActorSystem.Create("sharding-no-extension");
            InitializeLogger(system);
            try
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;
                serialization.GetSerializerById(13).Should().BeOfType<ClusterShardingMessageSerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
