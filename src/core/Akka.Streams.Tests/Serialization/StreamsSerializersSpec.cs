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
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization;
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
    /// Keeps <see cref="StreamsSerializers"/> in sync with Akka.Streams' reference.conf. Each system boots with the
    /// switch on; each test builds another <see cref="AkkaSerialization"/> through the table or by reflection alone.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class StreamsSerializersSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        private static readonly Config StreamsRows = ActorMaterializer.DefaultConfig();

        private static IEnumerable<(string Alias, string TypeName)> SerializerRows =>
            StreamsRows.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        private static IEnumerable<(string TypeName, string Alias)> BindingRows =>
            StreamsRows.GetConfig("akka.actor.serialization-bindings").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        private static readonly Type[] BoundSamples = { typeof(SinkRefImpl<int>), typeof(SourceRefImpl<int>), typeof(CumulativeDemand) };

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

        private static async Task WithSystem(string name, Config config, Action<ActorSystem> body)
        {
            var system = ActorSystem.Create(name, config.WithFallback(StreamsRows).WithFallback(ConfigurationFactory.Default()));
            try
            {
                body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static (string Name, string? Assembly) Split(string typeName)
        {
            Akka.Util.TypeExtensions.TrySplitTypeName(typeName, out var name, out var assembly);
            return (name, assembly);
        }

        [Fact(DisplayName = "StreamsSerializers should resolve every reference.conf row to the type and serializer reflection does")]
        public async Task Should_match_reflection_When_resolving_every_reference_conf_row()
        {
            var module = ModuleSerializerTable.Default.ForAssembly("Akka.Streams");
            module.Should().NotBeNull("core's module map names Akka.Streams");

            await WithSystem("streams-parity", Config.Empty, system =>
            {
                var reflected = Build(system, NoModules, dynamicTypeLoading: true);
                var fromTable = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: true);
                var settings = system.Settings.Config.GetConfig("akka.actor.serialization-settings");

                foreach (var (alias, typeName) in SerializerRows)
                {
                    var (name, assembly) = Split(typeName);
                    var entry = module!.FindSerializer(name, assembly);
                    entry.Should().NotBeNull(typeName);
                    entry!.Type.Should().Be(Type.GetType(typeName, throwOnError: true));

                    // the factory builds what reflection builds, under the same id
                    var built = entry.Create((ExtendedActorSystem)system, settings.GetConfig(alias));
                    built.Identifier.Should().Be(30);
                    built.Should().BeOfType(reflected.GetSerializerById(30).GetType(), alias);
                    fromTable.GetSerializerById(30).Should().BeOfType(entry.Type, alias);
                }

                foreach (var (typeName, _) in BindingRows)
                {
                    var (name, assembly) = Split(typeName);
                    module!.FindBoundType(name, assembly).Should().Be(Type.GetType(typeName, throwOnError: true), typeName);
                }

                foreach (var type in BoundSamples)
                    fromTable.FindSerializerForType(type).Should().BeOfType(reflected.FindSerializerForType(type).GetType());
            });
        }

        [Fact(DisplayName = "StreamsSerializers should list no serializer or type that reference.conf does not")]
        public void Should_have_a_reference_conf_row_When_the_table_lists_a_type()
        {
            var table = new StreamsSerializers();

            table.Serializers.Select(s => s.Type)
                .Except(SerializerRows.Select(r => Type.GetType(r.TypeName, throwOnError: true)))
                .Should().BeEmpty();
            table.BoundTypes
                .Except(BindingRows.Select(r => Type.GetType(r.TypeName, throwOnError: true)))
                .Should().BeEmpty();
        }

        [Fact(DisplayName = "Serialization should resolve reference.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
        {
            string Aqn(string typeName) => Type.GetType(typeName, throwOnError: true)!.AssemblyQualifiedName!;
            var hosting = ConfigurationFactory.ParseString(string.Join("\n",
                SerializerRows.Select(r => $@"akka.actor.serializers.{r.Alias} = ""{Aqn(r.TypeName)}""")
                    .Concat(BindingRows.Select(r => $@"akka.actor.serialization-bindings {{ ""{Aqn(r.TypeName)}"" = {r.Alias} }}"))));

            await WithSystem("streams-aqn", hosting, system =>
            {
                var serialization = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: false);

                serialization.GetSerializerById(30).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
                foreach (var type in BoundSamples)
                    serialization.FindSerializerForType(type).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf override a reference.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Streams_type()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams"" = bytes }");

            await WithSystem("streams-override", overrides, system =>
            {
                var serialization = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(SinkRefImpl<int>)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(SourceRefImpl<int>)).Should().BeOfType<Akka.Streams.Serialization.StreamRefSerializer>();
            });
        }
    }
}
