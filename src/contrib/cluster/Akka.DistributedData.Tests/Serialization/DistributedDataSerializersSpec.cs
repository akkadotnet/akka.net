//-----------------------------------------------------------------------
// <copyright file="DistributedDataSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
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
    /// Keeps <see cref="DistributedDataSerializers"/> in sync with Akka.DistributedData's reference.conf. The
    /// shared checks live in <see cref="ModuleSerializerSpecs"/>. Unlike Remote/Streams/Cluster, this test
    /// project has no <c>InternalsVisibleTo</c> grant from Akka core, so it cannot see the internal
    /// <c>ModuleSerializer</c> record (and skips the reflection-only baseline) or the internal
    /// <c>Serialization.GetSerializerById</c>; the serializer-type list below is a hand-kept mirror of
    /// <see cref="DistributedDataSerializers"/> instead of a live read of its <c>Serializers</c> property.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DistributedDataSerializersSpec : AkkaSpec
    {
        private static readonly Config DDataRows = DistributedData.DefaultConfig();

        private static readonly Type[] SerializerTypes =
        {
            typeof(ReplicatedDataSerializer), typeof(ReplicatorMessageSerializer)
        };

        public DistributedDataSerializersSpec(ITestOutputHelper output) : base(DDataRows, output)
        {
        }

        [Fact(DisplayName = "DistributedDataSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new DistributedDataSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(DDataRows, SerializerTypes, table.BoundTypes);
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("ddata-aqn", DDataRows, Sys);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_DistributedData_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.DistributedData.IReplicatorMessage, Akka.DistributedData"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("ddata-override", overrides, DDataRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Subscribe)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(GSet<string>)).Should().BeOfType<ReplicatedDataSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should build every DistributedData serializer under its usual class, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_DistributedData_serializer_classes_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.FindSerializerForType(typeof(GSet<string>)).Should().BeOfType<ReplicatedDataSerializer>();
            serialization.FindSerializerForType(typeof(Subscribe)).Should().BeOfType<ReplicatorMessageSerializer>();
        }
    }
}
