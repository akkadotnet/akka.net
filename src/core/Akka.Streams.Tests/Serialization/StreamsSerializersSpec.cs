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
using System.Collections.Immutable;
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
    /// Checks how <see cref="StreamsSerializers"/> behaves in a running system. What the table contains is approved in
    /// Akka.API.Tests (<c>SerializerTableSpec</c>). The shared checks live in <see cref="ModuleSerializerSpecs"/>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class StreamsSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly Type[] BoundSamples = { typeof(SinkRefImpl<int>), typeof(SourceRefImpl<int>), typeof(CumulativeDemand) };

        public StreamsSerializersSpec(ITestOutputHelper output) : base(ActorMaterializer.DefaultConfig(), output)
        {
        }

        private ImmutableHashSet<SerializerDetails> Table => new StreamsSerializers().Create((ExtendedActorSystem)Sys);

        [Fact(DisplayName = "Serialization should resolve every Streams table entry on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_table_When_a_plain_system_has_no_Streams_rows()
        {
            // core's module map names Akka.Streams, and StreamsSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Streams").Should().NotBeNull();

            await ModuleSerializerSpecs.AssertPlainSystemResolvesTable("streams-plain", Table, (s, id) => s.GetSerializerById(id));
        }

        [Fact(DisplayName = "StreamsSerializers should build without throwing on a system that never loaded its reference.conf")]
        public async Task Should_build_without_throwing_When_its_config_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("streams-no-config", s => new StreamsSerializers().Create(s));

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("streams-aqn", Table, Sys, BoundSamples);

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Streams binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Streams_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("streams-override", overrides, withCopiedRows ? ModuleSerializerSpecs.RowsOf(Table) : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(SinkRefImpl<int>)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(SourceRefImpl<int>)).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace the built-in stream-ref alias when dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_the_stream_ref_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("streams-alias-override", "akka-stream-ref",
                typeof(SinkRefImpl<int>), typeof(SourceRefImpl<int>), typeof(CumulativeDemand));

        [Fact(DisplayName = "Serialization should build StreamRefSerializer under id 30, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_stream_ref_serializer_id_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

            serialization.GetSerializerById(30).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            foreach (var type in BoundSamples)
                serialization.FindSerializerForType(type).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
        }

        /// <remarks>
        /// Akka.Streams is deployed with this test project, so its module default registers on startup - a plain
        /// system resolves the stream-ref serializer by id without ever creating a materializer or loading
        /// Streams' own reference.conf rows.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve the stream-ref serializer by id on a plain system that never created a materializer")]
        public async Task Should_resolve_the_stream_ref_serializer_When_a_plain_system_has_no_materializer()
        {
            var system = ActorSystem.Create("streams-no-materializer");
            InitializeLogger(system);
            try
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;
                serialization.GetSerializerById(30).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            }
            finally
            {
                await system.Terminate();
            }
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
