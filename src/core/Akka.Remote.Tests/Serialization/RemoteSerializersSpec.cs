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
using Akka.Dispatch.SysMsg;
using Akka.Remote.Artery;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
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
    /// Keeps <see cref="RemoteSerializers"/> in sync with Remote.conf. The shared checks live in
    /// <see cref="ModuleSerializerSpecs"/>; this spec adds what is specific to Remote, including the internal
    /// API needed to force a reflection-only baseline for the parity comparison below.
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

        /// <summary>Holds the switch at <paramref name="value"/> for <paramref name="body"/>, same as <see cref="Build"/> but for an arbitrary call.</summary>
        private static async Task WithSwitchAsync(bool value, Func<Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, value);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        [Fact(DisplayName = "RemoteSerializers should resolve every Remote.conf row to the type and serializer reflection does")]
        public void Should_match_reflection_When_resolving_every_Remote_conf_row()
        {
            var table = new RemoteSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(RemoteRows, table.Serializers.Select(s => (s.Alias, s.Type, s.Bindings)));

            // core's module map names Akka.Remote, and RemoteSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Remote").Should().NotBeNull();

            var reflected = Build(Sys, NoModules, dynamicTypeLoading: true);
            var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading: true);
            var settings = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");

            foreach (var (type, create, alias) in table.Serializers.Select(s => (s.Type, s.Create, s.Alias)))
            {
                // the factory builds what reflection builds, under the same id
                var built = create((ExtendedActorSystem)Sys, settings.GetConfig(alias));
                built.Should().BeOfType(reflected.GetSerializerById(built.Identifier).GetType(), type.Name);
                fromTable.GetSerializerById(built.Identifier).Should().BeOfType(type, type.Name);
            }

            foreach (var type in table.Serializers.SelectMany(s => s.Bindings))
            {
                var expected = reflected.FindSerializerForType(type);
                var actual = fromTable.FindSerializerForType(type);
                actual.Should().BeOfType(expected.GetType(), type.Name);
                actual.Identifier.Should().Be(expected.Identifier, type.Name);
            }

            // primitive's settings block reached the factory
            fromTable.FindSerializerForType(typeof(string)).Manifest("s")
                .Should().Be(reflected.FindSerializerForType(typeof(string)).Manifest("s"));
        }

        [Fact(DisplayName = "RemoteSerializers should list no serializer or type that Remote.conf does not")]
        public void Should_have_a_Remote_conf_row_When_the_table_lists_a_type()
        {
            var table = new RemoteSerializers();
            ModuleSerializerSpecs.AssertTableMatchesConfig(RemoteRows, table.Serializers.Select(s => (s.Alias, s.Type, s.Bindings)));
        }

        [Fact(DisplayName = "Serialization should resolve Remote.conf rows spelled as Akka.Hosting writes them when dynamic type loading is off")]
        public async Task Should_resolve_assembly_qualified_names_When_dynamic_type_loading_is_disabled()
            => await ModuleSerializerSpecs.AssertHostingSpellingResolves("remote-aqn", RemoteRows, Sys);

        [Fact(DisplayName = "Serialization should build every Remote serializer under its usual id, without a warning, when dynamic type loading is off")]
        public async Task Should_keep_the_Remote_serializer_ids_When_dynamic_type_loading_is_disabled()
        {
            var serialization = await ModuleSerializerSpecs.AssertBuildsWithoutWarning(Sys, EventFilter);

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
                serialization.GetSerializerById(id).Should().BeOfType(type);

            serialization.FindSerializerForType(typeof(IArteryControlMessage)).Should().BeOfType<ArteryControlMessageSerializer>();
            serialization.FindSerializerForType(typeof(HandshakeReq)).Should().BeOfType<ArteryControlMessageSerializer>();
        }

        [Fact(DisplayName = "Serialization should let application.conf override a Remote.conf row when dynamic type loading is off")]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Remote_type()
        {
            var overrides = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-bindings { ""Akka.Actor.Identify, Akka"" = bytes }
                akka.actor.serialization-settings.primitive.use-legacy-behavior = off");

            await ModuleSerializerSpecs.WithSystem("remote-override", overrides, RemoteRows, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

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

            await ModuleSerializerSpecs.WithSystem("remote-absent", local, null, system =>
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

            await ModuleSerializerSpecs.WithSystem("remote-misc-settings", settings, RemoteRows, system =>
            {
                ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: true)
                    .GetSerializerById(16).Should().BeOfType<MiscMessageSerializer>();
            });
        }

        /// <summary>
        /// SystemMessageSerializer.FromBinary(byte[], Type) is covered elsewhere; this goes through the real
        /// Serialization.Deserialize(bytes, id, manifest) with the switch off - the path an earlier draft of
        /// this fix got wrong, because its test used a stand-in serializer and never caught the DeathWatch
        /// break. Two manifests are old-style spellings (one with ProcessorArchitecture), so this can't pass
        /// on a warm TypeCache alone - it needs SystemMessageSerializer's own table.
        /// </summary>
        [Fact(DisplayName = "Serialization should deserialize every SystemMessage manifest When dynamic type loading is off")]
        public async Task Should_deserialize_every_SystemMessage_manifest_When_dynamic_type_loading_is_disabled()
        {
            var serializer = (SystemMessageSerializer)Sys.Serialization.FindSerializerForType(typeof(Terminate));
            var child = ActorOf<BlackHoleActor>();
            var watchee = ActorOf<BlackHoleActor>().AsInstanceOf<IInternalActorRef>();
            var watcher = ActorOf<BlackHoleActor>().AsInstanceOf<IInternalActorRef>();

            object[] messages =
            {
                new Create(null), new Recreate(new Exception("boom")), new Suspend(), new Resume(new Exception("boom")),
                new Supervise(child, true), new Watch(watchee, watcher), new Unwatch(watchee, watcher),
                new Failed(child, new Exception("boom"), 435345), new DeathWatchNotification(child, true, false)
            };

            await WithSwitchAsync(false, () =>
            {
                foreach (var message in messages)
                {
                    var bytes = serializer.ToBinary(message);
                    var manifest = serializer.Manifest(message);
                    Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().BeOfType(message.GetType(), message.GetType().Name);
                }

                Sys.Serialization.Deserialize(serializer.ToBinary(new Terminate()), serializer.Identifier,
                    "Akka.Dispatch.SysMsg.Terminate, Akka, Version=1.3.0.0, Culture=neutral, PublicKeyToken=null").Should().BeOfType<Terminate>();

                Sys.Serialization.Deserialize(serializer.ToBinary(new Create(null)), serializer.Identifier,
                    "Akka.Dispatch.SysMsg.Create, Akka, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null, ProcessorArchitecture=MSIL").Should().BeOfType<Create>();

                return Task.CompletedTask;
            });
        }
    }
}
