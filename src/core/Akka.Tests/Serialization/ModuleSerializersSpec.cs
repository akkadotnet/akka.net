//-----------------------------------------------------------------------
// <copyright file="ModuleSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// A fake module, keyed as the "Akka.Tests" assembly, stands in for a first-party module's
    /// <see cref="ModuleSerializers"/>. Each system boots with the switch on, so its own
    /// <see cref="AkkaSerialization"/> resolves the rows by reflection; the spec then builds a second one.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ModuleSerializersSpec
    {
        public sealed class ModuleMessage
        {
        }

        public sealed class UnboundMessage
        {
        }

        public class FakeSerializer : SerializerWithStringManifest
        {
            public FakeSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public FakeSerializer(ExtendedActorSystem system, Config config) : base(system) => HasConfig = true;

            public bool HasConfig { get; }

            public override int Identifier => 9311;

            public override string Manifest(object o) => "M";

            public override byte[] ToBinary(object obj) => Array.Empty<byte>();

            public override object FromBinary(byte[] bytes, string manifest) => new ModuleMessage();
        }

        public sealed class SetupSerializer : FakeSerializer
        {
            public SetupSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 9312;
        }

        /// <summary>
        /// A native <see cref="SerializerV2"/> (not wrapped by <see cref="SerializerV1Adapter"/>, unlike
        /// <see cref="FakeSerializer"/>) that reuses FakeSerializer's id (9311) under an alias of its own, to
        /// stand in for an unrelated alias that happens to reuse a module default's id. Needs its own
        /// <see cref="SerializerV2"/> type - two V1 serializers both compare equal as "SerializerV1Adapter"
        /// once adapted, which would hide the type mismatch the override warning looks for.
        /// </summary>
        public sealed class SameIdSerializer : SerializerV2
        {
            public SameIdSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 9311;

            public override string Manifest(object obj) => "M";

            public override int Serialize(object obj, System.Buffers.IBufferWriter<byte> writer) => 0;

            public override object Deserialize(System.Buffers.ReadOnlySequence<byte> bytes, string manifest) => new ModuleMessage();
        }

        private sealed class FakeModule : ModuleSerializers
        {
            public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system)
            {
                // a module can read its own settings block, the same way the real PrimitiveSerializers does
                var config = system.Settings.Config.GetConfig("akka.actor.serialization-settings.fake-module");
                var serializer = config.IsNullOrEmpty() ? new FakeSerializer(system) : new FakeSerializer(system, config);

                return ImmutableHashSet.Create(SerializerDetails.Create("fake-module", serializer,
                    ImmutableHashSet.Create(typeof(ModuleMessage), typeof(string), typeof(Identify), typeof(PoisonPill))));
            }
        }

        /// <summary>Stands in for a module built against a different Akka: its table's constructor hits a missing member.</summary>
        internal sealed class SkewedModule : ModuleSerializers
        {
            public SkewedModule() => throw new MissingMethodException("Akka.Serialization.Missing", "Member");

            public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => throw new NotSupportedException();
        }

        /// <summary>The same skew hit in a static initializer, which arrives wrapped in TypeInitializationException.</summary>
        internal sealed class StaticSkewedModule : ModuleSerializers
        {
            static StaticSkewedModule() => throw new MissingMethodException("Akka.Serialization.Missing", "Member");

            public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => throw new NotSupportedException();
        }

        /// <summary>The same skew, but hit building the serializers instead of loading the table.</summary>
        internal sealed class CreateSkewedModule : ModuleSerializers
        {
            public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system)
                => throw new MissingMethodException("Akka.Serialization.Missing", "Member");
        }

        /// <summary>
        /// A stdout logger that records instead of printing, so a test can see whether
        /// <c>Serialization</c> logged a serializer-override warning. Loaded by name via
        /// <c>akka.stdout-logger-class</c>, the same way a real custom logger is configured.
        /// </summary>
        internal sealed class RecordingLogger : MinimalLogger
        {
            public readonly List<object> Messages = new();

            protected override void Log(object message) => Messages.Add(message);

            public bool HasOverrideWarning =>
                Messages.OfType<LogEvent>().Any(e => e.Message?.ToString()?.Contains("being overriden") == true);
        }

        private const string RecordingLoggerName = "Akka.Tests.Serialization.ModuleSerializersSpec+RecordingLogger, Akka.Tests";

        private const string ModuleMessageName = "Akka.Tests.Serialization.ModuleSerializersSpec+ModuleMessage";

        /// <summary>The rows the fake module's reference.conf would carry.</summary>
        private static readonly Config ModuleConfig = ConfigurationFactory.ParseString($@"
            akka.actor {{
                serializers.fake-module = ""Akka.Tests.Serialization.ModuleSerializersSpec+FakeSerializer, Akka.Tests""
                serialization-bindings {{
                    ""{ModuleMessageName}, Akka.Tests"" = fake-module
                    ""System.String"" = fake-module
                    ""Akka.Actor.Identify, Akka"" = fake-module
                }}
            }}");

        private int _probes;

        private ModuleSerializerTable FakeTable() => TableOf(() =>
        {
            _probes++;
            return new FakeModule();
        });

        private static ModuleSerializerTable TableOf(Func<ModuleSerializers?> load) =>
            new(new Dictionary<string, Func<ModuleSerializers?>> { ["Akka.Tests"] = load });

        private static async Task<AkkaSerialization> Build(ActorSystem system, ModuleSerializerTable table, bool dynamicTypeLoading)
        {
            AkkaSerialization? serialization = null;
            await AkkaFeaturesSpec.WithDynamicTypeLoading(dynamicTypeLoading, () =>
            {
                serialization = new AkkaSerialization((ExtendedActorSystem)system, table);
                return Task.CompletedTask;
            });
            return serialization!;
        }

        private static async Task WithSystem(string name, ActorSystemSetup setup, Func<ActorSystem, Task> body)
        {
            var system = ActorSystem.Create(name, setup);
            try
            {
                await body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static Task WithSystem(string name, Config? config, Func<ActorSystem, Task> body) =>
            WithSystem(name, ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(
                (config ?? Config.Empty).WithFallback(ModuleConfig).WithFallback(ConfigurationFactory.Default()))), body);

        /// <summary>Runs <paramref name="body"/> against a system whose stdout logger is a <see cref="RecordingLogger"/>.</summary>
        private static async Task WithRecordingLogger(string name, Config config, Func<ActorSystem, RecordingLogger, Task> body)
        {
            var withLogger = ConfigurationFactory.ParseString($@"akka.stdout-logger-class = ""{RecordingLoggerName}""")
                .WithFallback(config);

            await WithSystem(name, withLogger, system =>
                body(system, (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger));
        }

        [Fact(DisplayName = "Serialization should resolve a module's serializer and binding rows when dynamic type loading is off")]
        public async Task Should_resolve_module_rows_When_dynamic_type_loading_is_disabled()
        {
            await WithSystem("module-rows-off", (Config?)null, async system =>
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<FakeSerializer>();
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<FakeSerializer>();
                _probes.Should().Be(1);
            });
        }

        [Fact(DisplayName = "Serialization should let application.conf rebind a module's types to another alias when dynamic type loading is off")]
        public async Task Should_honor_a_user_override_When_it_rebinds_a_module_type()
        {
            var overrides = ConfigurationFactory.ParseString($@"
                akka.actor.serialization-bindings {{
                    ""{ModuleMessageName}, Akka.Tests"" = bytes
                    ""System.String"" = bytes
                }}");

            await WithSystem("module-override-off", overrides, async system =>
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
            });
        }

        [Theory(DisplayName = "Serialization should reject a non-module row, or a module type under another name, when dynamic type loading is off")]
        [InlineData(@"akka.actor.serializers.other = ""Akka.Tests.Serialization.ModuleSerializersSpec+SetupSerializer, Akka.Tests""", "akka.actor.serializers.other")]
        [InlineData(@"akka.actor.serialization-bindings { ""Akka.Tests.Serialization.ModuleSerializersSpec+UnboundMessage, Akka.Tests"" = fake-module }", "UnboundMessage, Akka.Tests")]
        [InlineData(@"akka.actor.serialization-bindings { ""Akka.Tests.Serialization.ModuleSerializersSpec+ModuleMessage, Some.Other.Assembly"" = fake-module }", "ModuleMessage, Some.Other.Assembly")]
        [InlineData(@"akka.actor.serialization-bindings { ""Akka.Tests.Serialization.ModuleSerializersSpec+ModuleMessage"" = fake-module }", "ModuleSerializersSpec+ModuleMessage]")]
        public async Task Should_throw_ConfigurationException_When_a_row_is_not_in_the_module_table(string hocon, string expected)
        {
            await WithSystem("module-miss-off", ConfigurationFactory.ParseString(hocon), async system =>
            {
                var exception = await Assert.ThrowsAsync<ConfigurationException>(
                    () => Build(system, FakeTable(), dynamicTypeLoading: false));

                exception.Message.Should().Contain(expected);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
            });
        }

        [Fact(DisplayName = "Serialization should still skip a non-module serializer row a SerializationSetup covers when dynamic type loading is off")]
        public async Task Should_skip_a_setup_covered_alias_When_a_module_table_is_present()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.actor.serializers.mine = ""Akka.Tests.Serialization.ModuleSerializersSpec+FakeSerializer, Some.Other.Assembly""");
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(config.WithFallback(ModuleConfig).WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create("mine", new SetupSerializer(system), ImmutableHashSet.Create(typeof(UnboundMessage))))));

            await WithSystem("module-setup-off", setup, async system =>
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(UnboundMessage)).Should().BeOfType<SetupSerializer>();
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();
            });
        }

        /// <remarks>Type.GetType runs from inside Akka.dll, so reflection accepts a bare Akka.dll name; the table must too.</remarks>
        [Fact(DisplayName = "Serialization should accept a bare Akka.dll type name in a module binding, as reflection does")]
        public async Task Should_accept_a_bare_Akka_type_name_When_a_loaded_module_binds_it()
        {
            var config = ConfigurationFactory.ParseString(@"akka.actor.serialization-bindings { ""Akka.Actor.PoisonPill"" = fake-module }");
            await WithSystem("module-bare-akka", config, async system =>
            {
                ((ExtendedActorSystem)system).Serialization.FindSerializerForType(typeof(PoisonPill)).Should().BeOfType<FakeSerializer>();

                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(PoisonPill)).Should().BeOfType<FakeSerializer>();
            });
        }

        [Theory(DisplayName = "Serialization should build the same serializers from a module table as from reflection when dynamic type loading is on")]
        [InlineData(null)]
        [InlineData("akka.actor.serialization-settings.fake-module.some-setting = 1")]
        public async Task Should_match_reflection_When_dynamic_type_loading_is_enabled(string? settings)
        {
            var config = settings is null ? null : ConfigurationFactory.ParseString(settings);
            await WithSystem("module-parity-on", config, async system =>
            {
                // the system's own Serialization resolved every row through reflection
                var reflected = ((ExtendedActorSystem)system).Serialization;
                var fromTable = await Build(system, FakeTable(), dynamicTypeLoading: true);
                _probes.Should().Be(1);

                foreach (var type in new[] { typeof(ModuleMessage), typeof(string), typeof(Identify) })
                {
                    var expected = (FakeSerializer)reflected.FindSerializerForType(type);
                    var actual = (FakeSerializer)fromTable.FindSerializerForType(type);
                    actual.GetType().Should().Be(expected.GetType());
                    actual.HasConfig.Should().Be(expected.HasConfig).And.Be(settings is not null);
                }
            });
        }

        [Theory(DisplayName = "Serialization should treat a module as absent when loading or building it fails")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+SkewedModule, Akka.Tests")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+StaticSkewedModule, Akka.Tests")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+CreateSkewedModule, Akka.Tests")]
        [InlineData("No.Such.Module, No.Such.Assembly")]
        public async Task Should_treat_the_module_as_absent_When_its_table_fails_to_load(string tableTypeName)
        {
            var table = TableOf(() => ModuleSerializerTable.Load(tableTypeName));
            await WithSystem("module-skew", (Config?)null, async system =>
            {
                // switch on: reflection still resolves the rows
                var serialization = await Build(system, table, dynamicTypeLoading: true);
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();

                // switch off: the ordinary not-built-in error, not a load failure
                var exception = await Assert.ThrowsAsync<ConfigurationException>(() => Build(system, table, dynamicTypeLoading: false));
                exception.Message.Should().Contain("akka.actor.serializers.fake-module");

                // a skew hit while building the serializers stays visible
                if (tableTypeName.Contains(nameof(CreateSkewedModule)))
                    exception.InnerException.Should().BeOfType<MissingMethodException>();
            });
        }

        /// <remarks>
        /// A module that fails with version skew cannot be allowed to vanish silently: with its HOCON rows gone
        /// there is nothing else to tell the user its messages now fall back to other serializers. Startup still
        /// succeeds, since an app that never uses the module has to keep running.
        /// </remarks>
        [Theory(DisplayName = "Serialization should log an error and still start when a deployed module's table fails with version skew")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+SkewedModule, Akka.Tests", true)]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+StaticSkewedModule, Akka.Tests", true)]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+CreateSkewedModule, Akka.Tests", true)]
        [InlineData("No.Such.Module, No.Such.Assembly", false)]
        public async Task Should_log_an_error_When_a_deployed_module_table_is_skewed(string tableTypeName, bool expectError)
        {
            var table = TableOf(() => ModuleSerializerTable.Load(tableTypeName));
            // no HOCON rows for the module anywhere
            var config = ConfigurationFactory.ParseString($@"akka.stdout-logger-class = ""{RecordingLoggerName}""");
            var system = ActorSystem.Create("module-skew-logs", config);
            try
            {
                var logger = (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger;
                // the system's own startup already built a Serialization against this logger
                logger.Messages.Clear();

                var serialization = await Build(system, table, dynamicTypeLoading: false);

                var errors = logger.Messages.OfType<Error>().ToList();
                if (expectError)
                {
                    var error = errors.Should().ContainSingle().Subject;
                    error.Message.ToString().Should().Contain("[Akka.Tests]").And.Contain("not registered");
                    error.Cause.Should().NotBeNull();
                }
                else
                {
                    // the module simply is not deployed: nothing to report
                    errors.Should().BeEmpty();
                }

                // startup carried on with the other serializers
                serialization.FindSerializerForType(typeof(byte[])).Should().BeOfType<ByteArraySerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }

        /// <remarks>Every module the table knows about is built up front, so its own types resolve even without a row for them.</remarks>
        [Fact(DisplayName = "Serialization should resolve a module-owned framework type binding when no module serializer row is present and dynamic type loading is off")]
        public async Task Should_resolve_a_framework_type_row_From_the_module_default_When_no_serializer_row_is_present()
        {
            var system = ActorSystem.Create("module-none", @"akka.actor.serialization-bindings { ""System.String"" = bytes }");
            try
            {
                // the module's own default binding for ModuleMessage still applies; "System.String" is overridden to bytes
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();
                _probes.Should().Be(1);
            }
            finally
            {
                await system.Terminate();
            }
        }

        /// <remarks>A type no loaded module lists still throws like before.</remarks>
        [Fact(DisplayName = "Serialization should still reject a binding row for a type no module lists when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_no_module_lists_the_bound_type()
        {
            var system = ActorSystem.Create("module-unbound",
                @"akka.actor.serialization-bindings { ""System.Int32"" = bytes }");
            try
            {
                var exception = await Assert.ThrowsAsync<ConfigurationException>(
                    () => Build(system, FakeTable(), dynamicTypeLoading: false));
                exception.Message.Should().Contain("[System.Int32]");
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Theory(DisplayName = "Serialization should probe every known module up front, even when every row hits the built-in tables")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_probe_every_module_up_front(bool dynamicTypeLoading)
        {
            var system = ActorSystem.Create("module-local");
            try
            {
                await Build(system, FakeTable(), dynamicTypeLoading);
                _probes.Should().Be(1);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Theory(DisplayName = "Serialization should register a module's rows as defaults with no HOCON rows for it")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_register_module_defaults_When_no_HOCON_rows_are_present(bool dynamicTypeLoading)
        {
            // no ModuleConfig fallback at all - the module has no HOCON rows of its own in this system
            var system = ActorSystem.Create("module-defaults-only");
            try
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading);
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<FakeSerializer>();
                _probes.Should().Be(1);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Serialization should apply default, then HOCON, then Setup precedence for a module-bound type")]
        public async Task Should_apply_default_then_HOCON_then_Setup_precedence()
        {
            // level 1: nothing but the module default binds ModuleMessage - no HOCON rows for it anywhere
            var defaultOnly = ActorSystem.Create("module-precedence-default");
            try
            {
                var serialization = await Build(defaultOnly, FakeTable(), dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<FakeSerializer>();
            }
            finally
            {
                await defaultOnly.Terminate();
            }

            // level 2: a plain HOCON binding row rebinds ModuleMessage to "bytes" - HOCON beats the default
            var hoconConfig = ConfigurationFactory.ParseString($@"
                akka.actor.serialization-bindings {{ ""{ModuleMessageName}, Akka.Tests"" = bytes }}");
            var hoconOnly = ActorSystem.Create("module-precedence-hocon", hoconConfig);
            try
            {
                var serialization = await Build(hoconOnly, FakeTable(), dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<ByteArraySerializer>();
            }
            finally
            {
                await hoconOnly.Terminate();
            }

            // level 3: a SerializationSetup rebinds it again - Setup beats the HOCON row
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(hoconConfig.WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create("setup-wins", new SetupSerializer(system), ImmutableHashSet.Create(typeof(ModuleMessage))))));
            var withSetup = ActorSystem.Create("module-precedence-setup", setup);
            try
            {
                var serialization = await Build(withSetup, FakeTable(), dynamicTypeLoading: false);
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<SetupSerializer>();
            }
            finally
            {
                await withSetup.Terminate();
            }
        }

        [Fact(DisplayName = "Serialization should not log an override warning when HOCON replaces a module default")]
        public async Task Should_not_warn_When_HOCON_replaces_a_module_default()
        {
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serializers.fake-module = ""Akka.Serialization.ByteArraySerializer, Akka""");

            await WithRecordingLogger("module-default-no-warn", overrides, async (system, logger) =>
            {
                await Build(system, FakeTable(), dynamicTypeLoading: false);
                logger.HasOverrideWarning.Should().BeFalse();
            });
        }

        [Fact(DisplayName = "Serialization should still log an override warning when a SerializationSetup replaces a HOCON alias")]
        public async Task Should_warn_When_Setup_replaces_a_HOCON_alias()
        {
            var config = ConfigurationFactory.ParseString($@"
                akka.stdout-logger-class = ""{RecordingLoggerName}""
                akka.actor.serializers.fake-module = ""Akka.Serialization.ByteArraySerializer, Akka""");
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(config.WithFallback(ModuleConfig).WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create("fake-module", new SetupSerializer(system), ImmutableHashSet.Create(typeof(ModuleMessage))))));

            await WithSystem("module-setup-warns", setup, async system =>
            {
                await Build(system, FakeTable(), dynamicTypeLoading: false);
                var logger = (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger;
                logger.HasOverrideWarning.Should().BeTrue();
            });
        }

        [Fact(DisplayName = "Serialization should reuse the already-built module instance when a HOCON row names a built-in type")]
        public async Task Should_reuse_the_built_module_instance_When_a_HOCON_row_names_a_built_in_type()
        {
            await WithSystem("module-reuse", (Config?)null, async system =>
            {
                // ModuleConfig's own "serializers.fake-module" row names the exact same type the module table
                // already built as a default - it must be reused (same instance), not rebuilt
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                var fromDefault = serialization.FindSerializerForType(typeof(ModuleMessage));
                var fromRow = serialization.GetSerializerById(9311); // FakeSerializer.Identifier
                fromRow.Should().BeSameAs(fromDefault);

                _probes.Should().Be(1);
            });
        }

        [Fact(DisplayName = "Serialization should move a module default's bound types when HOCON overrides its alias")]
        public async Task Should_move_a_module_defaults_bound_types_When_HOCON_overrides_its_alias()
        {
            // no ModuleConfig fallback - the module's own rows never reach this config, only the override does
            var overrides = ConfigurationFactory.ParseString(
                @"akka.actor.serializers.fake-module = ""Akka.Serialization.ByteArraySerializer, Akka""");

            var system = ActorSystem.Create("module-alias-override-moves-types", overrides);
            try
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                // the override replaced the "fake-module" alias itself; every type the default bound to it
                // (not just the ones a binding row happens to name) has to follow, the same way it would if a
                // binding row for each type were present and reprocessed the (now overridden) alias
                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(PoisonPill)).Should().BeOfType<ByteArraySerializer>();
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Serialization should still warn when a different alias reuses a module default's id")]
        public async Task Should_warn_When_a_different_alias_reuses_a_module_defaults_id()
        {
            // no HOCON row for "fake-module" at all, so its default alias/id is never touched directly -
            // only "other-alias", which happens to reuse the same id (9311) FakeSerializer has
            var loggerConfig = ConfigurationFactory.ParseString($@"akka.stdout-logger-class = ""{RecordingLoggerName}""");
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(loggerConfig.WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create("other-alias", new SameIdSerializer(system), ImmutableHashSet.Create(typeof(UnboundMessage))))));

            await WithSystem("module-id-reuse-warns", setup, async system =>
            {
                var logger = (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger;
                // the outer system's own startup already built a (real-module) Serialization against this
                // same logger; clear it so the assertion below is about our Build call alone
                logger.Messages.Clear();

                await Build(system, FakeTable(), dynamicTypeLoading: false);
                logger.HasOverrideWarning.Should().BeTrue();
            });
        }

        [Fact(DisplayName = "Serialization should not warn when a HOCON binding row moves one of a default's types after its alias was overridden")]
        public async Task Should_not_warn_When_a_binding_row_moves_a_type_After_its_default_alias_was_overridden()
        {
            // the alias override alone moves every bound type to the override (previous test); a binding row
            // that then moves ONE of those types again must not warn either - it is still replacing a default,
            // just by name instead of via the alias
            var overrides = ConfigurationFactory.ParseString($@"
                akka.stdout-logger-class = ""{RecordingLoggerName}""
                akka.actor.serializers.fake-module = ""Akka.Serialization.ByteArraySerializer, Akka""
                akka.actor.serialization-bindings {{ ""{ModuleMessageName}, Akka.Tests"" = json }}");

            var system = ActorSystem.Create("module-alias-then-binding-override", overrides);
            try
            {
                var logger = (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger;
                logger.Messages.Clear();

                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: true);

                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<NewtonSoftJsonSerializer>();
                // the other three types still followed the alias override, same as before
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<ByteArraySerializer>();
                logger.HasOverrideWarning.Should().BeFalse();
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Serialization should resolve a module default's bound types against the last alias override, not the first")]
        public async Task Should_resolve_a_module_defaults_bound_types_Against_the_last_of_two_alias_overrides()
        {
            // HOCON overrides "fake-module" to ByteArraySerializer first; a SerializationSetup overrides the
            // same alias again to SetupSerializer, whose own UseFor does not include the module's types - they
            // still have to end up on SetupSerializer, not stuck on the first (HOCON) override
            var config = ConfigurationFactory.ParseString($@"
                akka.stdout-logger-class = ""{RecordingLoggerName}""
                akka.actor.serializers.fake-module = ""Akka.Serialization.ByteArraySerializer, Akka""");
            var setup = ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(config.WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create("fake-module", new SetupSerializer(system), ImmutableHashSet.Create(typeof(UnboundMessage))))));

            await WithSystem("module-alias-overridden-twice", setup, async system =>
            {
                var serialization = await Build(system, FakeTable(), dynamicTypeLoading: false);

                serialization.FindSerializerForType(typeof(ModuleMessage)).Should().BeOfType<SetupSerializer>();
                serialization.FindSerializerForType(typeof(string)).Should().BeOfType<SetupSerializer>();
                serialization.FindSerializerForType(typeof(Identify)).Should().BeOfType<SetupSerializer>();
                serialization.FindSerializerForType(typeof(PoisonPill)).Should().BeOfType<SetupSerializer>();
            });
        }
    }
}
