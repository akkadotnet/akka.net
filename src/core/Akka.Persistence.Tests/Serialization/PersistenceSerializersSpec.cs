//-----------------------------------------------------------------------
// <copyright file="PersistenceSerializersSpec.cs" company="Akka.NET Project">
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
    /// Keeps <see cref="PersistenceSerializers"/> in sync with persistence.conf. The shared checks live in
    /// <see cref="ModuleSerializerSpecs"/>; this spec adds what is specific to Persistence, including the internal
    /// API needed to force a reflection-only baseline for the parity comparison below.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistenceSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        private static readonly Config PersistenceRows = Persistence.DefaultConfig();

        private static readonly Type[] BoundSamples = { typeof(AtLeastOnceDeliverySnapshot), typeof(Akka.Persistence.Serialization.Snapshot) };

        public PersistenceSerializersSpec(ITestOutputHelper output) : base(PersistenceRows, output)
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

        [Fact(DisplayName = "PersistenceSerializers should resolve every persistence.conf row to the type and serializer reflection does")]
        public void Should_match_reflection_When_resolving_every_persistence_conf_row()
        {
            var table = new PersistenceSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(PersistenceRows, table.Serializers.Select(s => (s.Alias, s.Type, s.Bindings)));

            // core's module map names Akka.Persistence, and PersistenceSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Persistence").Should().NotBeNull();

            var reflected = Build(Sys, NoModules, dynamicTypeLoading: true);
            var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading: true);
            var settings = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");

            foreach (var (type, create, alias) in table.Serializers.Select(s => (s.Type, s.Create, s.Alias)))
            {
                var built = create((ExtendedActorSystem)Sys, settings.GetConfig(alias));
                built.Should().BeOfType(reflected.GetSerializerById(built.Identifier).GetType(), type.Name);
                fromTable.GetSerializerById(built.Identifier).Should().BeOfType(type, type.Name);
            }

            foreach (var type in BoundSamples)
                fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType(), type.FullName);
        }

        [Fact(DisplayName = "PersistenceSerializers should list no serializer or type that persistence.conf does not")]
        public void Should_have_a_persistence_conf_row_When_the_table_lists_a_type()
        {
            var table = new PersistenceSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(PersistenceRows, table.Serializers.Select(s => (s.Alias, s.Type, s.Bindings)));
        }

        [Fact(DisplayName = "Serialization should resolve persistence.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("persistence-aqn", PersistenceRows, Sys, BoundSamples);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Persistence_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Persistence.Serialization.Snapshot, Akka.Persistence"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("persistence-override", overrides, PersistenceRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Akka.Persistence.Serialization.Snapshot)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(AtLeastOnceDeliverySnapshot)).Should().BeOfType<PersistenceMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should build every Persistence serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Persistence_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(7).Should().BeOfType<PersistenceMessageSerializer>();
            serialization.GetSerializerById(8).Should().BeOfType<PersistenceSnapshotSerializer>();

            serialization.FindSerializerForType(typeof(AtLeastOnceDeliverySnapshot)).Should().BeOfType<PersistenceMessageSerializer>();
            serialization.FindSerializerForType(typeof(Akka.Persistence.Serialization.Snapshot)).Should().BeOfType<PersistenceSnapshotSerializer>();
        }
    }
}
