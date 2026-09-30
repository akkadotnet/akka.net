//-----------------------------------------------------------------------
// <copyright file="StreamsSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization;
using Akka.TestKit;
using Akka.Util;
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
                ModuleSerializerSpecs.AssertTableMatchesConfig(StreamsRows, table.Serializers.Select(s => s.Type), table.BoundTypes);

                // core's module map names Akka.Streams, and StreamsSerializers is what it loads for it
                ModuleSerializerTable.Default.ForAssembly("Akka.Streams").Should().NotBeNull();

                var reflected = Build(system, NoModules, dynamicTypeLoading: true);
                var fromTable = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: true);
                var settings = system.Settings.Config.GetConfig("akka.actor.serialization-settings");
                var aliasByType = ModuleSerializerSpecs.SerializerRows(StreamsRows)
                    .ToDictionary(r => Type.GetType(r.TypeName, throwOnError: true)!, r => r.Alias);

                foreach (var (type, create) in table.Serializers.Select(s => (s.Type, s.Create)))
                {
                    var built = create((ExtendedActorSystem)system, settings.GetConfig(aliasByType[type]));
                    built.Identifier.Should().Be(30);
                    built.Should().BeOfType(reflected.GetSerializerById(30).GetType(), type.Name);
                    fromTable.GetSerializerById(30).Should().BeOfType(type, type.Name);
                }

                foreach (var type in BoundSamples)
                    fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType());
            });
        }

        [Fact(DisplayName = "StreamsSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new StreamsSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(StreamsRows, table.Serializers.Select(s => s.Type), table.BoundTypes);
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

        /// <summary>
        /// Runs <paramref name="body"/> with <c>Akka.DynamicTypeLoading</c> forced off and restores whatever the
        /// switch reported beforehand - unset reads back as on, matching <see cref="AkkaFeatures"/>'s own default.
        /// </summary>
        private static void WithDynamicTypeLoadingOff(Action body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        [Fact(DisplayName = "SerializationTools should reject a stream-ref element type name When dynamic type loading is off")]
        public void Should_throw_SerializationException_When_resolving_a_stream_ref_element_type_name_with_dynamic_type_loading_disabled()
        {
            WithDynamicTypeLoadingOff(() =>
            {
                var typeName = typeof(string).AssemblyQualifiedName!;

                var exception = Assert.Throws<SerializationException>(
                    () => SerializationTools.TypeFromString(typeName));

                exception.Message.Should().Contain(typeName);
                exception.Message.Should().Contain(SwitchName);
            });
        }

        [Fact(DisplayName = "SinkRefImpl.Create should reject building a closed generic When dynamic type loading is off")]
        public void Should_throw_SerializationException_When_creating_a_SinkRefImpl_with_dynamic_type_loading_disabled()
        {
            WithDynamicTypeLoadingOff(() =>
            {
                var exception = Assert.Throws<SerializationException>(
                    () => SinkRefImpl.Create(typeof(string), Sys.DeadLetters));

                exception.Message.Should().Contain(SwitchName);
            });
        }

        [Fact(DisplayName = "SourceRefImpl.Create should reject building a closed generic When dynamic type loading is off")]
        public void Should_throw_SerializationException_When_creating_a_SourceRefImpl_with_dynamic_type_loading_disabled()
        {
            WithDynamicTypeLoadingOff(() =>
            {
                var exception = Assert.Throws<SerializationException>(
                    () => SourceRefImpl.Create(typeof(string), Sys.DeadLetters));

                exception.Message.Should().Contain(SwitchName);
            });
        }
    }
}
