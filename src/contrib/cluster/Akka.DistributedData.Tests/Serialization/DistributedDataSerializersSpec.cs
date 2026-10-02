//-----------------------------------------------------------------------
// <copyright file="DistributedDataSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization
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
    /// Checks how <see cref="DistributedDataSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DistributedDataSerializersSpec : AkkaSpec
    {
        public DistributedDataSerializersSpec(ITestOutputHelper output) : base(DistributedData.DefaultConfig(), output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new DistributedDataSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every DistributedData table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_DistributedData_rows()
        {
            // core's module map names Akka.DistributedData, and DistributedDataSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.DistributedData").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("ddata-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "DistributedDataSerializers should build without throwing on a system that never loaded its reference.conf")]
        public async Task Should_build_without_throwing_When_its_config_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("ddata-no-config", s => new DistributedDataSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("ddata-aqn", Table, Sys);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in DistributedData binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_DistributedData_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.DistributedData.IReplicatorMessage, Akka.DistributedData"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("ddata-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Subscribe)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(GSet<string>)).Should().BeOfType<ReplicatedDataSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace a built-in DistributedData alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_a_DistributedData_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("ddata-alias-override", "akka-replicated-data",
                typeof(GSet<string>));

        [Fact(DisplayName = "Serialization should build every DistributedData serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_DistributedData_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(11).Should().BeOfType<ReplicatedDataSerializer>();
            serialization.GetSerializerById(12).Should().BeOfType<ReplicatorMessageSerializer>();

            serialization.FindSerializerForType(typeof(GSet<string>)).Should().BeOfType<ReplicatedDataSerializer>();
            serialization.FindSerializerForType(typeof(Subscribe)).Should().BeOfType<ReplicatorMessageSerializer>();
        }

        /// <remarks>
        /// Akka.DistributedData is deployed with this test project, so its module default registers on startup -
        /// a plain system resolves both ids without ever starting the Replicator extension or loading
        /// DistributedData's own reference.conf rows.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve DistributedData serializers by id on a plain system with no Replicator started")]
        public async Task Should_resolve_DistributedData_serializers_When_a_plain_system_has_no_extension_started()
        {
            var system = ActorSystem.Create("ddata-no-extension");
            InitializeLogger(system);
            try
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;
                serialization.GetSerializerById(11).Should().BeOfType<ReplicatedDataSerializer>();
                serialization.GetSerializerById(12).Should().BeOfType<ReplicatorMessageSerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
