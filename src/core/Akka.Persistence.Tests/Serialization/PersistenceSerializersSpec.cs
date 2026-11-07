//-----------------------------------------------------------------------
// <copyright file="PersistenceSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Persistence.Tests.Serialization
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
    /// Checks how <see cref="PersistenceSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistenceSerializersSpec : AkkaSpec
    {
        private static readonly Type[] BoundSamples = { typeof(AtLeastOnceDeliverySnapshot), typeof(Akka.Persistence.Serialization.Snapshot) };

        public PersistenceSerializersSpec(ITestOutputHelper output) : base(Persistence.DefaultConfig(), output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new PersistenceSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every Persistence table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_Persistence_rows()
        {
            // core's module map names Akka.Persistence, and PersistenceSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Persistence").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("persistence-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "PersistenceSerializers should build without throwing on a system that never loaded persistence.conf")]
        public async Task Should_build_without_throwing_When_persistence_conf_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("persistence-no-config", s => new PersistenceSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve persistence.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("persistence-aqn", Table, Sys, BoundSamples);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Persistence binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Persistence_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Persistence.Serialization.Snapshot, Akka.Persistence"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("persistence-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Akka.Persistence.Serialization.Snapshot)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(AtLeastOnceDeliverySnapshot)).Should().BeOfType<PersistenceMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace a built-in Persistence alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_a_Persistence_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("persistence-alias-override", "akka-persistence-message",
                typeof(AtLeastOnceDeliverySnapshot));

        [Fact(DisplayName = "Serialization should build every Persistence serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Persistence_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(7).Should().BeOfType<PersistenceMessageSerializer>();
            serialization.GetSerializerById(8).Should().BeOfType<PersistenceSnapshotSerializer>();

            serialization.FindSerializerForType(typeof(AtLeastOnceDeliverySnapshot)).Should().BeOfType<PersistenceMessageSerializer>();
            serialization.FindSerializerForType(typeof(Akka.Persistence.Serialization.Snapshot)).Should().BeOfType<PersistenceSnapshotSerializer>();
        }

        /// <remarks>
        /// Akka.Persistence is deployed with this test project, so its module default registers on startup - a
        /// plain system resolves both ids without ever starting a journal or snapshot store plugin.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve Persistence serializers by id on a plain system with no persistence plugin started")]
        public async Task Should_resolve_Persistence_serializers_When_a_plain_system_has_no_extension_started()
        {
            var system = ActorSystem.Create("persistence-no-extension");
            InitializeLogger(system);
            try
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;
                serialization.GetSerializerById(7).Should().BeOfType<PersistenceMessageSerializer>();
                serialization.GetSerializerById(8).Should().BeOfType<PersistenceSnapshotSerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
