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
using Akka.Remote;
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
    /// Keeps <see cref="RemoteSerializers"/> in step with the rows Remote.conf shipped at 1.6.0-beta1, which
    /// <see cref="FrozenSerializerRows"/> keeps. The shared checks live in <see cref="ModuleSerializerSpecs"/>; this
    /// spec adds what is specific to Remote, including the internal API needed to force a reflection-only baseline
    /// for the parity comparison below.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class RemoteSerializersSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private static readonly Config RemoteProvider = ConfigurationFactory.ParseString(@"
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp { hostname = 127.0.0.1, port = 0 }");

        private static readonly ModuleSerializerTable NoModules = new(new Dictionary<string, Func<ModuleSerializers?>>());

        // Remote.conf no longer ships these rows; the table has to keep matching what 1.6.0-beta1 shipped
        private static readonly Config RemoteRows = FrozenSerializerRows.Remote;

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

        [Theory(DisplayName = "Serialization should resolve every built-in Remote serializer and bound type as 1.6.0-beta1 did, with no Remote rows in the config")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_match_the_beta1_rows_When_the_config_has_no_Remote_rows(bool dynamicTypeLoading)
        {
            var details = new RemoteSerializers().Create((ExtendedActorSystem)Sys);

            // core's module map names Akka.Remote, and RemoteSerializers is what it loads for it
            ModuleSerializerTable.Default.ForAssembly("Akka.Remote").Should().NotBeNull();

            // beta1 read the frozen rows from Remote.conf and resolved them by reflection, with no module table
            await ModuleSerializerSpecs.WithSystem("remote-beta1", RemoteConfigFactory.Default(), RemoteRows, system =>
            {
                var reflected = Build(system, NoModules, dynamicTypeLoading: true);

                // Sys carries no Remote rows at all: everything below comes from the table's defaults
                Sys.Settings.Config.HasPath("akka.actor.serializers.akka-misc").Should().BeFalse();
                var fromTable = Build(Sys, ModuleSerializerTable.Default, dynamicTypeLoading);

                foreach (var entry in details)
                {
                    // the table builds what reflection builds, under the same id
                    entry.Serializer.Should().BeOfType(reflected.GetSerializerById(entry.Serializer.Identifier).GetType(), entry.Alias);
                    fromTable.GetSerializerById(entry.Serializer.Identifier).Should().BeOfType(entry.Serializer.GetType(), entry.Alias);
                }

                foreach (var type in details.SelectMany(d => d.UseFor))
                {
                    var expected = reflected.FindSerializerForType(type);
                    var actual = fromTable.FindSerializerForType(type);
                    actual.Should().BeOfType(expected.GetType(), type.Name);
                    actual.Identifier.Should().Be(expected.Identifier, type.Name);
                }

                // primitive's settings block reached the table
                fromTable.FindSerializerForType(typeof(string)).Manifest("s")
                    .Should().Be(reflected.FindSerializerForType(typeof(string)).Manifest("s"));
            });
        }

        [Fact(DisplayName = "Serialization should resolve every frozen Remote row on a plain system, with dynamic type loading on and off")]
        public async Task Should_resolve_the_frozen_rows_When_a_plain_system_has_no_Remote_rows()
            => await ModuleSerializerSpecs.AssertPlainSystemResolvesFrozenRows("remote-plain", RemoteRows, (s, id) => s.GetSerializerById(id));

        [Fact(DisplayName = "RemoteSerializers should match the rows Remote.conf shipped at 1.6.0-beta1")]
        public void Should_match_the_frozen_rows_When_the_table_is_built()
        {
            var table = new RemoteSerializers();
            ModuleSerializerSpecs.AssertTableMatchesFrozenRows(RemoteRows, table.Create((ExtendedActorSystem)Sys));
        }

        [Fact(DisplayName = "RemoteSerializers should build without throwing on a system that never loaded Remote.conf")]
        public async Task Should_build_without_throwing_When_Remote_conf_is_absent()
            => await ModuleSerializerSpecs.AssertBuildsWithoutModuleConfig("remote-no-config", s => new RemoteSerializers().Create(s));

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

        /// <remarks>
        /// With the rows present, the config is one an application copied from 1.5; without them, it is the
        /// shipped one. Either way the application's own row beats the module default.
        /// </remarks>
        [Theory(DisplayName = "Serialization should let application.conf override a built-in Remote binding when dynamic type loading is off")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_honor_an_application_override_When_it_rebinds_a_Remote_type(bool withCopiedRows)
        {
            var overrides = ConfigurationFactory.ParseString(@"
                akka.actor.serialization-bindings { ""Akka.Actor.Identify, Akka"" = bytes }
                akka.actor.serialization-settings.primitive.use-legacy-behavior = off");

            await ModuleSerializerSpecs.WithSystem("remote-override", overrides, withCopiedRows ? RemoteRows : null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Manifest("s").Should().Be("S");
                // an untouched binding keeps its module default
                serialization.FindSerializerForType(typeof(ActorIdentity)).Should().BeOfType<MiscMessageSerializer>();
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf replace a built-in Remote alias When dynamic type loading is off")]
        public async Task Should_honor_an_alias_override_When_it_replaces_a_Remote_alias()
            => await ModuleSerializerSpecs.AssertAliasOverrideWins("remote-alias-override", "akka-misc",
                typeof(Identify), typeof(IActorRef), typeof(RemoteWatcher.Heartbeat));

        [Fact(DisplayName = "Serialization should probe and register Remote's serializers as defaults even when the config has no Remote rows")]
        public async Task Should_probe_Remote_When_a_local_config_has_no_Remote_rows()
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

                // the module is deployed with the app, so it is built and registered as a default up front,
                // whether or not this config carries a single Remote.conf row
                probes.Should().Be(1);
                // these two rows are explicit overrides, so they still win over Remote's own default binding
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
                // no row overrides this one, so Remote's module default answers it instead of falling back to json
                serialization.FindSerializerForType(typeof(IActorRef)).Should().BeOfType<MiscMessageSerializer>();
            });
        }

        /// <remarks>
        /// Regression: a lone <c>proto</c> row used to build every Remote serializer, including <c>primitive</c>,
        /// to answer it - and <c>PrimitiveSerializers</c> threw on the null config a system that never loaded
        /// Remote.conf hands it. <c>RemoteSerializers.Create</c> must not throw just because this config only
        /// ever asks it for one of its serializers.
        /// </remarks>
        [Fact(DisplayName = "Serialization should start and resolve a lone proto row with no other Remote.conf rows present")]
        public async Task Should_resolve_a_lone_proto_row_When_no_other_Remote_config_is_present()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor.serializers.proto = ""Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote""
                akka.actor.serialization-bindings { ""Google.Protobuf.IMessage, Google.Protobuf"" = proto }");

            await ModuleSerializerSpecs.WithSystem("remote-lone-proto", config, null, system =>
            {
                ((ExtendedActorSystem)system).Serialization.FindSerializerForType(typeof(Google.Protobuf.IMessage))
                    .Should().BeOfType<ProtobufSerializer>();
            });
        }

        /// <remarks>
        /// Regression: a binding-only row used to build Remote's whole module just to answer lookup 1, with the
        /// same throw as above. With the switch on it resolves through plain reflection instead.
        /// </remarks>
        [Fact(DisplayName = "Serialization should start with a RemoteWatcher+Heartbeat binding and no Remote serializer row")]
        public async Task Should_start_With_a_binding_only_RemoteWatcher_Heartbeat_row()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Remote.RemoteWatcher+Heartbeat, Akka.Remote"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("remote-binding-only-heartbeat", config, null, system =>
            {
                ((ExtendedActorSystem)system).Serialization.FindSerializerForType(typeof(RemoteWatcher.Heartbeat))
                    .Should().BeOfType<ByteArraySerializer>();
            });
        }

        /// <remarks>
        /// Remote's module is built as a default up front, with or without a row for it, so a binding-only row
        /// resolves even with the switch off - no Remote.conf serializer row is needed to answer it.
        /// </remarks>
        [Fact(DisplayName = "Serialization should resolve a RemoteWatcher+Heartbeat binding with no Remote serializer row when dynamic type loading is off")]
        public async Task Should_resolve_a_binding_only_row_From_the_Remote_module_default()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { ""Akka.Remote.RemoteWatcher+Heartbeat, Akka.Remote"" = bytes }");

            await ModuleSerializerSpecs.WithSystem("remote-binding-only-heartbeat-off", config, null, system =>
            {
                var serialization = ModuleSerializerSpecs.BuildDefault(system, dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(RemoteWatcher.Heartbeat)).Should().BeOfType<ByteArraySerializer>();
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
