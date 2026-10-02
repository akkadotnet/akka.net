//-----------------------------------------------------------------------
// <copyright file="SerializationV2SwitchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
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
    /// The Serialization V2 switch (<c>akka.actor.serialization-v2</c> / <see cref="SerializationV2Setup"/>), driven by
    /// a fake module keyed as "Akka.Tests": a legacy row, the read-only V2 row that supersedes it, and an unrelated
    /// native row the switch must leave alone. No first-party module ships a V2 row yet.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class SerializationV2SwitchSpec
    {
        private readonly ITestOutputHelper _output;

        public SerializationV2SwitchSpec(ITestOutputHelper output) => _output = output;

        public sealed class ProtocolMessage
        {
            public ProtocolMessage(string value) => Value = value;

            public string Value { get; }
        }

        public sealed class OtherProtocolMessage
        {
            public OtherProtocolMessage(string value) => Value = value;

            public string Value { get; }
        }

        public sealed class NativeMessage
        {
        }

        /// <summary>Stands in for a legacy serializer: writes "legacy:" + value and reads nothing else.</summary>
        public sealed class LegacySerializer : SerializerWithStringManifest
        {
            public const int Id = 9321;

            public LegacySerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => Id;

            public override string Manifest(object o) => o is ProtocolMessage ? "P" : "O";

            public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes("legacy:" + ValueOf(obj));

            public override object FromBinary(byte[] bytes, string manifest) => Read(Encoding.UTF8.GetString(bytes), "legacy:", manifest);
        }

        /// <summary>Stands in for a V2 serializer at legacy id + 40: writes "v2:" + value and reads nothing else.</summary>
        public sealed class V2Serializer : SerializerV2
        {
            public const int Id = LegacySerializer.Id + 40;

            public V2Serializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => Id;

            public override string Manifest(object obj) => obj is ProtocolMessage ? "P" : "O";

            public override int Serialize(object obj, IBufferWriter<byte> writer)
            {
                var bytes = Encoding.UTF8.GetBytes("v2:" + ValueOf(obj));
                writer.Write(bytes);
                return bytes.Length;
            }

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
                => Read(Encoding.UTF8.GetString(bytes.ToArray()), "v2:", manifest);
        }

        /// <summary>Stands in for a native SerializerV2 (Primitive, Persistence) that has no V2 row.</summary>
        public sealed class NativeSerializer : SerializerV2
        {
            public NativeSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 9323;

            public override string Manifest(object obj) => "N";

            public override int Serialize(object obj, IBufferWriter<byte> writer) => 0;

            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => new NativeMessage();
        }

        private static string ValueOf(object obj) => obj switch
        {
            ProtocolMessage p => p.Value,
            OtherProtocolMessage o => o.Value,
            _ => throw new ArgumentException($"Unexpected [{obj.GetType()}]", nameof(obj))
        };

        private static object Read(string text, string prefix, string manifest)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                throw new SerializationException($"Expected [{prefix}] bytes, got [{text}]");

            var value = text.Substring(prefix.Length);
            return manifest == "P" ? new ProtocolMessage(value) : new OtherProtocolMessage(value);
        }

        private const string LegacyAlias = "fake-legacy";
        private const string V2Alias = "fake-legacy-v2";

        private static readonly Type[] LegacyTypes = { typeof(ProtocolMessage), typeof(OtherProtocolMessage) };

        /// <summary>A module with a legacy row, the read-only V2 row that supersedes it, and an unrelated native row.</summary>
        private sealed class FakeModule : ModuleSerializers
        {
            private readonly Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>>? _rows;
            private readonly ImmutableDictionary<string, string> _supersedes;

            public FakeModule(
                Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>>? rows = null,
                ImmutableDictionary<string, string>? supersedes = null)
            {
                _rows = rows;
                _supersedes = supersedes ?? ImmutableDictionary<string, string>.Empty.Add(V2Alias, LegacyAlias);
            }

            public override ImmutableDictionary<string, string> Supersedes => _supersedes;

            public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => _rows?.Invoke(system) ?? Rows(system);

            internal static ImmutableHashSet<SerializerDetails> Rows(ExtendedActorSystem system) => ImmutableHashSet.Create(
                SerializerDetails.Create(LegacyAlias, new LegacySerializer(system), LegacyTypes.ToImmutableHashSet()),
                SerializerDetails.Create(V2Alias, new V2Serializer(system), ImmutableHashSet<Type>.Empty),
                SerializerDetails.Create("fake-native", new NativeSerializer(system), ImmutableHashSet.Create(typeof(NativeMessage))));
        }

        /// <summary>A stdout logger that records instead of printing, loaded by name via <c>akka.stdout-logger-class</c>.</summary>
        internal sealed class RecordingLogger : MinimalLogger
        {
            public readonly List<object> Messages = new();

            protected override void Log(object message) => Messages.Add(message);

            public List<string> V2Lines => Messages.OfType<Info>().Select(i => i.Message.ToString()!)
                .Where(m => m.StartsWith("Serialization V2", StringComparison.Ordinal)).ToList();
        }

        private const string RecordingLoggerName = "Akka.Tests.Serialization.SerializationV2SwitchSpec+RecordingLogger, Akka.Tests";

        private static string Pin(Type type, string alias) =>
            $@"akka.actor.serialization-bindings {{ ""{type.FullName}, Akka.Tests"" = {alias} }}";

        private static ModuleSerializerTable TableOf(ModuleSerializers module) =>
            new(new Dictionary<string, Func<ModuleSerializers?>> { ["Akka.Tests"] = () => module });

        private static async Task<AkkaSerialization> Build(ActorSystem system, bool dynamicTypeLoading, ModuleSerializers? module = null)
        {
            AkkaSerialization? serialization = null;
            await AkkaFeaturesSpec.WithDynamicTypeLoading(dynamicTypeLoading, () =>
            {
                serialization = new AkkaSerialization((ExtendedActorSystem)system, TableOf(module ?? new FakeModule()));
                return Task.CompletedTask;
            });
            return serialization!;
        }

        private static async Task WithSystem(string? hocon, Func<ActorSystem, Task> body, params Setup[] setups)
        {
            var config = ConfigurationFactory.ParseString($@"akka.stdout-logger-class = ""{RecordingLoggerName}""")
                .WithFallback(hocon is null ? Config.Empty : ConfigurationFactory.ParseString(hocon))
                .WithFallback(ConfigurationFactory.Default());
            var setup = ActorSystemSetup.Create(new Setup[] { BootstrapSetup.Create().WithConfig(config) }.Concat(setups).ToArray());

            var system = ActorSystem.Create("serialization-v2-switch", setup);
            try
            {
                await body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static RecordingLogger LoggerOf(ActorSystem system) => (RecordingLogger)((ExtendedActorSystem)system).Settings.StdoutLogger;

        private static void AssertLegacyBindings(AkkaSerialization serialization)
        {
            foreach (var type in LegacyTypes)
                serialization.FindSerializerForType(type).Should().BeOfType<LegacySerializer>();
            serialization.FindSerializerForType(typeof(NativeMessage)).Should().BeOfType<NativeSerializer>();
        }

        private static void AssertV2Bindings(AkkaSerialization serialization)
        {
            foreach (var type in LegacyTypes)
                serialization.FindSerializerForType(type).Should().BeOfType<V2Serializer>();
            serialization.FindSerializerForType(typeof(NativeMessage)).Should().BeOfType<NativeSerializer>();
        }

        /// <summary>Both ids stay registered whatever the switch says, and each reads its own format.</summary>
        private static void AssertBothFormatsRead(AkkaSerialization serialization)
        {
            serialization.GetSerializerById(LegacySerializer.Id).Should().BeOfType<LegacySerializer>();
            serialization.GetSerializerById(V2Serializer.Id).Should().BeOfType<V2Serializer>();

            var fromLegacy = serialization.Deserialize(Encoding.UTF8.GetBytes("legacy:old"), LegacySerializer.Id, "P");
            fromLegacy.Should().BeOfType<ProtocolMessage>().Which.Value.Should().Be("old");

            var fromV2 = serialization.Deserialize(Encoding.UTF8.GetBytes("v2:new"), V2Serializer.Id, "O");
            fromV2.Should().BeOfType<OtherProtocolMessage>().Which.Value.Should().Be("new");
        }

        [Theory(DisplayName = "Should_keep_every_binding_on_its_legacy_row_When_the_switch_is_off")]
        [InlineData(null, true)]
        [InlineData(null, false)]
        [InlineData("akka.actor.serialization-v2 = off", true)]
        [InlineData("akka.actor.serialization-v2 = off", false)]
        public async Task Should_keep_every_binding_on_its_legacy_row_When_the_switch_is_off(string? hocon, bool dynamicTypeLoading)
        {
            await WithSystem(hocon, async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                AssertLegacyBindings(serialization);
                AssertBothFormatsRead(serialization);

                // writes stay on the legacy format
                var bytes = serialization.Serialize(new ProtocolMessage("x"));
                Encoding.UTF8.GetString(bytes).Should().Be("legacy:x");
                LoggerOf(system).V2Lines.Should().BeEmpty();
            });
        }

        [Fact(DisplayName = "Should_ship_the_switch_off_When_reading_the_reference_config")]
        public void Should_ship_the_switch_off_When_reading_the_reference_config()
        {
            var config = ConfigurationFactory.Default();
            config.HasPath(AkkaSerialization.SerializationV2Key).Should().BeTrue();
            config.GetBoolean(AkkaSerialization.SerializationV2Key, true).Should().BeFalse();
        }

        [Theory(DisplayName = "Should_move_exactly_the_legacy_rows_bindings_to_the_V2_row_When_the_switch_is_on")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_move_exactly_the_legacy_rows_bindings_to_the_V2_row_When_the_switch_is_on(bool dynamicTypeLoading)
        {
            await WithSystem("akka.actor.serialization-v2 = on", async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                // every type the legacy row bound, and nothing else, now resolves to the V2 row
                var rows = FakeModule.Rows((ExtendedActorSystem)system);
                var allBound = rows.SelectMany(r => r.UseFor).ToList();
                allBound.Where(t => serialization.FindSerializerForType(t) is V2Serializer)
                    .Should().BeEquivalentTo(rows.Single(r => r.Alias == LegacyAlias).UseFor);
                allBound.Where(t => serialization.FindSerializerForType(t) is LegacySerializer).Should().BeEmpty();

                AssertV2Bindings(serialization);
                AssertBothFormatsRead(serialization);

                // writes switch to the V2 format, under the V2 id
                serialization.FindSerializerFor(new ProtocolMessage("x")).Identifier.Should().Be(V2Serializer.Id);
                var bytes = serialization.Serialize(new ProtocolMessage("x"));
                Encoding.UTF8.GetString(bytes).Should().Be("v2:x");
                serialization.Deserialize(bytes, V2Serializer.Id, "P").Should().BeOfType<ProtocolMessage>().Which.Value.Should().Be("x");
            });
        }

        [Theory(DisplayName = "Should_keep_a_type_on_the_legacy_alias_When_user_HOCON_pins_it_and_the_switch_is_on")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_keep_a_type_on_the_legacy_alias_When_user_HOCON_pins_it_and_the_switch_is_on(bool dynamicTypeLoading)
        {
            var hocon = "akka.actor.serialization-v2 = on\n" + Pin(typeof(ProtocolMessage), LegacyAlias);
            await WithSystem(hocon, async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                serialization.FindSerializerForType(typeof(ProtocolMessage)).Should().BeOfType<LegacySerializer>();
                serialization.FindSerializerForType(typeof(OtherProtocolMessage)).Should().BeOfType<V2Serializer>();
                AssertBothFormatsRead(serialization);
            });
        }

        [Theory(DisplayName = "Should_keep_a_user_HOCON_binding_to_the_V2_alias_When_the_switch_is_off")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_keep_a_user_HOCON_binding_to_the_V2_alias_When_the_switch_is_off(bool dynamicTypeLoading)
        {
            // the other direction: an operator can move one type to V2 by hand while the switch stays off
            await WithSystem(Pin(typeof(ProtocolMessage), V2Alias), async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                serialization.FindSerializerForType(typeof(ProtocolMessage)).Should().BeOfType<V2Serializer>();
                serialization.FindSerializerForType(typeof(OtherProtocolMessage)).Should().BeOfType<LegacySerializer>();
            });
        }

        [Theory(DisplayName = "Should_keep_a_SerializationSetup_binding_When_the_switch_is_on")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_keep_a_SerializationSetup_binding_When_the_switch_is_on(bool dynamicTypeLoading)
        {
            var setup = SerializationSetup.Create(system => ImmutableHashSet.Create(
                SerializerDetails.Create("app-pin", new LegacySerializer(system), ImmutableHashSet.Create(typeof(ProtocolMessage)))));

            await WithSystem("akka.actor.serialization-v2 = on", async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                serialization.FindSerializerForType(typeof(ProtocolMessage)).Should().BeOfType<LegacySerializer>();
                serialization.FindSerializerForType(typeof(OtherProtocolMessage)).Should().BeOfType<V2Serializer>();
            }, setup);
        }

        [Theory(DisplayName = "Should_let_SerializationV2Setup_win_over_HOCON_When_both_set_the_switch")]
        [InlineData("akka.actor.serialization-v2 = on", false, true)]
        [InlineData("akka.actor.serialization-v2 = on", false, false)]
        [InlineData("akka.actor.serialization-v2 = off", true, true)]
        [InlineData("akka.actor.serialization-v2 = off", true, false)]
        [InlineData(null, true, false)]
        public async Task Should_let_SerializationV2Setup_win_over_HOCON_When_both_set_the_switch(string? hocon, bool setupEnabled, bool dynamicTypeLoading)
        {
            await WithSystem(hocon, async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                if (setupEnabled)
                    AssertV2Bindings(serialization);
                else
                    AssertLegacyBindings(serialization);
                AssertBothFormatsRead(serialization);
            }, SerializationV2Setup.Create(setupEnabled));
        }

        [Theory(DisplayName = "Should_move_the_taken_over_bindings_to_the_override_When_user_HOCON_overrides_the_V2_alias")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_move_the_taken_over_bindings_to_the_override_When_user_HOCON_overrides_the_V2_alias(bool dynamicTypeLoading)
        {
            const string hocon = @"
                akka.actor.serialization-v2 = on
                akka.actor.serializers.fake-legacy-v2 = ""Akka.Serialization.ByteArraySerializer, Akka""";
            await WithSystem(hocon, async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                // the #8702 alias-override pass sees the V2 row as the owner of the bindings it took over
                foreach (var type in LegacyTypes)
                    serialization.FindSerializerForType(type).Should().BeOfType<ByteArraySerializer>();
                serialization.FindSerializerForType(typeof(NativeMessage)).Should().BeOfType<NativeSerializer>();
            });
        }

        [Theory(DisplayName = "Should_move_bindings_with_the_alias_that_owns_them_When_user_HOCON_overrides_the_legacy_alias")]
        [InlineData("off", true)]
        [InlineData("off", false)]
        [InlineData("on", true)]
        [InlineData("on", false)]
        public async Task Should_move_bindings_with_the_alias_that_owns_them_When_user_HOCON_overrides_the_legacy_alias(string switchValue, bool dynamicTypeLoading)
        {
            var hocon = $@"
                akka.actor.serialization-v2 = {switchValue}
                akka.actor.serializers.fake-legacy = ""Akka.Serialization.ByteArraySerializer, Akka""";
            await WithSystem(hocon, async system =>
            {
                var serialization = await Build(system, dynamicTypeLoading);

                // off: the legacy alias owns its bindings, so they follow the override (as before the switch existed);
                // on: the V2 row owns them, so overriding the legacy alias leaves them on V2
                foreach (var type in LegacyTypes)
                {
                    if (switchValue == "off")
                        serialization.FindSerializerForType(type).Should().BeOfType<ByteArraySerializer>();
                    else
                        serialization.FindSerializerForType(type).Should().BeOfType<V2Serializer>();
                }
            });
        }

        [Fact(DisplayName = "Should_log_one_line_naming_each_takeover_When_the_switch_is_on")]
        public async Task Should_log_one_line_naming_each_takeover_When_the_switch_is_on()
        {
            var hocon = "akka.actor.serialization-v2 = on\n" + Pin(typeof(ProtocolMessage), LegacyAlias);
            await WithSystem(hocon, async system =>
            {
                var logger = LoggerOf(system);
                logger.Messages.Clear();

                await Build(system, dynamicTypeLoading: false);
                // a rebuild with the same outcome (a module injecting config) does not repeat the line
                await Build(system, dynamicTypeLoading: false);

                var line = logger.V2Lines.Should().ContainSingle().Subject;
                _output.WriteLine(line);
                line.Should().Contain($"[{LegacyAlias}] -> [{V2Alias}] (1 of 2 bindings, 1 kept by user configuration)");
            });
        }

        [Fact(DisplayName = "Should_log_that_nothing_moved_When_the_switch_is_on_and_no_module_has_a_V2_row")]
        public async Task Should_log_that_nothing_moved_When_the_switch_is_on_and_no_module_has_a_V2_row()
        {
            await WithSystem("akka.actor.serialization-v2 = on", async system =>
            {
                // the system's own startup logged the line already (no first-party module has a V2 row yet); the
                // rebuild below has the same outcome, so it is not repeated
                var logger = LoggerOf(system);

                var module = new FakeModule(supersedes: ImmutableDictionary<string, string>.Empty);
                var serialization = await Build(system, dynamicTypeLoading: false, module);

                AssertLegacyBindings(serialization);
                var line = logger.V2Lines.Should().ContainSingle().Subject;
                _output.WriteLine(line);
                line.Should().Contain("no deployed module has a V2 serializer yet");
            });
        }

        private static FakeModule BrokenModule(string defect) => defect switch
        {
            // the V2 row binds a type of its own instead of shipping read-only
            "v2-binds-types" => new FakeModule(rows: system => ImmutableHashSet.Create(
                SerializerDetails.Create(LegacyAlias, new LegacySerializer(system), ImmutableHashSet.Create(typeof(ProtocolMessage))),
                SerializerDetails.Create(V2Alias, new V2Serializer(system), ImmutableHashSet.Create(typeof(OtherProtocolMessage))))),
            // the declaration names an alias the table doesn't have
            "missing-alias" => new FakeModule(supersedes: ImmutableDictionary<string, string>.Empty.Add(V2Alias, "no-such-alias")),
            // a chain: the V2 row is itself superseded
            "chain" => new FakeModule(supersedes: ImmutableDictionary<string, string>.Empty.Add(V2Alias, LegacyAlias).Add("fake-native", V2Alias)),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        [Theory(DisplayName = "Should_fail_startup_When_a_module_declares_an_invalid_takeover")]
        [InlineData("v2-binds-types", "binds types of its own")]
        [InlineData("missing-alias", "has no row")]
        [InlineData("chain", "is a V2 row itself or is already superseded")]
        public async Task Should_fail_startup_When_a_module_declares_an_invalid_takeover(string defect, string expected)
        {
            // checked whatever the switch says, so a broken declaration fails every test run of its module
            await WithSystem(null, async system =>
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => Build(system, dynamicTypeLoading: false, BrokenModule(defect)));
                _output.WriteLine(error.Message);
                error.Message.Should().Contain(expected);
            });
        }
    }
}
