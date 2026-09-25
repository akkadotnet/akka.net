//-----------------------------------------------------------------------
// <copyright file="RemoteSerializersSpec.cs" company="Akka.NET Project">
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
using Akka.Remote.Artery;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Remote.Tests.Serialization
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
    /// Keeps <see cref="RemoteSerializers"/> in sync with Remote.conf. Sys boots a remote provider with the switch on;
    /// each test builds another <see cref="AkkaSerialization"/> from a config, through the table or by reflection alone.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class RemoteSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly Config RemoteProvider = ConfigurationFactory.ParseString(@"
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp { hostname = 127.0.0.1, port = 0 }");

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        private static readonly Config RemoteRows = RemoteConfigFactory.Default();

        private static IEnumerable<(string Alias, string TypeName)> SerializerRows =>
            RemoteRows.GetConfig("akka.actor.serializers").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        private static IEnumerable<(string TypeName, string Alias)> BindingRows =>
            RemoteRows.GetConfig("akka.actor.serialization-bindings").AsEnumerable().Select(kv => (kv.Key, kv.Value.GetString()));

        public RemoteSerializersSpec(ITestOutputHelper output) : base(RemoteProvider, output)
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

        private static async Task WithSystem(string name, Config config, Action<ActorSystem> body)
        {
            var system = ActorSystem.Create(name, config.WithFallback(ConfigurationFactory.Default()));
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

        [Fact(DisplayName = "RemoteSerializers should resolve every Remote.conf row to the type and serializer reflection does")]
        public void Should_match_reflection_When_resolving_every_Remote_conf_row()
        {
            var module = ModuleSerializerTable.Default.ForAssembly("Akka.Remote");
            module.Should().NotBeNull("core's module map names Akka.Remote");

            var reflected = Build(Sys, NoModules, dynamicTypeLoading: true);
            var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading: true);
            var settings = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");

            foreach (var (alias, typeName) in SerializerRows)
            {
                var (name, assembly) = Split(typeName);
                var entry = module!.FindSerializer(name, assembly);
                entry.Should().NotBeNull(typeName);
                entry!.Type.Should().Be(Type.GetType(typeName, throwOnError: true));

                // the factory builds what reflection builds, under the same id
                var built = entry.Create((ExtendedActorSystem)Sys, settings.GetConfig(alias));
                built.Should().BeOfType(reflected.GetSerializerById(built.Identifier).GetType(), alias);
                fromTable.GetSerializerById(built.Identifier).Should().BeOfType(entry.Type, alias);
            }

            foreach (var (typeName, alias) in BindingRows)
            {
                var (name, assembly) = Split(typeName);
                var type = Type.GetType(typeName, throwOnError: true)!;
                module!.FindBoundType(name, assembly).Should().Be(type, typeName);

                var expected = reflected.FindSerializerForType(type);
                var actual = fromTable.FindSerializerForType(type);
                actual.Should().BeOfType(expected.GetType(), typeName);
                actual.Identifier.Should().Be(expected.Identifier, typeName);
            }

            // primitive's settings block reached the factory
            fromTable.FindSerializerForType(typeof(string)).Manifest("s")
                .Should().Be(reflected.FindSerializerForType(typeof(string)).Manifest("s"));
        }

        [Fact(DisplayName = "RemoteSerializers should list no serializer or type that Remote.conf does not")]
        public void Should_have_a_Remote_conf_row_When_the_table_lists_a_type()
        {
            var table = new RemoteSerializers();

            table.Serializers.Select(s => s.Type)
                .Except(SerializerRows.Select(r => Type.GetType(r.TypeName, throwOnError: true)))
                .Should().BeEmpty();
            table.BoundTypes
                .Except(BindingRows.Select(r => Type.GetType(r.TypeName, throwOnError: true)))
                .Should().BeEmpty();
        }

        [Fact(DisplayName = "Serialization should resolve Remote.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
        {
            string Aqn(string typeName) => Type.GetType(typeName, throwOnError: true)!.AssemblyQualifiedName!;
            var hosting = ConfigurationFactory.ParseString(string.Join("\n",
                SerializerRows.Select(r => $@"akka.actor.serializers.{r.Alias} = ""{Aqn(r.TypeName)}""")
                    .Concat(BindingRows.Select(r => $@"akka.actor.serialization-bindings {{ ""{Aqn(r.TypeName)}"" = {r.Alias} }}"))));

            await WithSystem("remote-aqn", hosting.WithFallback(RemoteRows), system =>
            {
                var serialization = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: false);

                foreach (var (typeName, _) in BindingRows)
                {
                    var type = Type.GetType(typeName, throwOnError: true)!;
                    serialization.FindSerializerForType(type)
                        .Should().BeOfType(Sys.Serialization.FindSerializerForType(type).GetType(), typeName);
                }
            });
        }

        [Fact(DisplayName = "Serialization should build every Remote serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Remote_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            AkkaSerialization? serialization = null;
            await EventFilter.Warning().ExpectAsync(0, () =>
            {
                serialization = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading: false);
                return Task.CompletedTask;
            });

            var ids = new Dictionary<int, Type>
            {
                [2] = typeof(ProtobufSerializer),
                [3] = typeof(DaemonMsgCreateSerializer),
                [6] = typeof(MessageContainerSerializer),
                [16] = typeof(MiscMessageSerializer),
                [17] = typeof(PrimitiveSerializers),
                [22] = typeof(SystemMessageSerializer),
                [23] = typeof(ArteryControlMessageSerializer),
            };
            foreach (var (id, type) in ids)
                serialization!.GetSerializerById(id).Should().BeOfType(type);

            serialization!.FindSerializerForType(typeof(IArteryControlMessage)).Should().BeOfType<ArteryControlMessageSerializer>();
            serialization.FindSerializerForType(typeof(HandshakeReq)).Should().BeOfType<ArteryControlMessageSerializer>();
        }

        [Fact(DisplayName = "Serialization should let application.conf override a Remote.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Remote_type()
        {
            var overrides = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-bindings { ""Akka.Actor.Identify, Akka"" = bytes }
                akka.actor.serialization-settings.primitive.use-legacy-behavior = off");

            await WithSystem("remote-override", overrides.WithFallback(RemoteRows), system =>
            {
                var serialization = Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Manifest("s").Should().Be("S");
            });
        }

        [Fact(DisplayName = "Serialization should neither probe nor register Remote's serializers when the config has no Remote rows")]
        public async Task Should_not_probe_Remote_When_a_local_config_has_no_Remote_rows()
        {
            var probes = 0;
            var table = new ModuleSerializerTable(new Dictionary<string, Func<ModuleSerializers?>>
            {
                ["Akka.Remote"] = () =>
                {
                    probes++;
                    return new RemoteSerializers();
                }
            });

            // types Remote.conf also binds, named by Akka.dll and CoreLib: neither assembly half leads to Remote's table
            var local = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-bindings {
                    ""Akka.Actor.Identify, Akka"" = bytes
                    ""System.String"" = bytes
                }");

            await WithSystem("remote-absent", local, system =>
            {
                var serialization = Build(system, table, dynamicTypeLoading: true);

                probes.Should().Be(0);
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(IActorRef)).Should().BeOfType<NewtonSoftJsonSerializer>();
            });
        }

        /// <remarks>Reflection hands a non-empty block to a (system, config) constructor this class lacks; the table does not.</remarks>
        [Fact(DisplayName = "Serialization should build akka-misc despite a settings block when dynamic type loading is on")]
        public async Task Should_build_a_one_constructor_serializer_When_it_has_a_settings_block()
        {
            var settings = ConfigurationFactory.ParseString("akka.actor.serialization-settings.akka-misc { x = 1 }");

            await WithSystem("remote-misc-settings", settings.WithFallback(RemoteRows), system =>
            {
                Build(system, ModuleSerializerTable.Default, dynamicTypeLoading: true)
                    .GetSerializerById(16).Should().BeOfType<MiscMessageSerializer>();
            });
        }
    }
}
