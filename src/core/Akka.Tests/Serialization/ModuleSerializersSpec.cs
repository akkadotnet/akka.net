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
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
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

        private sealed class FakeModule : ModuleSerializers
        {
            public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
            {
                new ModuleSerializer(typeof(FakeSerializer),
                    (system, config) => config.IsNullOrEmpty() ? new FakeSerializer(system) : new FakeSerializer(system, config))
            };

            public override IReadOnlyList<Type> BoundTypes { get; } =
                new[] { typeof(ModuleMessage), typeof(string), typeof(Identify), typeof(PoisonPill) };
        }

        /// <summary>Stands in for a module built against a different Akka: its table's constructor hits a missing member.</summary>
        internal sealed class SkewedModule : ModuleSerializers
        {
            public SkewedModule() => throw new MissingMethodException("Akka.Serialization.Missing", "Member");

            public override IReadOnlyList<ModuleSerializer> Serializers => throw new NotSupportedException();

            public override IReadOnlyList<Type> BoundTypes => throw new NotSupportedException();
        }

        /// <summary>The same skew hit in a static initializer, which arrives wrapped in TypeInitializationException.</summary>
        internal sealed class StaticSkewedModule : ModuleSerializers
        {
            static StaticSkewedModule() => throw new MissingMethodException("Akka.Serialization.Missing", "Member");

            public override IReadOnlyList<ModuleSerializer> Serializers => throw new NotSupportedException();

            public override IReadOnlyList<Type> BoundTypes => throw new NotSupportedException();
        }

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

        [Theory(DisplayName = "Serialization should treat a module whose table fails to load as absent")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+SkewedModule, Akka.Tests")]
        [InlineData("Akka.Tests.Serialization.ModuleSerializersSpec+StaticSkewedModule, Akka.Tests")]
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
            });
        }

        /// <remarks>Only modules this config's serializer rows loaded answer a framework-type row.</remarks>
        [Fact(DisplayName = "Serialization should still reject a System.String binding when no module serializer row is present and dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_framework_type_row_has_no_module()
        {
            var system = ActorSystem.Create("module-none", @"akka.actor.serialization-bindings { ""System.String"" = bytes }");
            try
            {
                var exception = await Assert.ThrowsAsync<ConfigurationException>(
                    () => Build(system, FakeTable(), dynamicTypeLoading: false));
                exception.Message.Should().Contain("[System.String]");
                _probes.Should().Be(0);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Theory(DisplayName = "Serialization should never probe a module when every row hits the built-in tables")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_not_probe_When_every_row_is_built_in(bool dynamicTypeLoading)
        {
            var system = ActorSystem.Create("module-local");
            try
            {
                await Build(system, FakeTable(), dynamicTypeLoading);
                _probes.Should().Be(0);
            }
            finally
            {
                await system.Terminate();
            }
        }
    }
}
