//-----------------------------------------------------------------------
// <copyright file="ShardingSerializersSpec.cs" company="Akka.NET Project">
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
using Akka.Cluster.Sharding.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

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
    /// checks live in <see cref="ModuleSerializerSpecs"/>; this spec adds what is specific to Sharding, including
    /// the internal API needed to force a reflection-only baseline for the parity comparison below.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ShardingSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        // sharding's own rows only; ClusterSharding.DefaultConfig() also falls back to DistributedData's reference.conf
        private static readonly Config ShardingRows =
            ConfigurationFactory.FromResource<ClusterSharding>("Akka.Cluster.Sharding.reference.conf");

        public ShardingSerializersSpec(ITestOutputHelper output) : base(ClusterSharding.DefaultConfig(), output)
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

        [Fact(DisplayName = "ShardingSerializers should resolve every reference.conf row to the type and serializer reflection does")]
        public void Should_match_reflection_When_resolving_every_reference_conf_row()
        {
            var table = new ShardingSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(ShardingRows, table.Serializers.Select(s => s.Type), table.BoundTypes);

            // core's module map names Akka.Cluster.Sharding, and ShardingSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Cluster.Sharding").Should().NotBeNull();

            var reflected = Build(Sys, NoModules, dynamicTypeLoading: true);
            var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading: true);
            var settings = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");
            var aliasByType = ModuleSerializerSpecs.SerializerRows(ShardingRows)
                .ToDictionary(r => Type.GetType(r.TypeName, throwOnError: true)!, r => r.Alias);

            foreach (var (type, create) in table.Serializers.Select(s => (s.Type, s.Create)))
            {
                var built = create((ExtendedActorSystem)Sys, settings.GetConfig(aliasByType[type]));
                built.Should().BeOfType(reflected.GetSerializerById(built.Identifier).GetType(), type.Name);
                fromTable.GetSerializerById(built.Identifier).Should().BeOfType(type, type.Name);
            }

            fromTable.FindSerializerForType(typeof(ShardRegion.StartEntity)).Should()
                .BeOfType(reflected.FindSerializerForType(typeof(ShardRegion.StartEntity)).GetType());
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
