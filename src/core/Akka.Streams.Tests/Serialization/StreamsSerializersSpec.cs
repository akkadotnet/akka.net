//-----------------------------------------------------------------------
// <copyright file="StreamsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Streams.Tests.Serialization
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
    /// Keeps <see cref="StreamsSerializers"/> in sync with Akka.Streams' reference.conf. The shared checks live in
    /// <see cref="ModuleSerializerSpecs"/>; this spec adds what is specific to Streams, including the internal
    /// API needed to force a reflection-only baseline for the parity comparison below.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class StreamsSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        private static readonly Config StreamsRows = ActorMaterializer.DefaultConfig();

        private static readonly Type[] BoundSamples = { typeof(SinkRefImpl<int>), typeof(SourceRefImpl<int>), typeof(CumulativeDemand) };

        public StreamsSerializersSpec(ITestOutputHelper output) : base(StreamsRows, output)
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

        [Fact(DisplayName = "StreamsSerializers should resolve every reference.conf row to the type and serializer reflection does")]
        public async Task Should_match_reflection_When_resolving_every_reference_conf_row()
        {
            await ModuleSerializerSpecs.WithSystem("streams-parity", Config.Empty, StreamsRows, system =>
            {
                var table = new StreamsSerializers();
                var details = table.Create((ExtendedActorSystem)system);
                ModuleSerializerSpecs.AssertTableMatchesConfig(StreamsRows, details);

                // core's module map names Akka.Streams, and StreamsSerializers is what it loads for it
                ModuleSerializerTable.Default.ForAssembly("Akka.Streams").Should().NotBeNull();

                var reflected = Build(system, NoModules, dynamicTypeLoading: true);
                var fromTable = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: true);

                foreach (var entry in details)
                {
                    entry.Serializer.Identifier.Should().Be(30);
                    entry.Serializer.Should().BeOfType(reflected.GetSerializerById(30).GetType(), entry.Alias);
                    fromTable.GetSerializerById(30).Should().BeOfType(entry.Serializer.GetType(), entry.Alias);
                }

                foreach (var type in BoundSamples)
                    fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType());
            });
        }

        [Fact(DisplayName = "StreamsSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new StreamsSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(StreamsRows, table.Create((ExtendedActorSystem)Sys));
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("streams-aqn", StreamsRows, Sys, BoundSamples);

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Streams_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("streams-override", overrides, StreamsRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(SinkRefImpl<int>)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(SourceRefImpl<int>)).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should build StreamRefSerializer under id 30, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_stream_ref_serializer_id_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(30).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            foreach (var type in BoundSamples)
                serialization.FindSerializerForType(type).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
        }

        [Fact(DisplayName = "StreamRefSerializer should serialize a SourceRef but fail to deserialize it When dynamic type loading is off")]
        public void Should_serialize_but_not_deserialize_a_SourceRef_When_dynamic_type_loading_is_disabled()
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                var serializer = new Akka.Streams.Serialization.StreamRefSerializer((ExtendedActorSystem)Sys);
                var sourceRef = new SourceRefImpl<int>(Sys.DeadLetters);

                // sending a stream ref needs no reflection - only typeof(T), which the trimmer can always see
                var manifest = serializer.Manifest(sourceRef);
                var bytes = serializer.ToBinary(sourceRef);

                // receiving one does - SerializationTools.TypeFromString has to turn the wire name back into a Type
                var exception = Assert.Throws<SerializationException>(() => serializer.FromBinary(bytes, manifest));

                exception.Message.Should().Contain(SwitchName);
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }
    }
}
