//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerDefaultsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Covers the two serializers and two serialization bindings that Akka.NET's own <c>akka.conf</c>
    /// declares, which <see cref="Serialization"/> now registers from built-in tables instead of through
    /// <see cref="Type.GetType(string)"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="AppContext"/> switches are process-wide, so these specs share
    /// <see cref="DynamicTypeLoadingCollection"/> with everything else that flips
    /// <c>Akka.DynamicTypeLoading</c> and never run beside another spec.
    /// </remarks>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class BuiltInSerializerDefaultsSpec
    {
        /// <summary>
        /// A plain type with no serialization binding of its own, so it can only be serialized by whatever
        /// <c>System.Object</c> is bound to.
        /// </summary>
        public sealed class SomePoco
        {
            public string? Name { get; set; }
        }

        /// <summary>
        /// A serializer that needs no reflection, so it is a legitimate thing to register under AOT - which is
        /// exactly the migration the ledger recommends for a switched-off application.
        /// </summary>
        public sealed class PocoSerializer : SerializerWithStringManifest
        {
            public PocoSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 9301;

            public override string Manifest(object o) => nameof(SomePoco);

            public override byte[] ToBinary(object obj) => Array.Empty<byte>();

            public override object FromBinary(byte[] bytes, string manifest) => new SomePoco();
        }

        /// <summary>
        /// A second plain type, which no <see cref="SerializationSetup"/> in this spec binds.
        /// </summary>
        public sealed class UnboundPoco
        {
        }

        private static ActorSystemSetup PocoSerializerSetup(string alias, Config config, params Type[] useFor) =>
            ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(config.WithFallback(ConfigurationFactory.Default())),
                SerializationSetup.Create(system => ImmutableHashSet<SerializerDetails>.Empty.Add(
                    SerializerDetails.Create(alias, new PocoSerializer(system), ImmutableHashSet.Create(useFor)))));

        /// <summary>
        /// HOCON rows the way a module's reference.conf writes them: a serializer named by a type core does not
        /// know, and a binding to it. <paramref name="boundTypeName"/> is how the binding row spells the type.
        /// </summary>
        private static Config ModuleStyleConfig(string boundTypeName) => ConfigurationFactory.ParseString($@"
            akka.actor {{
                serializers {{
                    poco = ""Akka.Tests.Serialization.SomeModuleSerializer, Akka.Tests""
                }}
                serialization-bindings {{
                    ""{boundTypeName}"" = poco
                }}
            }}");

        public static TheoryData<string> SomePocoSpellings() => new()
        {
            "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco",
            "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco, Akka.Tests",
            "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco ,akka.tests",
            "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco, Akka.Tests, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null"
        };

        private static async Task WithSystem(string name, Config? config, Func<ActorSystem, Task> body)
        {
            var system = config is null ? ActorSystem.Create(name) : ActorSystem.Create(name, config);
            try
            {
                await body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static async Task WithSystemFromSetup(string name, ActorSystemSetup setup, Func<ActorSystem, Task> body)
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

        /// <remarks>
        /// The switch-ON parity guard: with the switch at its shipping default, every input has to resolve to
        /// exactly the serializer it resolved to before the built-in tables existed.
        /// </remarks>
        [Fact(DisplayName = "Serialization should register the default bytes and json serializers when dynamic type loading is on")]
        public async Task Should_register_both_default_serializers_When_dynamic_type_loading_is_enabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, () => WithSystem("builtin-serializers-on", null, system =>
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;

                serialization.FindSerializerForType(typeof(byte[])).Should().BeOfType<ByteArraySerializer>();

                // "System.Object" = json, so an otherwise unbound type falls back to Newtonsoft.Json
                serialization.FindSerializerForType(typeof(SomePoco)).Should().BeOfType<NewtonSoftJsonSerializer>();
                serialization.FindSerializerFor(new SomePoco()).Should().BeOfType<NewtonSoftJsonSerializer>();

                return Task.CompletedTask;
            }));
        }

        [Fact(DisplayName = "Serialization should register bytes but not json when dynamic type loading is off")]
        public async Task Should_register_only_the_byte_array_serializer_When_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () => WithSystem("builtin-serializers-off", null, system =>
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;

                // ByteArraySerializer does no reflection of its own, so "System.Byte[]" = bytes still holds
                serialization.FindSerializerForType(typeof(byte[])).Should().BeOfType<ByteArraySerializer>();

                // Newtonsoft.Json is reflection-driven, so core leaves the json alias and the System.Object
                // binding out entirely rather than registering a serializer that throws on first use
                Assert.Throws<SerializationException>(() => serialization.FindSerializerForType(typeof(SomePoco)));

                var exception = Assert.Throws<SerializationException>(
                    () => serialization.FindSerializerFor(new SomePoco()));
                exception.Message.Should().Contain(nameof(SomePoco));

                // and the message has to say WHY, or it reads like a binding the user forgot to write
                exception.Message.Should().Contain("System.Object");
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                exception.Message.Should().Contain("SerializationSetup");

                return Task.CompletedTask;
            }));
        }

        /// <remarks>
        /// The migration the ledger tells a switched-off application to make. Whether a built-in binding
        /// survives has to be decided by the alias it points at, not by the feature switch: the
        /// <c>System.Object</c> row is dropped when it still points at <c>json</c>, and honored when the user
        /// has pointed it at a serializer of their own.
        /// </remarks>
        [Fact(DisplayName = "Serialization should honor a System.Object binding pointed at a SerializationSetup serializer when dynamic type loading is off")]
        public async Task Should_honor_the_object_binding_When_it_names_a_setup_serializer_and_dynamic_type_loading_is_disabled()
        {
            var config = ConfigurationFactory.ParseString(@"akka.actor.serialization-bindings { ""System.Object"" = mine }");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false,
                () => WithSystemFromSetup("object-binding-setup-off", PocoSerializerSetup("mine", config), system =>
                {
                    var serialization = ((ExtendedActorSystem)system).Serialization;

                    serialization.FindSerializerForType(typeof(SomePoco)).Should().BeOfType<PocoSerializer>();
                    serialization.FindSerializerFor(new SomePoco()).Should().BeOfType<PocoSerializer>();

                    return Task.CompletedTask;
                }));
        }

        /// <remarks>
        /// <para>
        /// The AOT canary fails the build on any warning logged during startup, so a default boot with the
        /// switch off has to be silent. Before the built-in tables it logged four "did not resolve" warnings;
        /// dropping the <c>System.Object</c> row from the feature switch rather than from the alias would just
        /// as easily have left a "Serialization binding to non existing serializer: 'json'" warning behind.
        /// </para>
        /// <para>
        /// Captured off the console rather than through <c>akka.loggers</c> or an <c>EventStream</c> probe: the
        /// warnings in question are logged inside <c>Serialization..ctor</c>, before any configured logger has
        /// started, so they reach <see cref="StandardOutLogger"/> and nothing else. The console is what the
        /// canary reads too.
        /// </para>
        /// </remarks>
        [Fact(DisplayName = "Serialization should log no warnings on a default boot when dynamic type loading is off")]
        public async Task Should_log_no_warnings_When_booting_with_dynamic_type_loading_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var captured = new StringWriter();
                var previousOut = Console.Out;
                try
                {
                    Console.SetOut(captured);
                    await WithSystem("no-warnings-off", null, system =>
                    {
                        // touch serialization so nothing is deferred past the assertion
                        ((ExtendedActorSystem)system).Serialization.FindSerializerForType(typeof(byte[]))
                            .Should().BeOfType<ByteArraySerializer>();
                        return Task.CompletedTask;
                    });
                }
                finally
                {
                    Console.SetOut(previousOut);
                }

                var output = captured.ToString();
                output.Should().NotContain("[WARNING]", "the AOT canary fails the build on any startup warning");
                output.Should().NotContain("[ERROR]");
            });
        }

        [Fact(DisplayName = "Serialization should recognize the built-in serializers and bindings spelled as full assembly-qualified names")]
        public async Task Should_recognize_the_built_in_names_When_they_carry_a_full_assembly_identity()
        {
            // Akka.Hosting writes Type.AssemblyQualifiedName into HOCON, version and all. Rather than carry a
            // versioned key - which would have to be built from a typeof, and would then only ever match this
            // one build - the lookup strips the assembly identity off the configured value first.
            var config = ConfigurationFactory.ParseString($@"
                akka.actor {{
                    serializers {{
                        bytes = ""{typeof(ByteArraySerializer).AssemblyQualifiedName}""
                        json = ""{typeof(NewtonSoftJsonSerializer).AssemblyQualifiedName}""
                    }}
                    serialization-bindings {{
                        ""{typeof(byte[]).AssemblyQualifiedName}"" = bytes
                        ""{typeof(object).AssemblyQualifiedName}"" = json
                    }}
                }}");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () => WithSystem("builtin-serializers-aqn", config, system =>
            {
                var serialization = ((ExtendedActorSystem)system).Serialization;

                serialization.FindSerializerForType(typeof(byte[])).Should().BeOfType<ByteArraySerializer>();
                Assert.Throws<SerializationException>(() => serialization.FindSerializerForType(typeof(SomePoco)));

                return Task.CompletedTask;
            }));
        }

        /// <remarks>
        /// The escape hatch the switch-off error messages point at. A module's reference.conf keeps its
        /// serializer and binding rows even when the application registers that serializer in code, so a
        /// <see cref="SerializationSetup"/> that covers the alias and the bound type has to be enough - the
        /// HOCON rows it covers are skipped instead of rejected. The Setup binds <see cref="SomePoco"/> itself
        /// either way, so what this proves is that <see cref="ActorSystem.Create(string, ActorSystemSetup)"/>
        /// no longer throws; the serializer assertions only confirm the Setup still wins.
        /// </remarks>
        [Theory(DisplayName = "Serialization should accept HOCON serializer and binding rows a SerializationSetup covers when dynamic type loading is off")]
        [MemberData(nameof(SomePocoSpellings))]
        public async Task Should_accept_rows_a_setup_covers_When_dynamic_type_loading_is_disabled(string boundTypeName)
        {
            var setup = PocoSerializerSetup("poco", ModuleStyleConfig(boundTypeName), typeof(SomePoco));

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false,
                () => WithSystemFromSetup("setup-covers-rows-off", setup, system =>
                {
                    var serialization = ((ExtendedActorSystem)system).Serialization;

                    serialization.FindSerializerForType(typeof(SomePoco)).Should().BeOfType<PocoSerializer>();
                    serialization.FindSerializerFor(new SomePoco()).Should().BeOfType<PocoSerializer>();

                    return Task.CompletedTask;
                }));
        }

        [Fact(DisplayName = "Serialization should reject a binding row that names a SerializationSetup type under the wrong assembly when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_binding_row_names_the_wrong_assembly_and_dynamic_type_loading_is_disabled()
        {
            const string boundTypeName = "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco, Some.Other.Assembly";
            var setup = PocoSerializerSetup("poco", ModuleStyleConfig(boundTypeName), typeof(SomePoco));

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("setup-wrong-assembly-off", setup));

                exception.Message.Should().Contain(boundTypeName);
                return Task.CompletedTask;
            });
        }

        /// <remarks>
        /// The serializer-row skip keys on the alias: a Setup that binds the type under an alias of its own
        /// does not excuse a HOCON serializer row core cannot build.
        /// </remarks>
        [Fact(DisplayName = "Serialization should reject a HOCON serializer row whose alias the SerializationSetup does not register when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_setup_registers_a_different_alias_and_dynamic_type_loading_is_disabled()
        {
            var setup = PocoSerializerSetup("mine", ModuleStyleConfig(
                "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco, Akka.Tests"), typeof(SomePoco));

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("setup-other-alias-off", setup));

                exception.Message.Should().Contain("akka.actor.serializers.poco");
                return Task.CompletedTask;
            });
        }

        /// <remarks>
        /// Covering the alias is not enough on its own: a binding row for a type the Setup does not bind would
        /// otherwise vanish, and that type would fail on first use instead of at startup.
        /// </remarks>
        [Fact(DisplayName = "Serialization should reject a binding row for a type the SerializationSetup does not bind when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_setup_covers_the_alias_but_not_the_type_and_dynamic_type_loading_is_disabled()
        {
            const string boundTypeName = "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+UnboundPoco, Akka.Tests";
            var setup = PocoSerializerSetup("poco", ModuleStyleConfig(boundTypeName), typeof(SomePoco));

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("setup-misses-type-off", setup));

                exception.Message.Should().Contain("akka.actor.serialization-bindings");
                exception.Message.Should().Contain(boundTypeName);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Serialization should reject a serializer named in HOCON that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_serializer_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            const string serializerTypeName = "Akka.Tests.Serialization.CustomSerializer, Akka.Tests";
            var config = ConfigurationFactory.ParseString(
                @"akka.actor.serializers.custom = """ + serializerTypeName + @"""");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-serializer-off", config));

                exception.Message.Should().Contain("akka.actor.serializers.custom");
                exception.Message.Should().Contain(serializerTypeName);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Serialization should reject a serialization binding that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_binding_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            const string boundTypeName = "Akka.Tests.Serialization.BuiltInSerializerDefaultsSpec+SomePoco, Akka.Tests";
            var config = ConfigurationFactory.ParseString(
                @"akka.actor.serialization-bindings { """ + boundTypeName + @""" = bytes }");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-binding-off", config));

                exception.Message.Should().Contain("akka.actor.serialization-bindings");
                exception.Message.Should().Contain(boundTypeName);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }
    }
}
